using Dalamud.Plugin.Ipc;
using ECommons.DalamudServices;

namespace AutoHouseLottery.Core.Ipc;

// AutoRetainer's multi-mode logs characters in and out on its own, so no character switch may start while it is on. The
// gate name is checked against AutoRetainer's Modules/IPC.cs (PunishXIV/AutoRetainer cc020df).
internal sealed class AutoRetainerIpc
{
    private const string MultiModeFailed = "AutoRetainer GetMultiModeEnabled failed";

    private static AutoRetainerIpc? instance;

    private readonly ICallGateSubscriber<bool> getMultiModeEnabled;

    private AutoRetainerIpc()
    {
        getMultiModeEnabled = Svc.PluginInterface.GetIpcSubscriber<bool>("AutoRetainer.GetMultiModeEnabled");
    }

    public static AutoRetainerIpc Instance => instance ??= new AutoRetainerIpc();

    // False when AutoRetainer is not loaded; null when it is loaded but the call failed, which callers treat as on.
    public bool? MultiModeEnabled()
        => getMultiModeEnabled.HasFunction ? IpcGate.Invoke<bool?>(true, () => getMultiModeEnabled.InvokeFunc(), null, MultiModeFailed) : false;
}
