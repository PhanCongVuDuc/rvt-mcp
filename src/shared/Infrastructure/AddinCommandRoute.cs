using System;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace RvtMcp.Plugin
{
    /// <summary>
    /// The two add-in tools (list_addin_commands / call_addin_command) run on a thread-pool thread, not in the plugin's
    /// ExternalEvent: the add-in decides itself whether a command needs Revit's thread and goes there on its own (Sonny.Revit
    /// through Revit.Async). Revit's thread is then never held while an add-in computes, and an add-in that does go to Revit's
    /// thread is not waiting for the ExternalEvent this plugin already holds. Every other tool is enqueued as before.
    /// No Revit type here, so the tests link this file.
    /// </summary>
    public static class AddinCommandRoute
    {
        public const string ListCommand = "list_addin_commands";
        public const string CallCommand = "call_addin_command";

        public static bool Takes(string commandName) =>
            string.Equals(commandName, ListCommand, StringComparison.Ordinal) ||
            string.Equals(commandName, CallCommand, StringComparison.Ordinal);

        /// <summary>
        /// Runs <paramref name="run"/> on the thread pool and completes <paramref name="tcs"/> with its response when
        /// <paramref name="commandName"/> is an add-in tool; returns false, having done nothing, for every other tool, which the
        /// caller enqueues for the ExternalEvent. <paramref name="run"/> goes through the same read-only check, call log and toast as
        /// the ExternalEvent path (McpEventHandler.RunAddinCommand).
        /// </summary>
        public static bool TryRun(string commandName, TaskCompletionSource<string> tcs, Func<string> run, string id = null)
        {
            if (!Takes(commandName))
                return false;

            Task.Run(() =>
            {
                try
                {
                    tcs.TrySetResult(run());
                }
                catch (Exception ex)
                {
                    // Nothing may be left unanswered: the server would wait out its own time limit for this call.
                    tcs.TrySetResult(JsonConvert.SerializeObject(new { id, success = false, error = ex.Message }));
                }
            });
            return true;
        }
    }
}
