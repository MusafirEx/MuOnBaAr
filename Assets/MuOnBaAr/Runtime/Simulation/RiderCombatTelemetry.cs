namespace MuOnBaAr.Runtime
{
    /// <summary>
    /// Stable, searchable Unity Console diagnostic message format.
    /// Messages containing [WDMCP_EVENT] can also be captured by MCP tracing.
    /// Does not generate logs by itself.
    /// </summary>
    public static class RiderCombatTelemetry
    {
        public const string Prefix = "[WDMCP_EVENT][MuOnBaAr][COMBAT]";

        public static string Format(
            string eventName, int tick, string player, string objectId, string details)
        {
            return Prefix + " event=" + eventName +
                   " tick=" + tick +
                   " player=" + player +
                   " object=" + objectId +
                   (string.IsNullOrEmpty(details) ? "" : " " + details);
        }
    }
}
