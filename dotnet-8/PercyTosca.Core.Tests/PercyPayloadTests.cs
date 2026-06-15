using Xunit;

namespace PercyTosca.Core.Tests
{
    public class PercyPayloadTests
    {
        [Fact]
        public void PayloadParser_AlreadyJson_NullReturnsEmptyString()
        {
            Assert.Equal("", PercyPayload.PayloadParser(null, alreadyJson: true));
        }

        [Fact]
        public void PayloadParser_AlreadyJson_PassesValueThrough()
        {
            string json = "{\"a\":1}";
            Assert.Equal(json, PercyPayload.PayloadParser(json, alreadyJson: true));
        }

        [Fact]
        public void PayloadParser_NotJson_SerializesObject()
        {
            var payload = new Dictionary<string, object> { { "level", "info" } };
            Assert.Equal("{\"level\":\"info\"}", PercyPayload.PayloadParser(payload, alreadyJson: false));
        }

        [Fact]
        public void PayloadParser_DefaultArgs_SerializesNullLiteral()
        {
            Assert.Equal("null", PercyPayload.PayloadParser());
        }
    }
}
