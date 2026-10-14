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
        var header = WardReader.ReadHeader(window.LastUpdateSequence);
        Diag($"Ward scan: the selector shows ward {header.ShownWardIndex + 1}; planning {ResidentialDistricts.WardCount} wards.");
        var acknowledged = ImmutableArray.CreateBuilder<WardReading>(ResidentialDistricts.WardCount);
        var stop = WardScanStop.Cancelled;
        int? stoppedAtWard = 0;
        try
        {
            if (!packets.Active)
            {
                stop = WardScanStop.NoPacketObserver;
                return;
            }

            for (var wardIndex = 0; wardIndex < ResidentialDistricts.WardCount; wardIndex++)
            {
                stoppedAtWard = wardIndex;
                var reading = wardIndex == header.ShownWardIndex ? WardPager.AcceptDisplayed(wardIndex, window, packets) : null;
                if (reading is null)
                {
                    if (WardPager.Request(wardIndex) is not { } request)
                    {
                        stop = WardScanStop.RequestRefused;
                        return;
                    }

                    var (acknowledgement, polled) = await AwaitAcknowledgement(request);
                    if (acknowledgement != WardAcknowledgement.Acknowledged)
                    {
                        stop = WardScanResult.StopFor(acknowledgement);
                        return;
                    }

                    reading = polled!;
                    await DelayMs(BetweenRequestsMs);
                }

                acknowledged.Add(reading);
                Diag($"Ward scan: ward {wardIndex + 1} acknowledged, {reading.Rows.Length} rows.");
            }

            stop = WardScanStop.Completed;
            stoppedAtWard = null;
        }
        finally
        {
            Result = new WardScanResult(acknowledged.ToImmutable(), ResidentialDistricts.WardCount, stop, stoppedAtWard);
            Diag($"Ward scan: {Result.Wards.Length} of {Result.WardsPlanned} wards acknowledged, ended {stop}{(stoppedAtWard is { } ward ? $" at ward {ward + 1}" : string.Empty)}.");
        }
    }

    // NextFrame throws once the task is cancelled, so cancellation leaves through the caller's finally.
    private async Task<(WardAcknowledgement Acknowledgement, WardReading? Reading)> AwaitAcknowledgement(WardRequest request)
    {
        while (true)
        {
            var acknowledgement = WardPager.Poll(request, window, packets, out var reading);
            if (acknowledgement != WardAcknowledgement.Pending)
            {
                return (acknowledgement, reading);
            }

            await NextFrame(PollFrames);
        }
    }
}
