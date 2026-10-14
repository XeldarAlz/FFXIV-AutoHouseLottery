using AutoHouseLottery.Core.Localization;

namespace AutoHouseLottery.Core.Changelog;

internal readonly record struct ChangelogEntry(string Version, string Date, LocString[] Highlights);
