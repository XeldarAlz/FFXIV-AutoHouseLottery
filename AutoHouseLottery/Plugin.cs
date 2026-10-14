using AutoHouseLottery.Core;
using AutoHouseLottery.Core.Debug;
using AutoHouseLottery.Core.Find;
using AutoHouseLottery.Core.Game;
using AutoHouseLottery.Core.Game.Dialogs;
using AutoHouseLottery.Core.Game.Placard;
using AutoHouseLottery.Core.Game.Ward;
using AutoHouseLottery.Core.Game.Watchers;
using AutoHouseLottery.Core.Integration;
using AutoHouseLottery.Core.Localization;
using AutoHouseLottery.Core.Providers;
using AutoHouseLottery.Core.Tasks;
using AutoHouseLottery.Core.Travel;
using AutoHouseLottery.Windows;
using AutoHouseLottery.Windows.Shell;
using clib;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.DalamudServices;
using System.Globalization;
using System.IO;

namespace AutoHouseLottery;

public sealed class Plugin : IDalamudPlugin
{
    private const string ProbeVerb = "probe";
    private const string DevVerb = "dev";

    [PluginService]
    internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

    [PluginService]
    internal static ICommandManager CommandManager { get; private set; } = null!;

    internal static Plugin Instance { get; private set; } = null!;

    internal Configuration Configuration { get; }
    internal WindowSystem WindowSystem { get; } = new("AutoHouseLottery");
    internal RunController Controller { get; }
    internal HousingObservers Housing { get; }
    internal HousingContext HousingData { get; }
    internal Discovery Discovery { get; }

    private readonly DutyWatcher dutyWatcher;
    private readonly GmAlertWatcher gmAlertWatcher;
    private readonly AddonUpdateCounter wardWindow;
    private readonly WardPortalObserver wardPackets;
    private readonly AddonUpdateCounter placardWindow;
    private readonly PlacardSaleHook placardSales;
    private readonly HousingLogWatcher housingLog;
    private readonly HousingFeedService housingFeed;
    private readonly AppWindow appWindow;
    private readonly RunStatusWindow runStrip;
    private readonly CommandInfo primaryCommand;
    private readonly CommandInfo aliasCommand;

    // The settings file could not be read at load; said once in chat when the character is in game, with where the old
    // file was kept, null when it could not be.
    private bool configurationReset;
    private string? configurationBackupPath;

    // A step that fails undoes the ones before it, since Dalamud never disposes a plugin whose constructor threw.
    public Plugin()
    {
        Instance = this;
        var completed = new Stack<Action>();
        try
        {
            ECommonsMain.Init(PluginInterface, this);
            completed.Push(ECommonsMain.Dispose);
            CLibMain.Init(PluginInterface, this, CLibModule.Automation);
            completed.Push(CLibMain.Dispose);

            Configuration = LoadConfiguration();

            wardWindow = new AddonUpdateCounter(WardReader.AddonName);
            completed.Push(wardWindow.Dispose);
            wardPackets = new WardPortalObserver();
            completed.Push(wardPackets.Dispose);
            placardWindow = new AddonUpdateCounter(PlacardReader.AddonName);
            completed.Push(placardWindow.Dispose);
            placardSales = new PlacardSaleHook();
            completed.Push(placardSales.Dispose);
            housingLog = new HousingLogWatcher();
            completed.Push(housingLog.Dispose);
            Housing = new HousingObservers(wardWindow, wardPackets, placardWindow, placardSales, SignboardAgentLayout.Resolve(placardSales.SignatureFound), housingLog);
            InitializeLocalization();
            var directory = PluginInterface.GetPluginConfigDirectory();
            housingFeed = Discovery.CreateFeed(directory, PluginVersion);
            completed.Push(housingFeed.Dispose);
            HousingData = new HousingContext(Configuration, directory, housingFeed);
            completed.Push(HousingData.Dispose);
            Discovery = new Discovery(Configuration, directory, housingFeed, new CurrentCharacter(HousingData), () => HousingData.JournalSnapshot, () => appWindow?.IsOpen == true);
            completed.Push(Discovery.Dispose);
            Controller = new RunController(HousingData, Housing, directory);
            completed.Push(Controller.Shutdown);
            dutyWatcher = new DutyWatcher();
            completed.Push(dutyWatcher.Dispose);
            gmAlertWatcher = new GmAlertWatcher();
            completed.Push(gmAlertWatcher.Dispose);
            Fonts.Initialize(PluginInterface.UiBuilder, PluginDirectory);
            completed.Push(Fonts.Dispose);
            appWindow = new AppWindow(this);
            completed.Push(appWindow.Dispose);
            WindowSystem.AddWindow(appWindow);
            completed.Push(WindowSystem.RemoveAllWindows);
            runStrip = new RunStatusWindow(this);
            WindowSystem.AddWindow(runStrip);

            primaryCommand = new CommandInfo(OnCommand) { HelpMessage = Loc.T(L.Plugin.CommandHelp) };
            aliasCommand = new CommandInfo(OnCommand) { HelpMessage = Loc.T(L.Plugin.CommandHelpAlias) };
            CommandManager.AddHandler(HouseLotteryConstants.PrimaryCommand, primaryCommand);
            completed.Push(() => CommandManager.RemoveHandler(HouseLotteryConstants.PrimaryCommand));
            CommandManager.AddHandler(HouseLotteryConstants.AliasCommand, aliasCommand);
            completed.Push(() => CommandManager.RemoveHandler(HouseLotteryConstants.AliasCommand));

            PluginInterface.UiBuilder.Draw += OnDraw;
            completed.Push(() => PluginInterface.UiBuilder.Draw -= OnDraw);
            PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
            completed.Push(() => PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi);
            PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
            completed.Push(() => PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi);

            Svc.ClientState.Login += OnLoadedInGame;
            completed.Push(() => Svc.ClientState.Login -= OnLoadedInGame);
            Svc.ClientState.Logout += OnLogout;
            completed.Push(() => Svc.ClientState.Logout -= OnLogout);
            if (Svc.ClientState.IsLoggedIn)
            {
                _ = Svc.Framework.RunOnFrameworkThread(OnLoadedInGame);
            }
        }
        catch
        {
            Undo(completed);
            throw;
        }
    }

