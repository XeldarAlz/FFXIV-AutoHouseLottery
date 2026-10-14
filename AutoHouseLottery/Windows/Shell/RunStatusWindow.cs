using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using System.Numerics;

namespace AutoHouseLottery.Windows.Shell;

// The compact strip left on screen while a run or a check of an earlier entry goes on with the main window closed, so
// the work is never invisible. Clicking it opens the main window; it never starts anything itself.
public sealed class RunStatusWindow : Window
{
    private const float StripWidth = 460f;
    private const ImGuiWindowFlags StripFlags =
        ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse
        | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoFocusOnAppearing;

    private readonly Plugin plugin;
    private IDisposable? chrome;
    private IDisposable? bodyFont;

    public RunStatusWindow(Plugin plugin) : base("Auto House Lottery###AutoHouseLotteryRunStrip", StripFlags)
    {
        this.plugin = plugin;
        RespectCloseHotkey = false;
        AllowPinning = false;
        AllowClickthrough = false;
    }

    public override void PreDraw()
    {
        bodyFont = Fonts.PushBody();
        chrome = Styling.PushChrome(Vector2.Zero);
        Size = new Vector2(StripWidth, MiniPlayer.Height() / ImGuiHelpers.GlobalScale);
        SizeCondition = ImGuiCond.Always;
    }

    public override void PostDraw()
    {
        chrome?.Dispose();
        chrome = null;
        bodyFont?.Dispose();
        bodyFont = null;
    }

    public override void Draw()
    {
        if (MiniPlayer.Draw(plugin, ImGui.GetWindowSize(), Styling.WindowRounding * ImGuiHelpers.GlobalScale))
        {
            plugin.ShowPage(AppWindow.RunPage);
        }
    }
}
