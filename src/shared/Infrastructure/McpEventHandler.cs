using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;


namespace RvtMcp.Plugin
{
    public class PendingRequest
    {
        public string Id { get; set; }
        public string CommandName { get; set; }
        public string ParamsJson { get; set; }
        public RvtMcpConfig RuntimeOptions { get; set; }
        public ChangeHistoryRequest History { get; set; }
        public TaskCompletionSource<string> Tcs { get; set; }
    }

    public class McpEventHandler : IExternalEventHandler
    {
        private readonly ConcurrentQueue<PendingRequest> _queue = new ConcurrentQueue<PendingRequest>();
        private readonly CommandDispatcher _dispatcher;
        private readonly McpSessionLog _sessionLog;
        // Revit's UI thread, where the handler is made (App.OnStartup): the History window's rows are WPF, so an add-in
        // tool answered on a pool thread reports its outcome back here (RunAddinCommand).
        private readonly System.Windows.Threading.Dispatcher _ui = System.Windows.Threading.Dispatcher.CurrentDispatcher;

        public McpEventHandler(CommandDispatcher dispatcher, McpSessionLog sessionLog)
        {
            _dispatcher = dispatcher;
            _sessionLog = sessionLog;

            try
            {
                var config = RvtMcpConfig.Load(args: null);
                SendCodeJournal.RunMaintenance(config);
            }
            catch { }
        }

        public void Enqueue(PendingRequest request)
        {
            _queue.Enqueue(request);
        }

