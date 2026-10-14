using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Tasks;
using System.Collections.Immutable;
using System.Threading.Tasks;

namespace AutoHouseLottery.Core.Game.Ward;

// Pages the open ward selector through every ward of its district and keeps only the wards whose rows were
// acknowledged for them. The first ward that is not acknowledged ends the scan, so the result's coverage is exact. The
// plan is always the known ward count; the window's own count (value 4) is only logged until a test in game confirms it.
internal sealed class WardScanTask(AddonUpdateCounter window, WardPortalObserver packets) : AutoCommon
{
    private const int PollFrames = 5;
    // A pause between ward requests, so paging stays at the pace of a player clicking through the list.
    private const int BetweenRequestsMs = 500;

    public WardScanResult? Result { get; private set; }

    protected override async Task Execute()
    {
        Status = "Scanning the wards";
        WardReader.ReadDisplayed(window.LastUpdateSequence, out var displayed, out var shownWardIndex);
        Diag($"Ward scan: the selector shows ward {shownWardIndex + 1}, value 4 reads {displayed?.WardCount?.ToString() ?? "no count"}; planning {ResidentialDistricts.WardCount} wards.");
        var acknowledged = ImmutableArray.CreateBuilder<WardReading>(ResidentialDistricts.WardCount);
        var stop = WardScanStop.Completed;
        int? stoppedAtWard = null;
        for (var wardIndex = 0; wardIndex < ResidentialDistricts.WardCount; wardIndex++)
        {
            if (CancelToken.IsCancellationRequested)
            {
                stop = WardScanStop.Cancelled;
                stoppedAtWard = wardIndex;
                break;
            }

            var reading = wardIndex == shownWardIndex ? WardPager.AcceptDisplayed(wardIndex, window, packets) : null;
            if (reading is null)
            {
                if (WardPager.Request(wardIndex) is not { } request)
                {
                    stop = WardScanStop.RequestRefused;
                    stoppedAtWard = wardIndex;
                    break;
                }

                (var acknowledgement, reading) = await AwaitAcknowledgement(request);
                if (acknowledgement != WardAcknowledgement.Acknowledged)
                {
                    stop = CancelToken.IsCancellationRequested ? WardScanStop.Cancelled : WardScanResult.StopFor(acknowledgement);
                    stoppedAtWard = wardIndex;
                    break;
                }

                await DelayMs(BetweenRequestsMs);
            }

            acknowledged.Add(reading!);
            Diag($"Ward scan: ward {wardIndex + 1} acknowledged ({reading!.Correlation}), {reading.Rows.Length} rows.");
        }

        Result = new WardScanResult(acknowledged.ToImmutable(), ResidentialDistricts.WardCount, stop, stoppedAtWard);
        Diag($"Ward scan: {Result.Wards.Length} of {Result.WardsPlanned} wards acknowledged, ended {stop}{(stoppedAtWard is { } ward ? $" at ward {ward + 1}" : string.Empty)}.");
    }

    private async Task<(WardAcknowledgement Acknowledgement, WardReading? Reading)> AwaitAcknowledgement(WardRequest request)
    {
        while (!CancelToken.IsCancellationRequested)
        {
            var acknowledgement = WardPager.Poll(request, window, packets, out var reading);
            if (acknowledgement != WardAcknowledgement.Pending)
            {
                return (acknowledgement, reading);
            }

            await NextFrame(PollFrames);
        }

        return (WardAcknowledgement.Pending, null);
    }
}
