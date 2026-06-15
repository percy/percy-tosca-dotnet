using System.Text.Json;

namespace PercyTosca.Core
{
    /// <summary>
    /// Pure payload serialization logic extracted from the Tosca-coupled SDK class.
    /// Mirrors the original ToscaPercySnapshot.PayloadParser behaviour exactly.
    /// </summary>
    public static class PercyPayload
    {
        /// <summary>
        /// Serializes a payload to a JSON string.
        /// When <paramref name="alreadyJson"/> is true the payload is passed through
        /// (null becomes an empty string); otherwise it is JSON serialized.
        /// </summary>
        public static string PayloadParser(object? payload = null, bool alreadyJson = false)
        {
            if (alreadyJson)
            {
                return payload is null ? "" : payload.ToString()!;
            }
            return JsonSerializer.Serialize(payload).ToString();
        }
    }
}