    private static string PluginDirectory => PluginInterface.AssemblyLocation.DirectoryName ?? string.Empty;

    private static string PluginVersion => typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "0";

    public void Dispose()
    {
        TaskStarter.StopCurrent();
        Controller.Shutdown();
        HousingTravel.Release();

        PluginInterface.UiBuilder.Draw -= OnDraw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        Svc.ClientState.Login -= OnLoadedInGame;
        Svc.ClientState.Logout -= OnLogout;
        Configuration.SaveIfPending();

        WindowSystem.RemoveAllWindows();
        appWindow.Dispose();
        Fonts.Dispose();

        CommandManager.RemoveHandler(HouseLotteryConstants.PrimaryCommand);
        CommandManager.RemoveHandler(HouseLotteryConstants.AliasCommand);

        housingLog.Dispose();
        placardSales.Dispose();
        placardWindow.Dispose();
        wardPackets.Dispose();
        wardWindow.Dispose();
        dutyWatcher.Dispose();
        gmAlertWatcher.Dispose();
        Discovery.Dispose();
        housingFeed.Dispose();
        HousingData.Dispose();

        CLibMain.Dispose();
        ECommonsMain.Dispose();
    }

    public void OnLanguageChanged()
    {
        primaryCommand.HelpMessage = Loc.T(L.Plugin.CommandHelp);
        aliasCommand.HelpMessage = Loc.T(L.Plugin.CommandHelpAlias);
    }

    public void ToggleMainUi() => appWindow.Toggle();

    public void ShowPage(AppWindow.Page page) => appWindow.Show(page);

    public void ToggleConfigUi() => appWindow.TogglePage(AppWindow.Page.Settings);

    public void ToggleAboutUi() => appWindow.TogglePage(AppWindow.Page.About);

    public void ToggleDependenciesUi() => appWindow.TogglePage(AppWindow.Page.Plugins);

    public void ToggleLotteryUi() => appWindow.TogglePage(AppWindow.Page.MyLottery);

    public void ToggleSavedUi() => appWindow.TogglePage(AppWindow.Page.Saved);

    public void ToggleLogUi() => appWindow.TogglePage(AppWindow.Page.Log);

    public void ToggleChangelogUi() => appWindow.TogglePage(AppWindow.Page.Changelog);

