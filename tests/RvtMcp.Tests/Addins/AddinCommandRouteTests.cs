using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RvtMcp.Plugin;
using Xunit;

namespace RvtMcp.Tests.Addins
{
    /// <summary>
    /// The two add-in tools run on a pool thread, off the ExternalEvent queue; every other tool still goes to the queue; the read-only
    /// policy still holds for them (Sonny.Revit spec 003, FR-043 / FR-046).
    /// </summary>
    [Collection(AddinRegistryCollection.Name)]
    public sealed class AddinCommandRouteTests : IDisposable
    {
        private const string Catalog = @"{""ok"":true,""result"":{""addin"":""Sonny.Revit"",""version"":""0.1.0"",""protocol"":""1"",""commands"":[
            {""name"":""walls.count"",""description"":""count"",""readOnly"":true,""argsSchema"":{""type"":""object"",""properties"":{}}},
            {""name"":""walls.delete"",""description"":""delete"",""readOnly"":false,""argsSchema"":{""type"":""object"",""properties"":{}}}]}}";

        public AddinCommandRouteTests()
        {
            AppDomain.CurrentDomain.SetData(AddinCommandRegistry.Key, null);
        }

        public void Dispose()
        {
            AppDomain.CurrentDomain.SetData(AddinCommandRegistry.Key, null);
        }

        [Theory]
        [InlineData("list_addin_commands")]
        [InlineData("call_addin_command")]
        public async Task An_addin_tool_runs_off_the_queue_on_another_thread(string tool)
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var caller = Thread.CurrentThread.ManagedThreadId;
            var insideTryRun = true;
            var ranInline = false;
            var ranOnPool = false;

            var taken = AddinCommandRoute.TryRun(tool, tcs, () =>
            {
                // The caller is itself a pool thread: once it awaits, the pool may hand it this work, so a matching id alone
                // is not a failure. Running inside TryRun on the caller is.
                ranInline = Volatile.Read(ref insideTryRun) && Thread.CurrentThread.ManagedThreadId == caller;
                ranOnPool = Thread.CurrentThread.IsThreadPoolThread;
                return "{\"success\":true}";
            });
            Volatile.Write(ref insideTryRun, false);

            Assert.True(taken);
            Assert.Equal("{\"success\":true}", await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.False(ranInline);
            Assert.True(ranOnPool);
        }

        [Theory]
        [InlineData("get_current_view_info")]
        [InlineData("send_code_to_revit")]
        [InlineData("batch_execute")]
        public void Every_other_tool_is_left_to_the_queue(string tool)
        {
            var tcs = new TaskCompletionSource<string>();
            var ran = false;

            var taken = AddinCommandRoute.TryRun(tool, tcs, () =>
            {
                ran = true;
                return "{}";
            });

            Assert.False(taken);
            Assert.False(ran);
            Assert.False(tcs.Task.IsCompleted);
        }

        [Fact]
        public async Task A_failing_run_still_answers()
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            AddinCommandRoute.TryRun("call_addin_command", tcs, () => throw new InvalidOperationException("boom"));

            var answer = JObject.Parse(await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.False(answer.Value<bool>("success"));
            Assert.Equal("boom", answer.Value<string>("error"));
        }

        // Every year's plugin takes the add-in tools off the queue before it enqueues a request for the ExternalEvent (FR-046).
        [Theory]
        [InlineData("plugin-r22")]
        [InlineData("plugin-r23")]
        [InlineData("plugin-r24")]
        [InlineData("plugin-r25")]
        [InlineData("plugin-r26")]
        [InlineData("plugin-r27")]
        public void Every_plugin_routes_the_addin_tools_before_the_queue(string plugin)
        {
            var source = ReadPluginApp(plugin);

            var route = source.IndexOf("AddinCommandRoute.TryRun(pending.CommandName, tcs, () => _handler.RunAddinCommand(pending)", StringComparison.Ordinal);
            var enqueue = source.IndexOf("_handler.Enqueue(pending);", StringComparison.Ordinal);

            Assert.True(route >= 0, plugin + " does not route the add-in tools off the ExternalEvent");
            Assert.True(enqueue > route, plugin + " enqueues before it routes the add-in tools");
            Assert.Equal(1, CountOf(source, "_handler.Enqueue("));
        }

        private static string ReadPluginApp(string plugin, [CallerFilePath] string testFile = "")
        {
            var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(testFile)!, "..", "..", ".."));
            return File.ReadAllText(Path.Combine(root, "src", plugin, "App.cs"));
        }

        private static int CountOf(string text, string part)
        {
            var count = 0;
            for (var at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal))
                count++;
            return count;
        }

        private static void Register(Func<string, string> function)
        {
            var registry = new ConcurrentDictionary<string, Func<string, string>>();
            registry["Sonny.Revit"] = function;
            AppDomain.CurrentDomain.SetData(AddinCommandRegistry.Key, registry);
        }
    }
}
