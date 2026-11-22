using System.Collections.Generic;

namespace RvtMcp.Plugin
{
    public class CommandDispatcher
    {
        private readonly Dictionary<string, IRevitCommand> _commands = new Dictionary<string, IRevitCommand>();

        public CommandDispatcher()
        {
            Register(new Handlers.GetCurrentViewHandler());
            Register(new Handlers.SendCodeToRevitHandler());
            Register(new Handlers.OpenModelHandler());
            Register(new Handlers.ListRecentModelsHandler());
            Register(new Handlers.ExportPdfHandler());
            Register(new Handlers.ExportImageHandler());
            Register(new Handlers.ListSheetsHandler());
            Register(new Handlers.CaptureViewImageHandler());
            Register(new Handlers.ActivateViewHandler());

            // Commands other add-ins register in the same Revit process (AppDomain registry)
            Register(new Handlers.ListAddinCommandsHandler());
            Register(new Handlers.CallAddinCommandHandler());
        }

        public void Register(IRevitCommand command)
        {
            _commands[command.Name] = command;
        }

        public IRevitCommand GetCommand(string name)
        {
            _commands.TryGetValue(name, out var command);
            return command;
        }
    }
}
