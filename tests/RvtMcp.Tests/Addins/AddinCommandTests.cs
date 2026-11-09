using System;
using System.Collections.Concurrent;
using Newtonsoft.Json.Linq;
using RvtMcp.Plugin;
using Xunit;

namespace RvtMcp.Tests.Addins
{
    /// <summary>
    /// Add-in commands (list_addin_commands / call_addin_command): another add-in in the same Revit process puts a
    /// ConcurrentDictionary&lt;string, Func&lt;string, string&gt;&gt; under the AppDomain key; no Revit needed to test it.
    /// </summary>
    [Collection(AddinRegistryCollection.Name)]
    public sealed class AddinCommandTests : IDisposable
    {
        private const string Catalog = @"{""ok"":true,""result"":{""addin"":""Sonny.Revit"",""version"":""0.1.0"",""protocol"":""1"",""commands"":[
            {""name"":""app.ping"",""description"":""ping"",""readOnly"":true,""argsSchema"":{""type"":""object"",""properties"":{}}},
            {""name"":""walls.count"",""description"":""count"",""readOnly"":true,""argsSchema"":{""type"":""object"",""properties"":{""level"":{""type"":""string""}},""required"":[""level""]}},
            {""name"":""walls.delete"",""description"":""delete"",""readOnly"":false,""argsSchema"":{""type"":""object"",""properties"":{}}}]}}";

        public AddinCommandTests()
        {
            AppDomain.CurrentDomain.SetData(AddinCommandRegistry.Key, null);
        }

        public void Dispose()
        {
            AppDomain.CurrentDomain.SetData(AddinCommandRegistry.Key, null);
        }

        [Fact]
        public void The_registry_key_is_the_contract_one()
        {
            Assert.Equal("Bimwright.RvtMcp.AddinCommands.v1", AddinCommandRegistry.Key);
        }

        [Fact]
        public void No_addin_lists_an_empty_array()
        {
            var outcome = AddinCommandRegistry.List();

            Assert.True(outcome.Success);
            Assert.Empty((JArray)outcome.Data["addins"]);
        }

        [Fact]
        public void Lists_the_catalog_of_each_registered_addin()
        {
            Register("Sonny.Revit", Fake());

            var addins = (JArray)AddinCommandRegistry.List().Data["addins"];

            var catalog = Assert.Single(addins);
            Assert.Equal("Sonny.Revit", catalog.Value<string>("addin"));
            Assert.Equal(3, ((JArray)catalog["commands"]).Count);
        }

        [Fact]
        public void A_registry_set_after_the_plugin_started_is_still_seen()
        {
            Assert.Empty((JArray)AddinCommandRegistry.List().Data["addins"]);

            Register("Sonny.Revit", Fake());

            Assert.Single((JArray)AddinCommandRegistry.List().Data["addins"]);
        }

        [Fact]
        public void Calls_the_addin_with_the_envelope_and_returns_its_answer_unchanged()
        {
            string sent = null;
            Register("Sonny.Revit", Fake(request =>
            {
                sent = request;
                return @"{""ok"":true,""result"":{""level"":""Tầng 1"",""count"":12}}";
            }));

            var outcome = AddinCommandRegistry.Call(@"{""addin"":""Sonny.Revit"",""command"":""walls.count"",""args"":{""level"":""Tầng 1""},""entry"":""cli""}");

            Assert.True(outcome.Success);
            Assert.Equal(12, outcome.Data["result"].Value<int>("count"));
            var envelope = JObject.Parse(sent);
            Assert.Equal("walls.count", envelope.Value<string>("command"));
            Assert.Equal("Tầng 1", envelope["args"].Value<string>("level"));
            Assert.Equal("cli", envelope.Value<string>("entry"));
        }

        [Fact]
        public void Missing_args_and_entry_default_to_an_empty_object_and_mcp()
        {
            string sent = null;
            Register("Sonny.Revit", Fake(request =>
            {
                sent = request;
                return @"{""ok"":true,""result"":{}}";
            }));

            AddinCommandRegistry.Call(@"{""addin"":""Sonny.Revit"",""command"":""app.ping""}");

            var envelope = JObject.Parse(sent);
            Assert.Equal(new JObject(), envelope["args"]);
            Assert.Equal("mcp", envelope.Value<string>("entry"));
        }

        [Fact]
        public void An_addin_error_envelope_is_returned_as_it_came()
        {
            Register("Sonny.Revit", Fake(_ => @"{""ok"":false,""error"":{""code"":""unknown_command"",""message"":""x"",""args"":[""walls.nope""]}}"));

            var outcome = AddinCommandRegistry.Call(@"{""addin"":""Sonny.Revit"",""command"":""walls.nope""}");

            Assert.True(outcome.Success);
            Assert.False(outcome.Data.Value<bool>("ok"));
            Assert.Equal("unknown_command", outcome.Data["error"].Value<string>("code"));
        }

        [Fact]
        public void An_addin_that_is_not_loaded_is_addin_not_loaded_with_the_list()
        {
            Register("Other.Addin", Fake());

            var outcome = AddinCommandRegistry.Call(@"{""addin"":""Sonny.Revit"",""command"":""app.ping""}");

            Assert.False(outcome.Success);
            Assert.StartsWith("addin_not_loaded", outcome.Error);
            Assert.Contains("Other.Addin", outcome.Error);
        }

        [Fact]
        public void No_registry_at_all_is_addin_not_loaded()
        {
            var outcome = AddinCommandRegistry.Call(@"{""addin"":""Sonny.Revit"",""command"":""app.ping""}");

            Assert.False(outcome.Success);
            Assert.StartsWith("addin_not_loaded", outcome.Error);
        }

        [Fact]
        public void Read_only_blocks_a_command_that_is_not_read_only_and_never_runs_it()
        {
            var ran = false;
            Register("Sonny.Revit", Fake(_ =>
            {
                ran = true;
                return @"{""ok"":true,""result"":{}}";
            }));

            var outcome = AddinCommandRegistry.Call(@"{""addin"":""Sonny.Revit"",""command"":""walls.delete"",""read_only"":true}");

            Assert.False(outcome.Success);
            Assert.StartsWith("read_only_blocked", outcome.Error);
            Assert.False(ran);
        }

        [Fact]
        public void Read_only_lets_a_read_only_command_through()
        {
            Register("Sonny.Revit", Fake(_ => @"{""ok"":true,""result"":{""count"":12}}"));

            var outcome = AddinCommandRegistry.Call(@"{""addin"":""Sonny.Revit"",""command"":""walls.count"",""args"":{""level"":""Tầng 1""},""read_only"":true}");

            Assert.True(outcome.Success);
        }

        [Fact]
        public void Another_protocol_is_addin_protocol_mismatch()
        {
            var catalog = JObject.Parse(Catalog);
            catalog["result"]["protocol"] = "2";
            Register("Sonny.Revit", Fake(_ => @"{""ok"":true,""result"":{}}", catalog.ToString()));

            var outcome = AddinCommandRegistry.Call(@"{""addin"":""Sonny.Revit"",""command"":""app.ping""}");

            Assert.False(outcome.Success);
            Assert.StartsWith("addin_protocol_mismatch", outcome.Error);
        }

        [Fact]
        public void An_addin_that_throws_gives_a_sanitized_error()
        {
            Register("Sonny.Revit", Fake(_ => throw new InvalidOperationException(@"failed in C:\Users\someone\secret\model.rvt")));

            var outcome = AddinCommandRegistry.Call(@"{""addin"":""Sonny.Revit"",""command"":""app.ping""}");

            Assert.False(outcome.Success);
            Assert.DoesNotContain(@"C:\Users\someone", outcome.Error);
            Assert.Contains("model.rvt", outcome.Error);
        }

        [Fact]
        public void A_registry_another_addin_created_is_reused_not_replaced()
        {
            var registry = new ConcurrentDictionary<string, Func<string, string>>();
            AppDomain.CurrentDomain.SetData(AddinCommandRegistry.Key, registry);
            registry["Sonny.Revit"] = Fake();

            Assert.Single((JArray)AddinCommandRegistry.List().Data["addins"]);
            Assert.Same(registry, AppDomain.CurrentDomain.GetData(AddinCommandRegistry.Key));
        }

        private static void Register(string addin, Func<string, string> function)
        {
            var registry = AppDomain.CurrentDomain.GetData(AddinCommandRegistry.Key) as ConcurrentDictionary<string, Func<string, string>>;
            if (registry == null)
            {
                registry = new ConcurrentDictionary<string, Func<string, string>>();
                AppDomain.CurrentDomain.SetData(AddinCommandRegistry.Key, registry);
            }

            registry[addin] = function;
        }

        private static Func<string, string> Fake(Func<string, string> run = null, string catalog = Catalog)
        {
            return request =>
            {
                var command = JObject.Parse(request).Value<string>("command");
                if (command == "_catalog") return catalog;
                return run == null ? @"{""ok"":true,""result"":{}}" : run(request);
            };
        }
    }

    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class AddinRegistryCollection
    {
        public const string Name = "AppDomain add-in registry";
    }
}
