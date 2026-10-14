using System.Linq;
using AutoHouseLottery.Core.Find.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Selection.Model;
using static AutoHouseLottery.Tests.FindFixtures;
using static AutoHouseLottery.Tests.HousingFixtures;

namespace AutoHouseLottery.Tests;

public sealed class FindSelectionTests
{
    [Fact]
    public void ChangingTheAreaOrFiltersNeverRemovesShortlistedPlots()
    {
        var plots = Plots(Listing(Plot(1, 1), size: PlotSize.Small), Listing(Plot(1, 2), size: PlotSize.Large));
        var selection = Selection(Scope(HousingDistricts.Mist));
        selection.AddAll(FindResultsBuilder.Build(plots, Query(selection.Area, selection.Filter)).MatchingKeys);

        selection.SetFilter(PlotFilter.Default with { Sizes = SizeFilter.Small });
        selection.SetArea(Scope(HousingDistricts.Goblet));
        selection.SetSort(ResultSort.LowestDeposit);
        var narrowed = FindResultsBuilder.Build(plots, Query(selection.Area, selection.Filter));

        Assert.Equal(0, narrowed.Count);
        Assert.Equal([Plot(1, 1), Plot(1, 2)], selection.Shortlist.Plots.ToArray());
        Assert.Equal(3, selection.QueryRevision);
    }

    [Fact]
    public void RemoveUnavailableDropsOnlyThePlotsKnownToBeOutOfReach()
    {
        var plots = Plots(Listing(Plot(1, 1)), Listing(Plot(1, 2), PlotSale.NotReported));
        var selection = Selection(Scope(HousingDistricts.Mist));
        selection.Toggle(Plot(1, 1));
        selection.Toggle(Plot(1, 2));
        selection.Toggle(Plot(1, 3));

        var removed = selection.RemoveUnavailable(ShortlistRules.Rows(selection.Shortlist.Plots, plots, VerifiedEntryPhase()));

        Assert.Equal(1, removed);
        Assert.Equal([Plot(1, 1), Plot(1, 3)], selection.Shortlist.Plots.ToArray());
    }

    [Fact]
    public void TheSavedShortlistReplacesAnUntouchedOneAndJoinsAnUsedOne()
    {
        var saved = new Shortlist(Guid.NewGuid(), "Shortlist");
        saved.Add(Plot(2, 2));
        var untouched = Selection(Scope(HousingDistricts.Mist));
        var used = Selection(Scope(HousingDistricts.Mist));
        used.Toggle(Plot(1, 1));

        Assert.True(untouched.TakeLoaded(saved));
        Assert.False(used.TakeLoaded(saved));

        Assert.Same(saved, untouched.Shortlist);
        Assert.Equal([Plot(1, 1), Plot(2, 2)], used.Shortlist.Plots.ToArray());
    }

    private static FindSelection Selection(AreaScope area) => new(new Shortlist(Guid.NewGuid(), "Shortlist"), area, PlotFilter.Default, ResultSort.Address, 0);
}