        public void Execute(UIApplication app)
        {
            while (_queue.TryDequeue(out var request))
            {
                // Stale command guard: skip if TCS already completed (timeout/cancel)
                if (request.Tcs.Task.IsCompleted)
                    continue;

                var sw = Stopwatch.StartNew();
                var runtimeConfig = new RvtMcpConfig();
                JObject changes = null;
                JObject history = null;
                JObject historyMarker = null;
                try
                {
                    runtimeConfig = RvtMcpConfig.LoadReadOnly().WithRuntimeOptions(request.RuntimeOptions);
                    var rejection = ToolReadPolicy.Rejection(request.CommandName, runtimeConfig.ReadOnlyOrDefault, runtimeConfig.EnableSendCodeOrDefault);
                    if (rejection != null) throw new InvalidOperationException(rejection);
                    var command = _dispatcher.GetCommand(request.CommandName);
                    if (command == null)
                    {
                        sw.Stop();
                        var unknownError = McpResponsePrivacy.RedactErrorForResponse($"Unknown command: {request.CommandName}");
                        McpLogger.Log(request.CommandName, request.ParamsJson, false,
                                      sw.ElapsedMilliseconds, unknownError, enabled: runtimeConfig.EnableCallLogOrDefault);
                        _sessionLog?.Add(new McpCallEntry
                        {
                            ToolName = request.CommandName,
                            ParamsJson = request.ParamsJson,
                            Success = false,
                            DurationMs = sw.ElapsedMilliseconds,
                            ErrorMessage = unknownError,
                            Summary = Localization.L.T("history.summary.unknown", ("tool", request.CommandName)),
                            PreserveSummary = true
                        });
                        var errorResponse = JsonConvert.SerializeObject(new
                        {
                            id = request.Id,
                            success = false,
                            error = unknownError
                        });
                        request.Tcs.TrySetResult(errorResponse);
                        break;
                    }

                    // S6 strict schema validation — fail fast with error-as-teacher envelope
                    var validation = SchemaValidator.Validate(command.ParametersSchema, request.ParamsJson);
                    if (!validation.IsValid)
                    {
                        sw.Stop();
                        var validationError = McpResponsePrivacy.RedactErrorForResponse(validation.Error);
                        McpLogger.Log(request.CommandName, request.ParamsJson, false,
                                      sw.ElapsedMilliseconds, validationError, enabled: runtimeConfig.EnableCallLogOrDefault);
                        _sessionLog?.Add(new McpCallEntry
                        {
                            ToolName = request.CommandName,
                            ParamsJson = request.ParamsJson,
                            Success = false,
                            DurationMs = sw.ElapsedMilliseconds,
                            ErrorMessage = validationError,
                            ToolDescription = command.Description,
                            Summary = Localization.L.T("history.summary.validationFailed", ("error", validationError)),
                            PreserveSummary = true
                        });
                        var validationResponse = JsonConvert.SerializeObject(new
                        {
                            id = request.Id,
                            success = false,
                            error = validationError,
                            suggestion = validation.Suggestion,
                            hint = validation.Hint
                        });
                        request.Tcs.TrySetResult(validationResponse);
                        break;
                    }

                    CommandResult result;
                    var surveySnapshot = Survey.SurveyHistoryBridge.Before(app, request, runtimeConfig.EnableChangeHistoryOrDefault);
                    using (var capture = McpChangeTracker.Begin(request.History?.IsValid == true && runtimeConfig.EnableChangeHistoryOrDefault))
                    {
                        bool rolledBack = false;
                        try
                        {
                            result = command.Execute(app, request.ParamsJson);
                            rolledBack = request.CommandName == "batch_execute"
                                && result.Data != null && JObject.FromObject(result.Data).Value<bool>("rolledBack");
                        }
                        finally
                        {
                            changes = capture.Snapshot(rolledBack);
                            try { history = capture.HistorySnapshot(app.ActiveUIDocument?.Document, rolledBack); }
                            catch (Exception ex)
                            {
                                HistoryDiagnostics.Report("history_snapshot", ex, request.History?.Id, App.DebugLog);
                                historyMarker = new JObject { ["status"] = "capture_failed" };
                            }
                            Survey.SurveyHistoryBridge.Complete(surveySnapshot, history);
                        }
                    }
                    Survey.SurveyHistoryBridge.Remember(app, request, runtimeConfig.EnableChangeHistoryOrDefault, result);
                    sw.Stop();
                    // Spill may replace result.Data; the outcome is judged on the handler's own data.
                    var handlerData = result.Data;

                    // Explicit output=file and oversized arbitrary-code responses spill before
                    // logging and guarding, so neither the wire nor logs retain the bulk body.
                    var preSpillResponse = JsonConvert.SerializeObject(new
                    {
                        id = request.Id,
                        success = result.Success,
                        data = result.Data,
                        error = McpResponsePrivacy.RedactErrorForResponse(NoDocumentGuidance.ForAgent(result.Error))
                    });
                    var envelope = JObject.Parse(preSpillResponse);
                    if (changes != null) envelope["changes"] = changes;
                    // Reserve the small receipt marker in the response budget before writing the capture.
                    if (history != null || historyMarker != null)
                        envelope["history_transfer"] = historyMarker ?? ChangeHistoryTransfer.PublicationMarker(request.History.Id, history);
                    var preGuard = ResponseEnvelopeGuard.Apply(request.CommandName, request.ParamsJson,
                        envelope, runtimeConfig);
                    result.Data = preGuard["data"];

                    // responseData is the redacted view used ONLY for session log + summary.
                    // The wire response (below) uses result.Data raw so the agent sees real values.
                    var responseData = McpResponsePrivacy.RedactDataForResponse(request.CommandName, result.Data);

                    string codeSnippet = null;
                    if (request.CommandName == "send_code_to_revit")
                    {
                        try { codeSnippet = JObject.Parse(request.ParamsJson)?.Value<string>("code"); }
                        catch { }
                    }

                    // Serialize result for logging
                    string resultJson = null;
                    try { resultJson = result.Data != null ? JsonConvert.SerializeObject(result.Data) : null; }
                    catch { }

                    string responseResultJson = null;
                    try { responseResultJson = responseData != null ? JsonConvert.SerializeObject(responseData) : null; }
                    catch { }

                    // Truncate for session log (max 10KB)
                    string sessionResult = responseResultJson != null && responseResultJson.Length > 10240
                        ? responseResultJson.Substring(0, 10240)
                        : responseResultJson;

                    var resultError = McpResponsePrivacy.RedactErrorForResponse(result.Error);
                    string rejectError = result.Success && !preGuard.Value<bool>("success")
                        ? preGuard.Value<string>("error") : null;

                    // History and mcp-calls.jsonl report one outcome, judged after the
                    // size guard so a rejected response is not recorded as a success.
                    var outcome = CommandOutcome.Normalize(
                        request.CommandName, result.Success, handlerData, resultError, rejectError);
                    historyMarker = historyMarker ?? PublishHistory(request, history, outcome.Success, handlerData, app.Application.VersionNumber);
                    if (historyMarker != null) preGuard["history_transfer"] = historyMarker;
                    else preGuard.Remove("history_transfer");
                    var response = preGuard.ToString(Formatting.None);

                    McpLogger.Log(request.CommandName, request.ParamsJson, outcome.Success,
                                  sw.ElapsedMilliseconds, outcome.Error, codeSnippet, resultJson, runtimeConfig.EnableCallLogOrDefault);

                    SendCodeJournalGate.OnSendCodeLogged(request.CommandName, request.ParamsJson, codeSnippet, outcome.Success, sw.ElapsedMilliseconds, outcome.Error, resultJson, runtimeConfig);

                    _sessionLog?.Add(new McpCallEntry
                    {
                        ToolName = request.CommandName,
                        ParamsJson = request.ParamsJson,
                        Success = outcome.Success,
                        DurationMs = sw.ElapsedMilliseconds,
                        ErrorMessage = outcome.Error,
                        CodeSnippet = codeSnippet,
                        ResultJson = sessionResult,
                        ToolDescription = command.Description,
                        Summary = SummaryGenerator.Generate(request.CommandName, request.ParamsJson,
                                                             sessionResult, outcome.Success, outcome.Error)
                    });

                    request.Tcs.TrySetResult(response);
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    var exError = McpResponsePrivacy.RedactErrorForResponse(ex.Message);
                    McpLogger.Log(request.CommandName, request.ParamsJson, false,
                                  sw.ElapsedMilliseconds, exError, enabled: runtimeConfig.EnableCallLogOrDefault);

                    string codeSnippetEx = null;
                    if (request.CommandName == "send_code_to_revit")
                    {
                        try { codeSnippetEx = JObject.Parse(request.ParamsJson)?.Value<string>("code"); }
                        catch { }
                    }
                    SendCodeJournalGate.OnSendCodeLogged(request.CommandName, request.ParamsJson, codeSnippetEx, false, sw.ElapsedMilliseconds, exError, null, runtimeConfig);

                    _sessionLog?.Add(new McpCallEntry
                    {
                        ToolName = request.CommandName,
                        ParamsJson = request.ParamsJson,
                        Success = false,
                        DurationMs = sw.ElapsedMilliseconds,
                        ErrorMessage = exError,
                        Summary = SummaryGenerator.Generate(request.CommandName, request.ParamsJson,
                                                             null, false, exError)
                    });
                    var errorEnvelope = JObject.FromObject(new
                    {
                        id = request.Id,
                        success = false,
                        error = exError
                    });
                    if (changes != null) errorEnvelope["changes"] = changes;
                    historyMarker = historyMarker ?? PublishHistory(request, history, false, null, app.Application.VersionNumber);
                    if (historyMarker != null) errorEnvelope["history_transfer"] = historyMarker;
                    if (changes != null)
                    {
                        try { errorEnvelope = ResponseEnvelopeGuard.Apply(request.CommandName, request.ParamsJson, errorEnvelope, runtimeConfig); }
                        catch { errorEnvelope["changes"] = ChangeSummary.Omitted(); }
                    }
                    request.Tcs.TrySetResult(errorEnvelope.ToString(Formatting.None));
                }

                break;
            }

            if (!_queue.IsEmpty)
            {
                try { App.Instance?.ExternalEvent?.Raise(); }
                catch { }
            }
        }

