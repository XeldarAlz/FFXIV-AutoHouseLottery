using System.Collections.Immutable;
using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Selection.Model;

internal readonly record struct ShortlistSnapshot(Guid Id, int Revision, ImmutableArray<PlotKey> Plots);
