using Autodesk.Revit.UI;

namespace RvtMcp.Plugin.Handlers
{
    /// <summary>The commands other add-ins registered in this Revit session (AddinCommandRegistry).</summary>
    public class ListAddinCommandsHandler : IRevitCommand
    {
        public string Name => "list_addin_commands";
        public string Description => "List the commands other add-ins registered in this Revit session: per add-in its name, version, protocol and commands (name, description, readOnly, argsSchema, resultSchema).";
        public string ParametersSchema => @"{""type"":""object"",""properties"":{}}";

        public CommandResult Execute(UIApplication app, string paramsJson)
        {
            var outcome = AddinCommandRegistry.List();
            return outcome.Success ? CommandResult.Ok(outcome.Data) : CommandResult.Fail(outcome.Error);
        }
    }
}
