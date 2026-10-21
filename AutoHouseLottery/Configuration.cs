using AutoHouseLottery.Core.Changelog;
using Dalamud.Configuration;
using ECommons.Throttlers;
using Newtonsoft.Json;

namespace AutoHouseLottery;

[Serializable]
public sealed partial class Configuration : IPluginConfiguration
{
    // 1: the first version of these settings. A later layout change bumps it and adds its step to Migrate.
    public const int CurrentVersion = 1;

    [JsonIgnore]
    private bool savePending;

    public int Version { get; set; }

    public bool AutoShowOnLogin { get; set; } = false;

    public bool DragWindowFromAnywhere { get; set; } = false;

    public string Language { get; set; } = "";

    public bool AutoPauseInContent { get; set; } = true;

    public bool PauseWhileTyping { get; set; } = true;

    // Lets a finished run hand off to other plugins, like "/li home" or "/ays m" for AutoRetainer; one command per line.
    public string PostRunCommands { get; set; } = string.Empty;

    public string LastSeenChangelogVersion { get; set; } = string.Empty;

    [JsonIgnore]
    public bool HasUnseenChangelog => !string.Equals(LastSeenChangelogVersion, ChangelogData.LatestVersion, StringComparison.Ordinal);

    public static Configuration CreateFresh() => new()
    {
        Version = CurrentVersion,
    };

    // True when the loaded config was older or needed a repair, so the caller saves it once.
    public bool Migrate()
    {
        var changed = RepairSafetySettings() | RepairCharacters() | RepairRoster() | RepairFindSettings() | RepairSettlementSettings() | RepairPostRunCommands();
        if (Version >= CurrentVersion)
        {
            return changed;
        }

        Version = CurrentVersion;
        return true;
    }

    public void MarkChangelogSeen()
    {
        if (!HasUnseenChangelog)
        {
            return;
        }

        LastSeenChangelogVersion = ChangelogData.LatestVersion;
        Save();
    }

    public void Save()
    {
        savePending = false;
        Plugin.PluginInterface.SavePluginConfig(this);
    }

    // A slider or a text box changes on every frame it is edited, so the throttle drops most calls; the change stays
    // pending, and FlushPendingSave writes the last one once the throttle window has passed.
    public void SaveDebounced()
    {
        savePending = true;
        FlushPendingSave();
    }

    public void FlushPendingSave()
    {
        if (savePending && EzThrottler.Throttle(Core.HouseLotteryConstants.ThrottleKeys.Save, Core.HouseLotteryConstants.SaveThrottleMs))
        {
            Save();
        }
    }

    public void SaveIfPending()
    {
        if (savePending)
        {
            Save();
        }
    }

    // A hand-edited file can carry a null command block; it is put right once on load.
    private bool RepairPostRunCommands()
    {
        if (PostRunCommands is not null)
        {
            return false;
        }

        PostRunCommands = string.Empty;
        return true;
    }
}
