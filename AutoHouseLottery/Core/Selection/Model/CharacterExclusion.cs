using AutoHouseLottery.Core.Eligibility.Model;

namespace AutoHouseLottery.Core.Selection.Model;

internal readonly record struct CharacterExclusion(ulong ContentId, EligibilityReason Reasons);
