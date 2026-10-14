using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Game.ClientState.GamePad;
using ECommons.DalamudServices;

namespace AutoHouseLottery.Core.Game.Watchers;

// Tells the player's own input apart from the plugin's work, so background settlement starts only when the player has
// left the game alone and pauses when they come back. Keyboard and mouse come from the system's last-input time, which
// the plugin's own travel, walking and dialog presses never touch. A gamepad leaves no trace there, so its buttons and
// sticks are read every frame, and the character moving while none of the plugin's work runs counts as input too.
// Framework thread only.
internal sealed partial class ManualInputWatcher
{
    private const float MovedMeters = 0.5f;

    // The game reports stick tilt from -99 to 99; a little drift stays below this.
    private const float StickDeadZone = 30f;

    private static readonly GamepadButtons[] gamepadButtons =
    [
        GamepadButtons.DpadUp, GamepadButtons.DpadDown, GamepadButtons.DpadLeft, GamepadButtons.DpadRight,
        GamepadButtons.North, GamepadButtons.South, GamepadButtons.West, GamepadButtons.East,
        GamepadButtons.L1, GamepadButtons.L2, GamepadButtons.L3, GamepadButtons.R1, GamepadButtons.R2, GamepadButtons.R3,
        GamepadButtons.Select, GamepadButtons.Start,
    ];

    private Vector3? lastPosition;
    private long lastMovedMs;
    private long lastGamepadMs;
    private bool systemTimeUnavailable;

    // Every frame: a held button or a tilted stick is the player's input.
    public void SampleGamepad(long nowMs)
    {
        var gamepad = Svc.GamepadState;
        if (gamepad.LeftStick.Length() > StickDeadZone || gamepad.RightStick.Length() > StickDeadZone)
        {
            lastGamepadMs = nowMs;
            return;
        }

        for (var buttonIndex = 0; buttonIndex < gamepadButtons.Length; buttonIndex++)
        {
            if (gamepad.Raw(gamepadButtons[buttonIndex]) > 0)
            {
                lastGamepadMs = nowMs;
                return;
            }
        }
    }

    public void Sample(long nowMs, bool ownWorkActive)
    {
        var position = Svc.Objects.LocalPlayer?.Position;
        if (!ownWorkActive && position is { } current && lastPosition is { } previous && Vector3.Distance(current, previous) > MovedMeters)
        {
            lastMovedMs = nowMs;
        }

        lastPosition = position;
    }

    // Time since the player's last own input. Unknown system input time counts as input just now, so background work
    // never starts on a guess.
    public long IdleMs(long nowMs)
    {
        var systemIdle = SystemIdleMs();
        var sinceMoved = lastMovedMs == 0 ? long.MaxValue : nowMs - lastMovedMs;
        var sinceGamepad = lastGamepadMs == 0 ? long.MaxValue : nowMs - lastGamepadMs;
        return Math.Max(0, Math.Min(systemIdle, Math.Min(sinceMoved, sinceGamepad)));
    }

    private long SystemIdleMs()
    {
        if (systemTimeUnavailable)
        {
            return 0;
        }

        try
        {
            var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
            if (GetLastInputInfo(ref info))
            {
                return unchecked((uint)Environment.TickCount - info.Time);
            }
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            systemTimeUnavailable = true;
            RunLog.Warning(exception, "The system's last input time is not available; automatic refunds wait for a manual start");
        }

        return 0;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetLastInputInfo(ref LastInputInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }
}
