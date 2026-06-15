using Xunit;

namespace PercyTosca.Core.Tests
{
    public class PercyOptionsTests
    {
        [Fact]
        public void AddIfNotNull_AddsWhenValuePresent()
        {
            var options = new Dictionary<string, object>();
            PercyOptions.AddIfNotNull(options, "scope", ".main");
            Assert.True(options.ContainsKey("scope"));
            Assert.Equal(".main", options["scope"]);
        }

        [Fact]
        public void AddIfNotNull_SkipsWhenValueNull()
        {
            var options = new Dictionary<string, object>();
            PercyOptions.AddIfNotNull(options, "scope", null);
            Assert.False(options.ContainsKey("scope"));
        }

        [Fact]
        public void ParseWidths_NullReturnsEmptyList()
        {
            Assert.Empty(PercyOptions.ParseWidths(null));
        }

        [Fact]
        public void ParseWidths_EmptyReturnsEmptyList()
        {
            Assert.Empty(PercyOptions.ParseWidths(""));
        }

        [Fact]
        public void ParseWidths_TrimsSpacesAndParsesValues()
        {
            var widths = PercyOptions.ParseWidths(" 375 , 768,1280 ");
            Assert.Equal(new List<int> { 375, 768, 1280 }, widths);
        }

        [Fact]
        public void ParseWidths_DropsInvalidEntriesAndEmptyEntries()
        {
            // "abc" is non-numeric and dropped; the trailing/consecutive commas yield
            // empty entries which are removed by RemoveEmptyEntries.
            var widths = PercyOptions.ParseWidths("375,abc,,768,");
            Assert.Equal(new List<int> { 375, 768 }, widths);
        }

        [Fact]
        public void ParseMinHeight_ValidValueIsParsed()
        {
            Assert.Equal(2000, PercyOptions.ParseMinHeight("2000"));
        }

        [Fact]
        public void ParseMinHeight_InvalidValueDefaultsTo1024()
        {
            Assert.Equal(1024, PercyOptions.ParseMinHeight("not-a-number"));
        }

        [Fact]
        public void ParseMinHeight_NullDefaultsTo1024()
        {
            Assert.Equal(1024, PercyOptions.ParseMinHeight(null));
        }

        [Fact]
        public void BuildSnapshotOptions_IncludesWidthsWhenNonEmpty()
        {
            var widths = new List<int> { 375, 768 };
            var options = PercyOptions.BuildSnapshotOptions(widths, 1024, null, null, null);

            Assert.True(options.ContainsKey("widths"));
            Assert.Same(widths, options["widths"]);
            Assert.Equal(1024, options["minHeight"]);
        }

        [Fact]
        public void BuildSnapshotOptions_OmitsWidthsWhenEmpty()
        {
            var options = PercyOptions.BuildSnapshotOptions(new List<int>(), 1024, null, null, null);

            Assert.False(options.ContainsKey("widths"));
            Assert.True(options.ContainsKey("minHeight"));
        }

        [Fact]
        public void BuildSnapshotOptions_OmitsOptionalKeysWhenNull()
        {
            var options = PercyOptions.BuildSnapshotOptions(new List<int>(), 1024, null, null, null);

            Assert.False(options.ContainsKey("scope"));
            Assert.False(options.ContainsKey("percyCSS"));
            Assert.False(options.ContainsKey("enableJavascript"));
        }

        [Fact]
        public void BuildSnapshotOptions_IncludesOptionalKeysWhenPresent()
        {
            var options = PercyOptions.BuildSnapshotOptions(
                new List<int> { 1280 }, 900, ".scope", "body { color: red; }", true);

            Assert.Equal(".scope", options["scope"]);
            Assert.Equal("body { color: red; }", options["percyCSS"]);
            Assert.Equal(true, options["enableJavascript"]);
            Assert.Equal(900, options["minHeight"]);
        }

        [Fact]
        public void AddSnapshotMetadata_AddsAllFiveFields()
        {
            var options = new Dictionary<string, object>();
            var dom = new Dictionary<string, object> { { "html", "<html></html>" } };

            PercyOptions.AddSnapshotMetadata(options, dom, "https://example.com", "Home");

            Assert.Equal("percy-tosca", options["clientInfo"]);
            Assert.Equal("Tosca", options["environmentInfo"]);
            Assert.Same(dom, options["domSnapshot"]);
            Assert.Equal("https://example.com", options["url"]);
            Assert.Equal("Home", options["name"]);
        }
    }
}
