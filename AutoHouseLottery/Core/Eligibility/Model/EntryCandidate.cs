using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Eligibility.Model;

internal readonly record struct EntryCandidate(PlotKey Plot, PlotBuyers Buyers, PlotAvailability Availability, long? Deposit);
