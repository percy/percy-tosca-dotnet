using System.Text.Json;
using Tricentis.Automation.AutomationInstructions.TestActions;
using Tricentis.Automation.Creation.Attributes;
using Tricentis.Automation.Engines;
using Tricentis.Automation.Engines.Adapters.Attributes;
using Tricentis.Automation.Engines.SpecialExecutionTasks;
using Tricentis.Automation.Engines.SpecialExecutionTasks.Attributes;
using Tricentis.Automation.Engines.Technicals.Html;
using Percy.CustomJSExecutor;
using PercyTosca.Core;
[assembly: EngineId("Percy")]

namespace ToscaPercySnapshot
{
    [SpecialExecutionTaskName("PercySnapshot")]
    [SupportedTechnical(typeof(IHtmlInputElementTechnical))]
    public class ToscaPercySnapshot : SpecialExecutionTask
    {
        public static readonly bool DEBUG = Environment.GetEnvironmentVariable("PERCY_LOGLEVEL") == "debug";
        private static HttpClient _http;
        private readonly CustomJSExecutor customJSExecutor;
        public static readonly string CLI_API = Environment.GetEnvironmentVariable("PERCY_CLI_API") ?? "http://localhost:5338";
        // The default path is typically C:\Users\<username>\AppData\Local\Temp
        public static readonly string LOG_DIR = Path.GetTempPath();
        public static readonly string LOG_PATH = Path.Combine(LOG_DIR, "percy.txt");
        private static string _dom = null;
        private static IHtmlDocumentTechnical browser = null;
        private static bool? _enabled = null;
        // Tosca-free HTTP/version-gate logic lives in PercyTosca.Core and is delegated to below.
        private static PercyClient _percyClient;

        public ToscaPercySnapshot(Tricentis.Automation.Creation.Validator validator) : base(validator) {
            this.customJSExecutor = new CustomJSExecutor(validator);
        }

        public override ActionResult Execute(ISpecialExecutionTaskTestAction testAction)
        {
            string snapshotName = testAction.GetParameterAsInputValue("SnapshotName", true)?.Value?.ToString();
            string caption = testAction.GetParameterAsInputValue("Caption", true)?.Value?.ToString();
            if (caption == null)
            {
                caption = "*";
            }
            Log($"Starting Execution for snapshot, {snapshotName}");

            if (!Enabled())
            {
                Log($"Percy is not running!");
                return (ActionResult)new UnknownFailedActionResult("Percy is not running!");
            }
            if (string.IsNullOrEmpty(snapshotName))
            {
                Log($"SnapshotName cannot be empty!");
                return (ActionResult)new UnknownFailedActionResult("SnapshotName cannot be empty!");
            }

            try
            {
                string minHeightString = testAction.GetParameterAsInputValue("MinHeight", true)?.Value?.ToString();
                int minHeight = PercyOptions.ParseMinHeight(minHeightString);
                string scope = testAction.GetParameterAsInputValue("ScopeSelector", true)?.Value?.ToString();
                string percyCSS = testAction.GetParameterAsInputValue("PercyCSS", true)?.Value?.ToString();
                bool enableJavascript = Convert.ToBoolean(testAction.GetParameterAsInputValue("EnableJavascript", true)?.Value);
                string widthsString = testAction.GetParameterAsInputValue("Widths", true)?.Value?.ToString();

                List<int> widthsList = PercyOptions.ParseWidths(widthsString);
                Dictionary<string, object> snapshotOptions =
                    PercyOptions.BuildSnapshotOptions(widthsList, minHeight, scope, percyCSS, enableJavascript);

                int retryCount = 10;
                int delay = 1000;
                for (int i = 0; i < retryCount; i++)
                {
                    try
                    {
                        browser = customJSExecutor.GetHtmlDocumentFromCaption(caption, testAction);
                        if (browser != null)
                            break;
                        Thread.Sleep(delay);
                    }
                    catch
                    {
                        Thread.Sleep(delay);
                    }
                }

                if (browser == null)
                {
                    Log($"Browser not found!");
                    return (ActionResult)new UnknownFailedActionResult("Browser not found!");
                }

                string script = GetPercyDOM();
                browser.EntryPoint.ExecuteJavaScriptInDocument(browser, script);

                dynamic domSnapshot = null;
                domSnapshot = getSerializedDom(browser, snapshotOptions);

                PercyOptions.AddSnapshotMetadata(
                    snapshotOptions,
                    domSnapshot,
                    browser.EntryPoint.GetJavaScriptResult("return document.URL"),
                    snapshotName);

                Request("/percy/snapshot", snapshotOptions);
            }
            catch (Exception ex)
            {
                testAction.SetResult(new UnknownFailedActionResult("Failed to execute Percy snapshot"));
                throw ex;
            }
            return new PassedActionResult("Snapshot Taken!");
        }

        private static Func<bool> Enabled = () =>
        {
            if (_enabled != null) return (bool)_enabled;

            // Delegate the version-gate logic to the Tosca-free Core, routing log output
            // back through this class's Log helper.
            _enabled = GetPercyClient().Enabled(msg => Log(msg));
            return (bool)_enabled;
        };

        private static void writeLog(string msg)
        {
            using (StreamWriter writer = new StreamWriter(LOG_PATH, append: true))
            {
                writer.WriteLine(msg);
            }
        }

        private static void setHttpClient(HttpClient client)
        {
            _http = client;
        }

        private static HttpClient getHttpClient()
        {
            if (_http == null)
            {
                setHttpClient(new HttpClient());
                _http.Timeout = TimeSpan.FromMinutes(10);
            }

            return _http;
        }

        // Builds (once) the Tosca-free Core client used for all CLI communication.
        private static PercyClient GetPercyClient()
        {
            if (_percyClient == null)
            {
                _percyClient = new PercyClient(getHttpClient(), CLI_API);
            }

            return _percyClient;
        }

        private static PercyResponse Request(string endpoint, object payload = null, bool isJson = false)
        {
            return GetPercyClient().Request(endpoint, payload, isJson);
        }

        private static void Log<T>(T message, string level = "info")
        {
            string label = DEBUG ? "percy:tosca" : "percy";
            string labeledMessage = $"[\u001b[35m{label}\u001b[39m] {message}";
            try
            {
                Dictionary<string, object> logPayload = new Dictionary<string, object> {
                    { "message", labeledMessage },
                    { "level", level }
                };
                Request("/percy/log", logPayload);
            }
            catch (Exception e)
            {
                writeLog($"Sending log to CLI failed: {e.Message}");
            }
        }

        private static string GetPercyDOM()
        {
            if (_dom != null) return (string)_dom;
            _dom = Request("/percy/dom.js").Content;
            return (string)_dom;
        }

        private static dynamic getSerializedDom(IHtmlDocumentTechnical browser, Dictionary<string, object> options)
        {
            var opts = JsonSerializer.Serialize(options);
            string script = $"return JSON.stringify(PercyDOM.serialize({opts}))";
            dynamic response = browser.EntryPoint.GetJavaScriptResult(script);
            var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(response);
            return dict;
        }
    }
}
