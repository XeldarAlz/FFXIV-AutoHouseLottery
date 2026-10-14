using AutoHouseLottery.Core.Game.Model;
using Dalamud.Hooking;
using ECommons.DalamudServices;

namespace AutoHouseLottery.Core.Game.Placard;

// Copies what the game's placard sale handler receives: the housing type, the plot's address and, for an unowned plot
// as PaissaHouse reads it, the sale information block. Optional: when the signature is missing or the computed start
// does not look like a function, nothing is hooked and the placard reader falls back to the agent and the window text.
internal sealed unsafe class PlacardSaleHook : IDisposable
{
    // PaissaHouse (zhudotexe/FFXIV_PaissaHouse, Paissa/LotteryObserver.cs, MIT): two movups that copy the sale block
    // into the agent, 0xA8 bytes into the handler.
    public const string Signature = "41 0F 10 06 0F 11 43 48 41 0F 10 4E 10 0F 11 4B 58";

    private const int FunctionStartBeforeMatch = 0xA8;
    private const int FunctionAlignment = 16;
    // The compiler pads between functions with int3, or the previous function's ret sits right before an aligned start.
    private const byte PaddingInterrupt = 0xCC;
    private const byte PreviousReturn = 0xC3;

    private readonly Hook<HandleSaleInfoDelegate>? hook;
    private readonly object gate = new();
    private PlacardSaleCapture? latest;

    public PlacardSaleHook()
    {
        SignatureFound = Svc.SigScanner.TryScanText(Signature, out var match);
        if (!SignatureFound)
        {
            RunLog.Warning("Placard sale hook: PaissaHouse's signature was not found on this build; the hook stays off.");
            return;
        }

        var start = match - FunctionStartBeforeMatch;
        if (!LooksLikeFunctionStart(start))
        {
            RunLog.Warning($"Placard sale hook: 0x{start:X} does not look like the start of the handler; the hook stays off.");
            return;
        }

        try
        {
            hook = Svc.Hook.HookFromAddress<HandleSaleInfoDelegate>(start, Detour);
            hook.Enable();
            RunLog.Debug($"Placard sale hook: observing the sale handler at 0x{start:X}.");
        }
        catch (Exception exception)
        {
            RunLog.Warning(exception, "Placard sale hook: the hook could not be installed; it stays off");
            hook?.Dispose();
            hook = null;
        }
    }

    private delegate void HandleSaleInfoDelegate(void* agent, byte housingType, ushort territoryTypeId, byte wardIndex, byte plotIndex, short apartmentNumber, byte* saleInfo, long unknown);

    public bool SignatureFound { get; }

    public bool Active => hook is { IsEnabled: true };

    public PlacardSaleCapture? Latest
    {
        get
        {
            lock (gate)
            {
                return latest;
            }
        }
    }

    public void Dispose() => hook?.Dispose();

    private static bool LooksLikeFunctionStart(nint start)
        => start % FunctionAlignment == 0 && *((byte*)start - 1) is PaddingInterrupt or PreviousReturn;

    private void Detour(void* agent, byte housingType, ushort territoryTypeId, byte wardIndex, byte plotIndex, short apartmentNumber, byte* saleInfo, long unknown)
    {
        var sequence = GameSequence.Next();
        try
        {
            var capture = new PlacardSaleCapture(
                housingType <= (byte)PlacardHousingType.Apartment ? (PlacardHousingType)housingType : null,
                housingType,
                new PlotAddress(territoryTypeId, wardIndex, plotIndex),
                apartmentNumber,
                saleInfo == null || housingType != (byte)PlacardHousingType.UnownedHouse ? null : PlacardSaleInfo.Parse(new ReadOnlySpan<byte>(saleInfo, PlacardSaleInfo.Length)),
                sequence);
            lock (gate)
            {
                latest = capture;
            }

            RunLog.Debug($"Placard sale hook: housing type {housingType}, territory {territoryTypeId}, ward {wardIndex + 1}, plot {plotIndex + 1}, sale {capture.Sale?.ToString() ?? "none"}.");
        }
        catch (Exception exception)
        {
            RunLog.Warning(exception, "Placard sale hook: the sale information could not be copied");
        }

        hook!.Original(agent, housingType, territoryTypeId, wardIndex, plotIndex, apartmentNumber, saleInfo, unknown);
    }
}
