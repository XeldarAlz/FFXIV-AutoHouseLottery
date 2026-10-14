# Third-party notices

Auto House Lottery ships no third-party data. The few pieces adapted from other projects are listed under [Copied code](#copied-code) with their origin. The one bundled third-party asset is the font below; its license travels with it.

The release zip also carries the compiled libraries the plugin builds against: the ECommons submodule and the NuGet packages pinned in `AutoHouseLottery/packages.lock.json` (the clib automation library and what it depends on). They ship as built and unmodified, each under its own license, and none of their source is copied into this repository. Besides those, the zip holds the plugin itself, its manifest, its icon, the nine language catalogs and the font below.

Lifestream and vnavmesh are separate plugins you install yourself; none of their code ships with Auto House Lottery, which talks to them only through Dalamud's IPC. Housing listings are fetched at run time from the public Aetherphone housing service (`https://housing-api.yozoracho.dev/api/v1`); no listing data ships with the plugin. Game data such as world, district and zone names and the text of the housing dialogs is read from your game client at run time.

## Noto Sans

- Source: Noto Sans Medium by the Noto Project Authors, https://github.com/notofonts/latin-greek-cyrillic, shipped as `AutoHouseLottery/Fonts/NotoSans-Medium-Latin.ttf`.
- Used: the primary font for Latin letters, digits and punctuation in the plugin's windows.
- License: SIL Open Font License 1.1, shipped next to the font in full as `AutoHouseLottery/Fonts/OFL-NotoSans.txt`.

## Aetherphone

- Source: FFXIV-Aetherphone by XeldarAlz, the same author as this project, https://github.com/XeldarAlz/FFXIV-Aetherphone.
- Used: the Lifestream travel bridge's housing address and the housing discovery code (the housing service client and its normalization, the cache and refresh service, the district maps and marker projection, saved plots and reminders). Each file is listed under [Ported from Aetherphone](#ported-from-aetherphone) with the commit it came from. The data center region rows that tell a Chinese world apart (`AutoHouseLottery/Core/Housing/Model/WorldRegion.cs`) follow its `Core/Game/GameData.cs`, as facts only.
- License: GNU Affero General Public License v3.0, the license of this project.

## Auto GC Supply

- Source: FFXIV-AutoGCSupply by XeldarAlz, the same author as this project, https://github.com/XeldarAlz/FFXIV-AutoGCSupply. The plugin's scaffold comes from it: the window shell and its components, the localization, the Console and its run log, the settings pages, the GM alert, the duty and input watchers, the helper plugins page with its one-click install, the changelog page, the issue templates and the workflows.
- Used: beyond the scaffold, the task, walking and navmesh helpers and the Timers window pass are adapted from it; each file is listed under [Ported from Auto GC Supply](#ported-from-auto-gc-supply).
- License: GNU Affero General Public License v3.0, the license of this project.

## Game interaction references

What this project knows about the game's housing windows (the plot placard, the lottery entry confirmation, the result dialogs and the housing agents) was learned by reading these projects. Reading them is how the facts were found; any code actually taken from one is listed under [Copied code](#copied-code) with its origin.

- [PaissaHouse](https://github.com/zhudotexe/FFXIV_PaissaHouse), MIT, the maintained upstream: the placard sale handler and its sale information block, and the purchase and tenant type bytes of the ward packet.
- [PaissaHouse-Lite](https://github.com/NightmareXIV/PaissaHouse-Lite), MIT: the placard's sale information block.
- [LlamaLibrary](https://github.com/nt153133/__LlamaLibrary), MIT: the placard agent's fields, the placard button callback, and the ward selector's values and ward callback.
- [FFXIVClientStructs](https://github.com/aers/FFXIVClientStructs), MIT: `AgentHousingSignboard`, `HousingManager` and the housing packets. It also ships with Dalamud, which the plugin builds against.
- [AltMate](https://github.com/naco-ff14/AltMate), AGPL-3.0: the placard text nodes, the entry confirmation and the result dialogs.
- [HousingHerald](https://github.com/mashirochan/FFXIV-HousingHerald), AGPL-3.0: the placard text nodes and the entry confirmation.
- [Lifestream](https://github.com/NightmareXIV/Lifestream): the names and signatures of the IPC gates the travel adapter calls, read from its public `IPC/IPCProvider.cs`.
- HouseHunter, closed source, read as a recovered build: the mechanics the code comments cite, namely the personal lottery record behind `HousingManager + 0xB0` and its layout, the Timers window's values and lottery tab, and the ward selector's values and ward callback. Facts only; none of its code is copied.
- [ffxiv-datamining](https://github.com/xivapi/ffxiv-datamining), no license: the plugin uses only the row IDs of the game's Addon, LogMessage and EObjName sheets and reads their text from your own client. The unit tests hold the text of Addon rows 7122, 7123, 7124 and 7128 in English, German, French and Japanese, so the dialog matcher is tested against the real row structure.

## Copied code

Each piece of code copied from another project has an entry here naming the file it became, the project and commit it came from, and its license.

- `AutoHouseLottery/Core/Game/Placard/PlacardSaleHook.cs` and `AutoHouseLottery/Core/Game/Model/PlacardSaleInfo.cs`: the placard sale handler's signature, the offset from it to the handler, the handler's parameters and the sale information layout, adapted from `Paissa/LotteryObserver.cs` and `Structures/Lottery.cs` of [PaissaHouse](https://github.com/zhudotexe/FFXIV_PaissaHouse) at commit `2ac7ba9c20cf087a6e28199ed9e59f12a1716ccb`. MIT, Copyright (c) 2021 Andrew Zhu.
- `AutoHouseLottery/Core/Game/Ward/WardPortalObserver.cs`: the offsets of the ward packet's purchase and tenant type bytes, from `Structures/HousingWardInfo.cs` of the same PaissaHouse commit. MIT, Copyright (c) 2021 Andrew Zhu.
- `AutoHouseLottery/Core/Game/Placard/SignboardAgentLayout.cs`: the six code patterns that locate the placard agent's fields, from `Memory/Offsets.cs` (`AgentHousingSignBoardOffsets`) of [LlamaLibrary](https://github.com/nt153133/__LlamaLibrary) at commit `073b4a4e02fde7f02ca9c30c6a8a4e8610e7cd44`. MIT, Copyright (c) 2022 nt153133.

The MIT license of both projects reads:

> Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

### Ported from Auto GC Supply

From FFXIV-AutoGCSupply at commit `9ddf709a2299a61a562c9129920cd991308aabcc`, AGPL-3.0:

- `AutoHouseLottery/Core/Tasks/AutoCommon.cs` and `AutoCommon.Cancellable.cs`, `MoveOp.cs`, `StuckDetector.cs`, `MoveStallTracker.cs` and `TaskStarter.cs`: from the files of the same names under `AutoGcSupply/Core/Tasks/`, trimmed to what the plugin uses.
- `AutoHouseLottery/Core/Ipc/NavmeshIpc.cs`: from `AutoGcSupply/Core/Ipc/NavmeshIPC.cs`, trimmed.
- `AutoHouseLottery/Core/Game/Model/DisplayText.cs`: from `AutoGcSupply/Core/HandIn/Model/PromptText.cs`.
- `AutoHouseLottery/Core/Game/AddonReads.cs`: the AtkValue reads from `AutoGcSupply/Core/HandIn/AtkValueReads.cs`.
- `AutoHouseLottery/Core/Game/Lottery/LotteryTimersPass.cs`: the shape of `AutoGcSupply/Core/Requests/TimersLoader.cs`.

### Ported from Aetherphone

- `AutoHouseLottery/Core/Ipc/LifestreamIpc.cs`: the Lifestream housing address and the `GoToHousingAddress` call, from `src/Aetherphone/Core/Venues/LifestreamBridge.cs` at Aetherphone commit `255d4dd538d016036c8432a284948be4e0e74512`. AGPL-3.0.

The housing discovery files below come from `src/Aetherphone/Core/Housing/` and `src/Aetherphone/Core/Net/` at the same Aetherphone commit `255d4dd538d016036c8432a284948be4e0e74512`, AGPL-3.0, with the phone interface left out and the data normalized into this plugin's plot store:

- `AutoHouseLottery/Core/Providers/Model/HousingEndpoints.cs` and the `Feed*` response types: from `HousingEndpoints.cs` and `PaissaModels.cs`, global service only.
- `AutoHouseLottery/Core/Providers/Model/FeedNormalizer.cs`: the field mapping of `HousingRestProvider.cs` (zero-based wards and plots, size, purchase flags and lottery phase), with stricter address, timestamp, count and completeness checks.
- `AutoHouseLottery/Core/Providers/HousingFeedClient.cs` and `Model/BoundedBody.cs`: the request, timeout, Retry-After and response size handling of `Core/Net/HttpService.cs` and the two-request limit of `Core/Net/RequestThrottle.cs`.
- `AutoHouseLottery/Core/Providers/HousingFeedService.cs` and `Model/RefreshCadence.cs`: the refresh, backoff and caching flow and the cadence constants of `HousingService.cs` and `HousingRecords.cs`.
- `AutoHouseLottery/Core/Providers/HousingFeedCache.cs`: from `HousingCache.cs`, keeping each response with its receipt instead of the converted plots.
- `AutoHouseLottery/Core/Maps/HousingGameMaps.cs`, `MapTextures.cs` and `Model/MapProjection.cs`: from `HousingGameMaps.cs` and `Core/Maps/MapTextures.cs`.
- `AutoHouseLottery/Core/Find/Model/PlotFilter.cs`: the filter model of `HousingFilterState.cs`, extended to areas, buyer type, price and the lottery statuses.
- `AutoHouseLottery/Core/Saved/SavedService.cs`, `NoticeAnnouncer.cs` and `Model/NoticeRules.cs`, `NoticePlanner.cs`, `NoticeLedger.cs`: the watch list reconciliation and the deduplicated reminders of `HousingWatchStore.cs` and `HousingReminderService.cs`.
- `AutoHouseLottery/Windows/Find/FindText.cs`: the labels and countdown of `HousingFormat.cs`.