        /// <summary>
        /// One add-in tool (list_addin_commands / call_addin_command) off the ExternalEvent, on the pool thread
        /// <see cref="AddinCommandRoute"/> runs it on: the same read-only check, schema check, response guard, call log and History row
        /// as <see cref="Execute"/>, without Revit's thread. The add-in tools read no UIApplication - AddinCommandRegistry reaches
        /// the add-in through the AppDomain - so the handler gets none. Returns the wire response.
        /// </summary>
        public string RunAddinCommand(PendingRequest request)
        {
            var sw = Stopwatch.StartNew();
            var runtimeConfig = new RvtMcpConfig();
            string description = null;
            try
            {
                runtimeConfig = RvtMcpConfig.LoadReadOnly().WithRuntimeOptions(request.RuntimeOptions);
                var rejection = ToolReadPolicy.Rejection(request.CommandName, runtimeConfig.ReadOnlyOrDefault, runtimeConfig.EnableSendCodeOrDefault);
                if (rejection != null) throw new InvalidOperationException(rejection);
                var command = _dispatcher.GetCommand(request.CommandName);
                if (command == null)
                {
                    // As Execute: an unknown tool is its own History summary, not the generic failure.
                    sw.Stop();
                    var unknownError = McpResponsePrivacy.RedactErrorForResponse($"Unknown command: {request.CommandName}");
                    Report(request, false, unknownError, sw.ElapsedMilliseconds, null, null, runtimeConfig,
                        Localization.L.T("history.summary.unknown", ("tool", request.CommandName)), preserveSummary: true);
                    return JsonConvert.SerializeObject(new { id = request.Id, success = false, error = unknownError });
                }

                description = command.Description;

                var validation = SchemaValidator.Validate(command.ParametersSchema, request.ParamsJson);
                if (!validation.IsValid)
                {
                    sw.Stop();
                    var validationError = McpResponsePrivacy.RedactErrorForResponse(validation.Error);
                    Report(request, false, validationError, sw.ElapsedMilliseconds, null, description, runtimeConfig,
                        Localization.L.T("history.summary.validationFailed", ("error", validationError)), preserveSummary: true);
                    return JsonConvert.SerializeObject(new
                    {
                        id = request.Id,
                        success = false,
                        error = validationError,
                        suggestion = validation.Suggestion,
                        hint = validation.Hint
                    });
                }

                var result = command.Execute(null, request.ParamsJson);
                sw.Stop();
                // The same steps as Execute's success path: guard the response, log what the guard let through, show the redacted view.
                var handlerData = result.Data;
                // Serialized then parsed, as Execute does, so both paths read the handler's data with the same serializer settings.
                var envelope = JObject.Parse(JsonConvert.SerializeObject(new
                {
                    id = request.Id,
                    success = result.Success,
                    data = result.Data,
                    error = McpResponsePrivacy.RedactErrorForResponse(NoDocumentGuidance.ForAgent(result.Error))
                }));
                var guarded = ResponseEnvelopeGuard.Apply(request.CommandName, request.ParamsJson, envelope, runtimeConfig);
                var guardedData = guarded["data"];
                string resultJson = null;
                try { resultJson = guardedData != null ? JsonConvert.SerializeObject(guardedData) : null; }
                catch { }
                string responseResultJson = null;
                try
                {
                    var responseData = McpResponsePrivacy.RedactDataForResponse(request.CommandName, guardedData);
                    responseResultJson = responseData != null ? JsonConvert.SerializeObject(responseData) : null;
                }
                catch { }
                var resultError = McpResponsePrivacy.RedactErrorForResponse(result.Error);
                string rejectError = result.Success && !guarded.Value<bool>("success") ? guarded.Value<string>("error") : null;
                var outcome = CommandOutcome.Normalize(request.CommandName, result.Success, handlerData, resultError, rejectError);
                var sessionResult = responseResultJson != null && responseResultJson.Length > 10240
                    ? responseResultJson.Substring(0, 10240)
                    : responseResultJson;
                Report(request, outcome.Success, outcome.Error, sw.ElapsedMilliseconds, resultJson, description, runtimeConfig,
                    SummaryGenerator.Generate(request.CommandName, request.ParamsJson, sessionResult, outcome.Success, outcome.Error),
                    preserveSummary: false, sessionResult: sessionResult);
                return guarded.ToString(Formatting.None);
            }
            catch (Exception ex)
            {
                sw.Stop();
                var exError = McpResponsePrivacy.RedactErrorForResponse(ex.Message);
                Report(request, false, exError, sw.ElapsedMilliseconds, null, description, runtimeConfig,
                    SummaryGenerator.Generate(request.CommandName, request.ParamsJson, null, false, exError), preserveSummary: false);
                return JsonConvert.SerializeObject(new { id = request.Id, success = false, error = exError });
            }
        }

