using System.Text;
using System.Text.Json;

namespace PercyTosca.Core
{
    /// <summary>
    /// Tosca-free HTTP client for talking to the Percy CLI. The <see cref="HttpClient"/>
    /// (and therefore its <see cref="HttpMessageHandler"/>) is injectable so that requests
    /// can be exercised in tests without real network access.
    /// </summary>
    public class PercyClient
    {
        private readonly HttpClient _http;
        private readonly string _cliApi;

        /// <summary>
        /// The `config` object from the last successful healthcheck (the merged
        /// .percy.yml the CLI resolved), or null if absent/unavailable. SDKs read
        /// this to merge global config with per-snapshot options before serialize.
        /// </summary>
        public JsonElement? CliConfig { get; private set; }

        public PercyClient(HttpClient http, string cliApi)
        {
            _http = http;
            _cliApi = cliApi;
        }

        /// <summary>
        /// Performs a Percy CLI request. When <paramref name="payload"/> is non-null the
        /// request is a POST with a JSON body; otherwise it is a GET. The body is built via
        /// <see cref="PercyPayload.PayloadParser"/>. Throws on non-success status codes and
        /// returns the body plus the resolved x-percy-core-version header.
        /// </summary>
        public PercyResponse Request(string endpoint, object? payload = null, bool isJson = false)
        {
            StringContent? body = payload == null ? null : new StringContent(
                PercyPayload.PayloadParser(payload, isJson), Encoding.UTF8, "application/json");

            Task<HttpResponseMessage> apiTask = body != null
                ? _http.PostAsync($"{_cliApi}{endpoint}", body)
                : _http.GetAsync($"{_cliApi}{endpoint}");
            apiTask.Wait();

            HttpResponseMessage response = apiTask.Result;
            response.EnsureSuccessStatusCode();

            Task<string> contentTask = response.Content.ReadAsStringAsync();
            contentTask.Wait();

            IEnumerable<string>? version = null;
            response.Headers.TryGetValues("x-percy-core-version", out version);

            return new PercyResponse(
                version == null ? null : version.First(),
                contentTask.Result);
        }

        /// <summary>
        /// Determines whether Percy is running and supported, mirroring the original
        /// version-gate logic. The healthcheck request is performed via <see cref="Request"/>
        /// (so it is fully injectable in tests), and log output is routed through
        /// <paramref name="log"/>. Returns true only for a healthy "1.x" CLI.
        /// </summary>
        public bool Enabled(Action<string> log)
        {
            try
            {
                PercyResponse res = Request("/percy/healthcheck");
                JsonElement data = JsonSerializer.Deserialize<JsonElement>(res.Content);

                if (data.GetProperty("success").GetBoolean() != true)
                {
                    throw new Exception(data.GetProperty("error").GetString());
                }
                else if (res.Version == null)
                {
                    log("You may be using @percy/agent " +
                        "which is no longer supported by this SDK. " +
                        "Please uninstall @percy/agent and install @percy/cli instead. " +
                        "https://www.browserstack.com/docs/percy/migration/migrate-to-cli");
                    return false;
                }
                else if (res.Version[0] != '1')
                {
                    log($"Unsupported Percy CLI version, {res.Version}");
                    return false;
                }
                else
                {
                    if (data.TryGetProperty("config", out JsonElement configElement))
                        CliConfig = configElement;
                    return true;
                }
            }
            catch (Exception error)
            {
                log("Percy is not running, disabling snapshots");
                log(error.Message);
                return false;
            }
        }
    }
}
