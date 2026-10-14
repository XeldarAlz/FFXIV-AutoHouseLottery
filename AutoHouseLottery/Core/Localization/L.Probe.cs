namespace AutoHouseLottery.Core.Localization;

internal static partial class L
{
    internal static class Probe
    {
        public static readonly LocString Done = new("probe.done", "Housing readings written to the Console (/ahl log).");
        public static readonly LocString Started = new("probe.started", "Probe task started; follow it in the Console (/ahl log).");
        public static readonly LocString Failed = new("probe.failed", "The probe could not start; the Console (/ahl log) says why.");
        public static readonly LocString Usage = new("probe.usage", "Usage: /ahl probe [scan | lottery | goto <district> <ward> <plot> | approach <ward> <plot> | press <enter | results | cancel | no> | stop]");
    }
}
