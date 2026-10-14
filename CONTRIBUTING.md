# Contributing

Thanks for taking an interest. This is a small solo project, but PRs are welcome and I'll review them.

## Quick start

```bash
git clone --recurse-submodules https://github.com/XeldarAlz/FFXIV-AutoHouseLottery.git
cd FFXIV-AutoHouseLottery
dotnet build AutoHouseLottery.sln -c Release
dotnet test AutoHouseLottery.sln -c Release
```

You need the .NET 10 SDK. The plugin requires Dalamud at runtime; CI pulls a Dalamud dev build automatically and that's enough to compile. When your Dalamud build is not where XIVLauncher puts it, pass its folder with `-p:DalamudLibPath=<folder>/`. See `.github/workflows/ci.yml` if you want to reproduce CI locally.

`dotnet test AutoHouseLottery.Tests -c Release` runs the unit tests alone. They cover the gil arithmetic, the housing readers' parsing and matching rules (sheet-row dialog matching, ward acknowledgement, the placard sale block, the lottery record, arrival and travel ownership), check that all nine catalogs carry the same keys in the same order with the same placeholders and no em dashes, and check that the newest changelog entry matches the version in `Directory.Build.props`. The test project builds without Dalamud: it compiles the pure folders of the plugin through `<Compile Include>` links, so a new `Core/<Area>/Model/` folder is added to that list when it lands.

Load the built plugin via `/xlsettings` -> **Experimental** -> **Dev Plugin Locations**, pointing at `AutoHouseLottery/bin/Release/AutoHouseLottery.dll`.

## Project layout

Folders marked *planned* arrive with later phases of the plugin.

- `AutoHouseLottery/Core/Housing/Model/` (*planned*): plot and lottery period identity (`PlotKey`, `CycleKey`), plot observations with their own source times, and the coverage of what was actually read.
- `AutoHouseLottery/Core/Providers/` (*planned*): the Aetherphone housing service client and its disk cache, keeping fetch age apart from observation age.
- `AutoHouseLottery/Core/Selection/Model/` (*planned*): the shortlist, the best match ranking, Same plot and Spread placement, and the assignment planner.
- `AutoHouseLottery/Core/Eligibility/Model/` (*planned*): eligibility, ownership group and budget rules, where Unknown never counts as eligible.
- `AutoHouseLottery/Core/Game/`: the duty and GM watchers and text input focus, the ward, placard, lottery and character readers, the sheet-row dialog matching and the gated dialog buttons; their raw readings and pure rules live under `Core/Game/Model/`.
- `AutoHouseLottery/Core/Travel/`: travel to a plot through Lifestream, arrival checks and the final approach to a plot's placard, with their pure rules under `Core/Travel/Model/`.
- `AutoHouseLottery/Core/Tasks/`: the run controller, the single owner of every game-changing action; later the entry and settlement drivers and the result and refund scheduler, with their pure rules under `Core/Tasks/Model/`.
- `AutoHouseLottery/Core/Persistence/` (*planned*): the action journal and entry receipts, written before any irreversible confirmation.
- `AutoHouseLottery/Core/Ipc/`: the guarded IPC call helper and the Lifestream and vnavmesh adapters; later the plugin's own public API.
- `AutoHouseLottery/Core/External/`: the helper plugins on the Plugins page and their one-click install.
- `AutoHouseLottery/Core/Diagnostics/` (*planned*): the compatibility report and sanitized traces for bug reports.
- `AutoHouseLottery/Core/Changelog/`: the changelog entries.
- `AutoHouseLottery/Core/` itself, `Core/Shared/` and `Core/Debug/`: the run log behind the Console page, gil arithmetic, the `/ahl target` dump and the `/ahl probe` housing readings.
- `AutoHouseLottery/Core/Localization/` and `AutoHouseLottery/Localization/`: the string table and the nine language catalogs.
- `AutoHouseLottery/Windows/`: the ImGui window with its shell, pages (Find a home, My lottery, Saved, and the Settings, Plugins, Console, Changelog and About pages), sections, settings and components.
- `AutoHouseLottery/`: plugin entry points, config, command wiring.
- `AutoHouseLottery.Tests/`: unit tests for everything above that runs without the game.
- `fixtures/` (*planned*): sanitized, versioned observations of the game's housing windows and the housing service, recorded per game build and used by the tests.
- `ECommons/`: submodule, shared Dalamud helpers. Don't patch this directly; upstream it.

Keep logic small and direct. This plugin has one job.

## Before you open a PR

1. `dotnet build -c Release` cleanly, and `dotnet test -c Release` passes.
2. Test in game: at least one dry run of the entry flow, from Enter through travel and the plot's placard to the lottery confirmation, where the dry run presses Cancel, with the Console open. A real entry deposits the full price of the plot and cannot be cancelled, so make one only on a plot and with a budget you chose deliberately, watch it the whole way and attach its Console lines. When your change touches results, refunds or claims, test it on a real result of your own. The housing districts and their subdivisions differ, and Private and Free Company plots take different paths, so a fix that works for one may not work for another.
3. Keep the diff focused. One concern per PR.
4. Match the existing style. No heavy abstractions "for later."
5. Every string a user sees goes through `Core/Localization/L*.cs` and all nine catalogs under `AutoHouseLottery/Localization/`, with the same keys in the same order. CI checks the keys and rejects em dashes.
6. If your change affects what a user sees or types (commands, window layout, settings), update the README.
7. If you used AI beyond autocomplete, say which level in the PR description. It is one line, and [AI-USAGE.md](AI-USAGE.md) explains the level names and why I ask.

## Good first issues

Check the tracker for anything labeled `good first issue`. Travel quirks (a ward or subdivision Lifestream misses, a placard the plugin can't reach) and lottery dialog quirks in a client language other than English are usually the lowest-friction way to help: name the district, ward and plot where it misbehaved, attach a log of what the plugin did vs. what should have happened, and a fix is usually a small change.

## Security

Please don't file public issues for security problems; see [SECURITY.md](SECURITY.md).

## Code of conduct

See [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md). Be decent.

## License

By contributing, you agree your contributions are licensed under AGPL-3.0-or-later with the additional terms in [NOTICE](NOTICE), the same as the project.
