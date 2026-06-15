using System.Net;
using Xunit;

namespace PercyTosca.Core.Tests
{
    public class PercyClientTests
    {
        private const string CliApi = "http://localhost:5338";

        private static (PercyClient client, StubHttpMessageHandler handler) BuildClient(
            Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            var handler = new StubHttpMessageHandler(responder);
            var http = new HttpClient(handler);
            return (new PercyClient(http, CliApi), handler);
        }

        private static HttpResponseMessage Ok(string body, string? coreVersion = null)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body)
            };
            if (coreVersion != null)
            {
                response.Headers.Add("x-percy-core-version", coreVersion);
            }
            return response;
        }

        [Fact]
        public void Request_WithoutPayload_SendsGetToBuiltUrlAndReturnsVersion()
        {
            var (client, handler) = BuildClient(_ => Ok("dom-js-content", "1.27.0"));

            PercyResponse res = client.Request("/percy/dom.js");

            Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
            Assert.Equal("http://localhost:5338/percy/dom.js", handler.LastRequest!.RequestUri!.ToString());
            Assert.Null(handler.LastRequestBody);
            Assert.Equal("dom-js-content", res.Content);
            Assert.Equal("1.27.0", res.Version);
        }

        [Fact]
        public void Request_WithoutVersionHeader_ReturnsNullVersion()
        {
            var (client, _) = BuildClient(_ => Ok("body"));

            PercyResponse res = client.Request("/percy/healthcheck");

            Assert.Null(res.Version);
            Assert.Equal("body", res.Content);
        }

        [Fact]
        public void Request_WithPayload_SendsPostWithJsonBody()
        {
            var (client, handler) = BuildClient(_ => Ok("{\"success\":true}"));
            var payload = new Dictionary<string, object> { { "name", "snap" } };

            client.Request("/percy/snapshot", payload);

            Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
            Assert.Equal("http://localhost:5338/percy/snapshot", handler.LastRequest!.RequestUri!.ToString());
            Assert.Equal("{\"name\":\"snap\"}", handler.LastRequestBody);
            Assert.Equal("application/json", handler.LastRequest!.Content!.Headers.ContentType!.MediaType);
        }

        [Fact]
        public void Request_WithJsonPayload_PassesBodyThroughUnserialized()
        {
            var (client, handler) = BuildClient(_ => Ok("ok"));

            client.Request("/percy/log", "{\"raw\":true}", isJson: true);

            Assert.Equal("{\"raw\":true}", handler.LastRequestBody);
        }

        [Fact]
        public void Request_ThrowsOnNonSuccessStatus()
        {
            var (client, _) = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("boom")
            });

            Assert.Throws<HttpRequestException>(() => client.Request("/percy/healthcheck"));
        }

        [Fact]
        public void Enabled_HealthySupportedVersion_ReturnsTrue()
        {
            var logs = new List<string>();
            var (client, _) = BuildClient(_ => Ok("{\"success\":true}", "1.27.0"));

            bool enabled = client.Enabled(logs.Add);

            Assert.True(enabled);
            Assert.Empty(logs);
        }

        [Fact]
        public void Enabled_NullCoreVersion_LogsAgentWarningAndReturnsFalse()
        {
            var logs = new List<string>();
            // success true but no x-percy-core-version header => Version is null.
            var (client, _) = BuildClient(_ => Ok("{\"success\":true}"));

            bool enabled = client.Enabled(logs.Add);

            Assert.False(enabled);
            Assert.Contains(logs, l => l.Contains("@percy/agent"));
        }

        [Fact]
        public void Enabled_UnsupportedVersion_ReturnsFalse()
        {
            var logs = new List<string>();
            var (client, _) = BuildClient(_ => Ok("{\"success\":true}", "2.0.0"));

            bool enabled = client.Enabled(logs.Add);

            Assert.False(enabled);
            Assert.Contains(logs, l => l.Contains("Unsupported Percy CLI version"));
        }

        [Fact]
        public void Enabled_SuccessFalse_ThrowsInternallyAndReturnsFalse()
        {
            var logs = new List<string>();
            var (client, _) = BuildClient(_ => Ok("{\"success\":false,\"error\":\"nope\"}", "1.27.0"));

            bool enabled = client.Enabled(logs.Add);

            Assert.False(enabled);
            Assert.Contains(logs, l => l.Contains("Percy is not running, disabling snapshots"));
            Assert.Contains(logs, l => l.Contains("nope"));
        }

        [Fact]
        public void Enabled_RequestThrows_ReturnsFalse()
        {
            var logs = new List<string>();
            var (client, _) = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)
            {
                Content = new StringContent("down")
            });

            bool enabled = client.Enabled(logs.Add);

            Assert.False(enabled);
            Assert.Contains(logs, l => l.Contains("Percy is not running, disabling snapshots"));
        }
    }
}