        // The call log line and the History row of one tool call. The log is a file and is written here; the row
        // is WPF and go to Revit's UI thread. An exception out of them must not change the answer already decided.
        private void Report(PendingRequest request, bool success, string error, long durationMs, string resultJson, string description,
            RvtMcpConfig runtimeConfig, string summary, bool preserveSummary, string sessionResult = null)
        {
            try
            {
                McpLogger.Log(request.CommandName, request.ParamsJson, success, durationMs, error, null, resultJson,
                    runtimeConfig.EnableCallLogOrDefault);
            }
            catch (Exception logEx)
            {
                App.DebugLog("McpLogger.Log failed (add-in tool): " + logEx.Message);
            }

            Action show = () =>
            {
                try
                {
                    _sessionLog?.Add(new McpCallEntry
                    {
                        ToolName = request.CommandName,
                        ParamsJson = request.ParamsJson,
                        Success = success,
                        DurationMs = durationMs,
                        ErrorMessage = error,
                        ResultJson = sessionResult,
                        ToolDescription = description,
                        Summary = summary,
                        PreserveSummary = preserveSummary
                    });
                }
                catch (Exception reportEx)
                {
                    App.DebugLog("Reporting an add-in tool call failed: " + reportEx.Message);
                }
            };
            if (_ui.CheckAccess())
                show();
            else
                _ui.BeginInvoke(show);
        }

