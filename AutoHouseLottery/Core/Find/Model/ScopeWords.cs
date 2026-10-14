using AutoHouseLottery.Core.Localization;

namespace AutoHouseLottery.Core.Find.Model;

// Which phrase names each part of an area scope; every value has its own, so an empty choice never reads as another.
internal static class ScopeWords
{
    public static LocString Divisions(DivisionSpan divisions) => divisions switch
    {
        DivisionSpan.Both => L.Find.ScopeBothDivisions,
        DivisionSpan.Main => L.Find.ScopeMainDivision,
        DivisionSpan.Subdivision => L.Find.ScopeSubdivision,
        _ => L.Find.ScopeNoDivision,
    };
}
