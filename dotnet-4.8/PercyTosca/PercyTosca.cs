using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Tricentis.Automation.AutomationInstructions.TestActions;
using Tricentis.Automation.Creation;
using Tricentis.Automation.Creation.Attributes;
using Tricentis.Automation.Engines;
using Tricentis.Automation.Engines.Adapters.Attributes;
using Tricentis.Automation.Engines.SpecialExecutionTasks;
using Tricentis.Automation.Engines.SpecialExecutionTasks.Attributes;
using Tricentis.Automation.Engines.SpecialExecutionTasks.Html;
using Tricentis.Automation.Engines.Technicals.Html;
[assembly: EngineId("Percy")]

namespace ToscaPercySnapshot
{
    [SpecialExecutionTaskName("PercySnapshot")]
    [SupportedTechnical(typeof(IHtmlInputElementTechnical))]
    public class ToscaPercySnapshot : SpecialExecutionTask
    {
        public static readonly bool DEBUG = Environment.GetEnvironmentVariable("PERCY_LOGLEVEL") == "debug";
        private static HttpClient _http;
        public static readonly string CLI_API = Environment.GetEnvironmentVariable("PERCY_CLI_API") ?? "http://localhost:5338";
        // The default path is typically C:\Users\<username>\AppData\Local\Temp
        public static readonly string LOG_DIR = Path.GetTempPath();
        public static readonly string LOG_PATH = Path.Combine(LOG_DIR, "percy.txt");
        private static string _dom = null;
        private static IHtmlDocumentTechnical browser = null;
        private static bool? _enabled = null;
        // The CLI-resolved .percy.yml config, captured from the healthcheck
        // response (PER-8053) so MergeSnapshotOptions can merge it into the
        // per-snapshot serialize options.
        private static JsonElement? _cliConfig = null;

        public ToscaPercySnapshot(Validator validator) : base(validator) { }

