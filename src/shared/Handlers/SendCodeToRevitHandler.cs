using System;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

namespace RvtMcp.Plugin.Handlers
{
    public class SendCodeToRevitHandler : IRevitCommand
    {
        public string Name => "send_code_to_revit";
        public string Description => "Send C# code to Revit to compile and execute dynamically";
        public string ParametersSchema => @"{""type"":""object"",""properties"":{""code"":{""type"":""string"",""description"":""C# code to compile and execute in Revit""}},""required"":[""code""]}";

        public CommandResult Execute(UIApplication app, string paramsJson)
        {
            var request = JObject.Parse(paramsJson);
            var code = request.Value<string>("code");

            if (string.IsNullOrWhiteSpace(code))
                return Fail("code parameter is required.");

            var entries = LiveEntry.ParseAll(RvtMcpConfig.LiveEntriesFromFile());

            var stale = SendCodeStaleAssemblyGuard.RefusalOrNull(code, entries.Select(e => e.NamespacePrefix));
            if (stale != null)
                return Fail(stale);

            try
            {
                var fullCode = SendCodeSourceBuilder.Build(code);

                // Each entry is asked before compiling, so a build finished since the last click is staged now
                // and the snippet compiles against it; no entry configured leaves every step as it was.
                var resolutions = entries
                    .Select(entry => LiveEntryResolver.Resolve(entry, AppDomain.CurrentDomain.GetAssemblies()))
                    .ToList();

                var compiled = LiveSnippetCompiler.Compile(fullCode, AppDomain.CurrentDomain.GetAssemblies(), resolutions);

                if (compiled.RefusalOrNull != null)
                    return Fail(compiled.RefusalOrNull);

                if (!compiled.Success)
                    return Fail("Compilation failed:\n" + string.Join("\n", compiled.Errors));

                var assembly = compiled.Stage.Count == 0
                    ? Assembly.Load(compiled.Image)
                    : SnippetBinding.Load(compiled.Image, compiled.Stage);
                var type = assembly.GetType("McpDynamicScript");

                if (type == null)
                    return Fail("Class 'McpDynamicScript' not found. Ensure your code defines this class with a static Run(UIApplication) method.");

                var method = type.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);
                if (method == null)
                    return Fail("Method 'Run(UIApplication)' not found in McpDynamicScript.");

                var output = method.Invoke(null, new object[] { app });
                // Live wire returns raw output so the agent can read structured JSON
                // (anonymous objects serialize properly, strings stay strings, numbers stay numbers).
                // Persistence paths (McpLogger, McpSessionLog, JournalEntry, bake suggestions, usage
                // events) call BakeRedactor independently at write time, so logs/bakes remain redacted.
                var live = LiveEvidence.FromUsedOrNull(compiled.Used);
                if (live == null)
                {
                    return CommandResult.Ok(new
                    {
                        executed = true,
                        result = output
                    });
                }

                return CommandResult.Ok(new
                {
                    executed = true,
                    result = output,
                    live
                });
            }
            catch (TargetInvocationException ex)
            {
                return Fail($"Runtime error: {ex.InnerException?.Message ?? ex.Message}");
            }
            catch (Exception ex)
            {
                return Fail($"Error: {ex.Message}");
            }
        }

        private static CommandResult Fail(string error)
        {
            return CommandResult.Fail(McpResponsePrivacy.RedactErrorForResponse(error));
        }
    }
}
