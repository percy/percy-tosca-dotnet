namespace PercyTosca.Core
{
    /// <summary>
    /// Pure snapshot-option assembly logic extracted from the Tosca-coupled SDK class.
    /// Mirrors the original ToscaPercySnapshot option-building behaviour exactly.
    /// </summary>
    public static class PercyOptions
    {
        /// <summary>
        /// Adds <paramref name="value"/> to <paramref name="options"/> under <paramref name="key"/>
        /// only when the value is not null.
        /// </summary>
        public static void AddIfNotNull(Dictionary<string, object> options, string key, object? value)
        {
            if (value != null)
            {
                options.Add(key, value);
            }
        }

        /// <summary>
        /// Parses a comma-separated widths string into a list of ints.
        /// Whitespace is trimmed, empty entries removed, and non-numeric entries dropped.
        /// A null/empty input yields an empty list.
        /// </summary>
        public static List<int> ParseWidths(string? widthsString)
        {
            List<int> widthsList = new List<int>();

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
                                         .Select(v => v!.Value)
                                         .ToList();
            }

            return widthsList;
        }

        /// <summary>
        /// Parses a min-height string, defaulting to 1024 when the value is not a valid int.
        /// </summary>
        public static int ParseMinHeight(string? minHeightString)
        {
            return int.TryParse(minHeightString, out int parsedValue) ? parsedValue : 1024;
        }

        /// <summary>
        /// Assembles the snapshot options dictionary from the parsed inputs.
        /// Mirrors the original ordering and conditional inclusion rules:
        /// widths only when non-empty, minHeight always, and scope/percyCSS/enableJavascript
        /// only when non-null.
        /// </summary>
        public static Dictionary<string, object> BuildSnapshotOptions(
            List<int> widthsList,
            int minHeight,
            string? scope,
            string? percyCSS,
            bool? enableJavascript)
        {
            Dictionary<string, object> snapshotOptions = new Dictionary<string, object>();

            if (widthsList.Count > 0)
            {
                snapshotOptions.Add("widths", widthsList);
            }

            snapshotOptions.Add("minHeight", minHeight);
            AddIfNotNull(snapshotOptions, "scope", scope);
            AddIfNotNull(snapshotOptions, "percyCSS", percyCSS);
            AddIfNotNull(snapshotOptions, "enableJavascript", enableJavascript);

            return snapshotOptions;
        }

        /// <summary>
        /// Adds the client/environment/dom/url/name fields to an already-assembled
        /// snapshot options dictionary just before the snapshot request, mirroring the
        /// original Execute() flow.
        /// </summary>
        public static void AddSnapshotMetadata(
            Dictionary<string, object> snapshotOptions,
            object domSnapshot,
            string url,
            string snapshotName)
        {
            snapshotOptions.Add("clientInfo", "percy-tosca");
            snapshotOptions.Add("environmentInfo", "Tosca");
            snapshotOptions.Add("domSnapshot", domSnapshot);
            snapshotOptions.Add("url", url);
            snapshotOptions.Add("name", snapshotName);
        }
    }
}