        public override ActionResult Execute(ISpecialExecutionTaskTestAction testAction)
        {
            string snapshotName = testAction.GetParameterAsInputValue("SnapshotName", true)?.Value?.ToString();
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
                Dictionary<string, object> snapshotOptions = new Dictionary<string, object>();
                List<int> widthsList = new List<int>();

                string minHeightString = testAction.GetParameterAsInputValue("MinHeight", true)?.Value?.ToString();
                int minHeight = int.TryParse(minHeightString, out int parsedValue) ? parsedValue : 1024;
                string scope = testAction.GetParameterAsInputValue("ScopeSelector", true)?.Value?.ToString();
                string percyCSS = testAction.GetParameterAsInputValue("PercyCSS", true)?.Value?.ToString();
                bool enableJavascript = Convert.ToBoolean(testAction.GetParameterAsInputValue("EnableJavascript", true)?.Value);
                string widthsString = testAction.GetParameterAsInputValue("Widths", true)?.Value?.ToString();

                if (!string.IsNullOrEmpty(widthsString))
                {
                    widthsList = widthsString.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                             .Select(w =>
                                             {
                                                 if (int.TryParse(w.Trim(), out int val))
                                                 {
                                                     return (int?)val;
                                                 }
                                                 return null;
                                             })
                                             .Where(v => v.HasValue)
                                             .Select(v => v.Value)
                                             .ToList();
                }

                if (widthsList.Count > 0)
                {
                    snapshotOptions.Add("widths", widthsList);
                }

                snapshotOptions.Add("minHeight", minHeight);
                AddIfNotNull(snapshotOptions, "scope", scope);
                AddIfNotNull(snapshotOptions, "percyCSS", percyCSS);
                AddIfNotNull(snapshotOptions, "enableJavascript", enableJavascript);

                int retryCount = 10;
                int delay = 1000;
                for (int i = 0; i < retryCount; i++)
                {
                    try
                    {
                        browser = ExecuteJavaScriptBase.GetHtmlDocumentFromCaption("*", testAction);
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

                // Merge .percy.yml config options with snapshot options
                // (per-snapshot options take priority) before serialize.
                var mergedOptions = MergeSnapshotOptions(snapshotOptions);

                dynamic domSnapshot = null;
                domSnapshot = getSerializedDom(browser, mergedOptions);

                snapshotOptions.Add("clientInfo", "percy-tosca");
                snapshotOptions.Add("environmentInfo", "Tosca");
                snapshotOptions.Add("domSnapshot", domSnapshot);
                snapshotOptions.Add("url", browser.EntryPoint.GetJavaScriptResult("return document.URL"));
                snapshotOptions.Add("name", snapshotName);

                Request("/percy/snapshot", snapshotOptions);
            }
            catch (Exception ex)
            {
                testAction.SetResult(new UnknownFailedActionResult("Failed to execute Percy snapshot"));
                throw ex;
            }
            return new PassedActionResult("Snapshot Taken!");
        }

        private void AddIfNotNull(Dictionary<string, object> options, string key, object value)
        {
            if (value != null)
            {
                options.Add(key, value);
            }
        }

        private static Func<bool> Enabled = () =>
        {
            if (_enabled != null) return (bool)_enabled;

            try
            {
                dynamic res = Request("/percy/healthcheck");
                dynamic data = JsonSerializer.Deserialize<dynamic>(res.content);

                if (data.GetProperty("success").GetBoolean() != true)
                {
                    throw new Exception(data.error);
                }
                else if (res.version == null)
                {
                    Log("You may be using @percy/agent " +
                        "which is no longer supported by this SDK. " +
                        "Please uninstall @percy/agent and install @percy/cli instead. " +
                        "https://www.browserstack.com/docs/percy/migration/migrate-to-cli");
                    return (bool)(_enabled = false);
                }
                else if (res.version[0] != '1')
                {
                    Log($"Unsupported Percy CLI version, {res.version}");
                    return (bool)(_enabled = false);
                }
                else
                {
                    // Capture the CLI-resolved .percy.yml config (PER-8053) so it can
                    // be merged into the per-snapshot serialize options later.
                    try
                    {
                        JsonElement dataElement = JsonSerializer.Deserialize<JsonElement>(res.content);
                        if (dataElement.TryGetProperty("config", out JsonElement cfg))
                        {
                            _cliConfig = cfg.Clone();
                        }
                    }
                    catch (Exception e)
                    {
                        Log<Exception>(e, "debug");
                    }
                    return (bool)(_enabled = true);
                }
            }
            catch (Exception error)
            {
                Log("Percy is not running, disabling snapshots");
                Log<Exception>(error, "debug");
                return (bool)(_enabled = false);
            }
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

        private static string PayloadParser(object payload = null, bool alreadyJson = false)
        {
            if (alreadyJson)
            {
                return payload is null ? "" : payload.ToString();
            }
            return JsonSerializer.Serialize(payload).ToString();
        }

        private static dynamic Request(string endpoint, object payload = null, bool isJson = false)
        {
            StringContent body = payload == null ? null : new StringContent(
                PayloadParser(payload, isJson), Encoding.UTF8, "application/json");

            HttpClient httpClient = getHttpClient();
            Task<HttpResponseMessage> apiTask = body != null
                ? httpClient.PostAsync($"{CLI_API}{endpoint}", body)
                : httpClient.GetAsync($"{CLI_API}{endpoint}");
            apiTask.Wait();

            HttpResponseMessage response = apiTask.Result;
            response.EnsureSuccessStatusCode();

            Task<string> contentTask = response.Content.ReadAsStringAsync();
            contentTask.Wait();

            IEnumerable<string> version = null;
            response.Headers.TryGetValues("x-percy-core-version", out version);

            return new
            {
                version = version == null ? null : version.First(),
                content = contentTask.Result
            };
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
            _dom = Request("/percy/dom.js").content;
            return (string)_dom;
        }

        // Merges the CLI-resolved .percy.yml `snapshot` config with per-snapshot
        // options (PER-8053). Config is the base; per-snapshot options override.
        private static Dictionary<string, object> MergeSnapshotOptions(Dictionary<string, object> options)
        {
            var merged = new Dictionary<string, object>();
            if (_cliConfig.HasValue &&
                _cliConfig.Value.ValueKind == JsonValueKind.Object &&
                _cliConfig.Value.TryGetProperty("snapshot", out JsonElement snapshotElement) &&
                snapshotElement.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty prop in snapshotElement.EnumerateObject())
                {
                    merged[prop.Name] = JsonElementToObject(prop.Value);
                }
            }
            // Deep-merge per-snapshot options over the .percy.yml config:
            // nested objects recurse (per-call wins at leaves); arrays/scalars replace.
            merged = DeepMerge(merged, options);
            return merged;
        }

        // Recursively converts a JsonElement into plain CLR objects:
        // Object -> Dictionary<string, object> (recursive), Array -> List<object> (recursive),
        // primitives as-is.
        private static object JsonElementToObject(JsonElement el)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.Object:
                    var dict = new Dictionary<string, object>();
                    foreach (JsonProperty prop in el.EnumerateObject())
                        dict[prop.Name] = JsonElementToObject(prop.Value);
                    return dict;
                case JsonValueKind.Array:
                    var list = new List<object>();
                    foreach (JsonElement item in el.EnumerateArray())
                        list.Add(JsonElementToObject(item));
                    return list;
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                case JsonValueKind.Number:
                    return el.TryGetInt32(out int intVal) ? intVal : (object)el.GetDouble();
                case JsonValueKind.String:
                    return el.GetString();
                case JsonValueKind.Null:
                    return null;
                default:
                    return el.GetRawText();
            }
        }

        // Deep-merges overrideDict onto baseDict. When both sides hold a
        // Dictionary<string, object> for the same key, recurse; otherwise the
        // override value wins (arrays and scalars replace).
        private static Dictionary<string, object> DeepMerge(Dictionary<string, object> baseDict, Dictionary<string, object> overrideDict)
        {
            var merged = new Dictionary<string, object>(baseDict);
            if (overrideDict == null) return merged;
            foreach (var kvp in overrideDict)
            {
                if (merged.TryGetValue(kvp.Key, out object existing) &&
                    existing is Dictionary<string, object> existingDict &&
                    kvp.Value is Dictionary<string, object> overrideValueDict)
                {
                    merged[kvp.Key] = DeepMerge(existingDict, overrideValueDict);
                }
                else
                {
                    merged[kvp.Key] = kvp.Value;
                }
            }
            return merged;
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