        public string GetName() => "RvtMcp.McpEventHandler";

        private static JObject PublishHistory(PendingRequest request, JObject history, bool success, object data, string revitYear)
        {
            if (history == null) return null;
            try
            {
                history["callId"] = request.History.Id;
                history["session"] = request.History.Session;
                history["tool"] = request.CommandName;
                history["at"] = DateTime.UtcNow.ToString("o");
                history["success"] = success;
                history["revitYear"] = revitYear;
                history["user"] = Environment.UserName;
                history["machine"] = Environment.MachineName;
                // The handler returns updated rows only after commit, including partial-success calls.
                if (request.CommandName == "set_element_parameter_values" && data != null)
                {
                    var values = JObject.FromObject(data);
                    history["parameterValues"] = new JObject { ["parameter"] = values["parameterName"], ["updated"] = values["updated"] };
                }
                return new ChangeHistoryTransfer().Publish(request.History.Id, history);
            }
            catch (Exception ex)
            {
                // History failure must never change or replay an already completed Revit operation.
                HistoryDiagnostics.Report("history_publish", ex, request.History?.Id, App.DebugLog);
                return new JObject { ["status"] = "capture_failed" };
            }
        }

        public void CancelAll()
        {
            while (_queue.TryDequeue(out var request))
            {
                request.Tcs.TrySetCanceled();
            }
        }
    }
}
