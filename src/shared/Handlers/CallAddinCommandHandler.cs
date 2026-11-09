using Autodesk.Revit.UI;

namespace RvtMcp.Plugin.Handlers
{
    /// <summary>
    /// Runs one command of another add-in (AddinCommandRegistry) and returns that add-in's response envelope unchanged.
    /// read_only=true refuses a command its add-in declares readOnly=false before it runs.
    /// </summary>
    public class CallAddinCommandHandler : IRevitCommand
    {
        public string Name => "call_addin_command";
        public string Description => "Run a command another add-in registered in this Revit session, by add-in name and command name, with JSON args. Returns the add-in's response envelope { ok, result | error }.";
        public string ParametersSchema => @"{""type"":""object"",""properties"":{""addin"":{""type"":""string""},""command"":{""type"":""string""},""args"":{""type"":""object""},""entry"":{""type"":""string"",""enum"":[""mcp"",""cli""]},""read_only"":{""type"":""boolean""}},""required"":[""addin"",""command""]}";

        public CommandResult Execute(UIApplication app, string paramsJson)
        {
            var outcome = AddinCommandRegistry.Call(paramsJson);
            return outcome.Success ? CommandResult.Ok(outcome.Data) : CommandResult.Fail(outcome.Error);
        }
    }
}