    private void OnCommand(string command, string args)
    {
        var trimmed = args.Trim();
        if (trimmed.Equals("config", StringComparison.OrdinalIgnoreCase))
        {
            ToggleConfigUi();
        }
        else if (trimmed.Equals("about", StringComparison.OrdinalIgnoreCase))
        {
            ToggleAboutUi();
        }
        else if (trimmed.Equals("deps", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("dependencies", StringComparison.OrdinalIgnoreCase))
        {
            ToggleDependenciesUi();
        }
        else if (trimmed.Equals("lottery", StringComparison.OrdinalIgnoreCase))
        {
            ToggleLotteryUi();
        }
        else if (trimmed.Equals("saved", StringComparison.OrdinalIgnoreCase))
        {
            ToggleSavedUi();
        }
        else if (trimmed.Equals("log", StringComparison.OrdinalIgnoreCase))
        {
            ToggleLogUi();
        }
        else if (trimmed.Equals("changelog", StringComparison.OrdinalIgnoreCase))
        {
            ToggleChangelogUi();
        }
        else if (trimmed.Equals("target", StringComparison.OrdinalIgnoreCase))
        {
            TargetDumper.Dump();
        }
        else if (trimmed.Equals("stop", StringComparison.OrdinalIgnoreCase))
        {
            Controller.Stop();
            Controller.StopSettlement();
        }
        else if (trimmed.Equals("pause", StringComparison.OrdinalIgnoreCase))
        {
            Controller.Pause(PauseReason.Manual);
        }
        else if (trimmed.Equals("resume", StringComparison.OrdinalIgnoreCase))
        {
            Controller.Resume();
        }
        else if (StartsWithVerb(trimmed, ProbeVerb))
        {
            HousingProbe.Run(trimmed[ProbeVerb.Length..], Housing, HousingData);
        }
        else if (StartsWithVerb(trimmed, DevVerb))
        {
            DevCommands.Run(trimmed[DevVerb.Length..], Controller, HousingData);
        }
        else
        {
            ToggleMainUi();
        }
    }

    private static bool StartsWithVerb(string arguments, string verb)
        => arguments.StartsWith(verb, StringComparison.OrdinalIgnoreCase)
        && (arguments.Length == verb.Length || char.IsWhiteSpace(arguments[verb.Length]));

    private void OnDraw()
    {
        var status = Controller.Status;
        runStrip.IsOpen = !appWindow.IsOpen && (Controller.Busy || (status.RunId != Guid.Empty && status.Step == Core.Tasks.Model.EntryStep.NeedsReconciliation));
        WindowSystem.Draw();
        Configuration.FlushPendingSave();
    }

    private void InitializeLocalization()
    {
        var directory = Path.Combine(PluginDirectory, "Localization");
        if (string.IsNullOrEmpty(Configuration.Language))
        {
            Configuration.Language = DetectLanguage();
            Configuration.Save();
        }

        Loc.Initialize(Configuration.Language, directory);
    }

    private static string DetectLanguage()
    {
        var dalamudLanguage = PluginInterface.UiLanguage;
        if (Languages.IsKnown(dalamudLanguage))
        {
            return Languages.Resolve(dalamudLanguage).Code;
        }

        switch (Svc.ClientState.ClientLanguage)
        {
            case Dalamud.Game.ClientLanguage.German: return Languages.German.Code;
            case Dalamud.Game.ClientLanguage.French: return Languages.French.Code;
            case Dalamud.Game.ClientLanguage.Japanese: return Languages.Japanese.Code;
        }

        var osLanguage = CultureInfo.InstalledUICulture.TwoLetterISOLanguageName;
        return Languages.IsKnown(osLanguage) ? Languages.Resolve(osLanguage).Code : Languages.English.Code;
    }

    private void OnLogout(int type, int code) => Controller.Interrupt();

    private static void Undo(Stack<Action> completed)
    {
        while (completed.Count > 0)
        {
            var step = completed.Pop();
            try
            {
                step();
            }
            catch (Exception)
            {
                // Loading has already failed; the error Dalamud reports is the one that stopped it.
            }
        }
    }

    // A settings file that cannot be read must not keep the plugin from loading: it is copied next to itself under a
    // dated name, the plugin starts from the defaults, and the chat says so once the character is in game.
    private Configuration LoadConfiguration()
    {
        Configuration? loaded = null;
        var unreadable = false;
        try
        {
            loaded = PluginInterface.GetPluginConfig() as Configuration;
            unreadable = loaded is null && PluginInterface.ConfigFile.Exists;
        }
        catch (Exception exception)
        {
            RunLog.Error(exception, "The settings file could not be read");
            unreadable = true;
        }

        if (unreadable)
        {
            configurationReset = true;
            configurationBackupPath = BackUpUnreadableConfiguration();
        }

        var configuration = loaded ?? Configuration.CreateFresh();
        if (configuration.Migrate())
        {
            configuration.Save();
        }

        return configuration;
    }

    private static string? BackUpUnreadableConfiguration()
    {
        var file = PluginInterface.ConfigFile;
        var backup = Path.Combine(file.DirectoryName ?? string.Empty, $"{Path.GetFileNameWithoutExtension(file.Name)}.unreadable-{DateTime.Now:yyyyMMdd-HHmmss}{file.Extension}");
        try
        {
            File.Copy(file.FullName, backup, overwrite: false);
            RunLog.Warning($"The unreadable settings file was kept as {backup}; starting from default settings.");
            return backup;
        }
        catch (Exception exception)
        {
            RunLog.Error(exception, $"The unreadable settings file could not be kept as {backup}; starting from default settings");
            return null;
        }
    }

    private void ShowConfigurationNotice()
    {
        if (!configurationReset)
        {
            return;
        }

        configurationReset = false;
        var notice = configurationBackupPath is { } backup ? Loc.T(L.Plugin.ConfigReset, backup) : Loc.T(L.Plugin.ConfigResetUnsaved);
        Svc.Chat.PrintError($"{HouseLotteryConstants.LogPrefix} {notice}");
    }

    private void OnLoadedInGame()
    {
        ShowConfigurationNotice();
        if (!Configuration.AutoShowOnLogin)
        {
            return;
        }

        appWindow.Show(AppWindow.Page.FindHome);
    }
}
