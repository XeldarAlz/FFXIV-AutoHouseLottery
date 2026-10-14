# Third-party notices

Auto House Lottery ships no third-party data and, so far, no code copied from other plugins. The one bundled third-party asset is the font below; its license travels with it.

The release zip also carries the compiled libraries the plugin builds against: the ECommons submodule and the NuGet packages pinned in `AutoHouseLottery/packages.lock.json` (the clib automation library and what it depends on). They ship as built and unmodified, each under its own license, and none of their source is copied into this repository. Besides those, the zip holds the plugin itself, its manifest, its icon, the nine language catalogs and the font below.

Lifestream is a separate plugin you install yourself; none of its code ships with Auto House Lottery, which talks to it only through Dalamud's IPC. Housing listings are fetched at run time from the public Aetherphone housing service (`https://housing-api.yozoracho.dev/api/v1`); no listing data ships with the plugin. Game data such as world, district and zone names and the text of the housing dialogs is read from your game client at run time.

## Noto Sans

- Source: Noto Sans Medium by the Noto Project Authors, https://github.com/notofonts/latin-greek-cyrillic, shipped as `AutoHouseLottery/Fonts/NotoSans-Medium-Latin.ttf`.
- Used: the primary font for Latin letters, digits and punctuation in the plugin's windows.
- License: SIL Open Font License 1.1, shipped next to the font in full as `AutoHouseLottery/Fonts/OFL-NotoSans.txt`.

## Aetherphone

- Source: FFXIV-Aetherphone by XeldarAlz, the same author as this project, https://github.com/XeldarAlz/FFXIV-Aetherphone.
- Used: the housing discovery code (the housing service client and its normalization, the cache, the district maps and marker projection, saved plots and reminders, and the Lifestream travel bridge) will be ported from it. Its notices are kept, and the Aetherphone commit each part came from is recorded under [Copied code](#copied-code) when it lands.
- License: GNU Affero General Public License v3.0, the license of this project.

## Game interaction references

What this project knows about the game's housing windows (the plot placard, the lottery entry confirmation, the result dialogs and the housing agents) was learned by reading these projects. Reading them is how the facts were found; any code actually taken from one is listed under [Copied code](#copied-code) with its origin.

- [PaissaHouse-Lite](https://github.com/NightmareXIV/PaissaHouse-Lite), MIT: the placard's sale information block.
- [LlamaLibrary](https://github.com/nt153133/__LlamaLibrary), MIT: the placard agent's fields and the placard button callback.
- [FFXIVClientStructs](https://github.com/aers/FFXIVClientStructs), MIT: `AgentHousingSignboard`, `HousingManager` and the housing packets. It also ships with Dalamud, which the plugin builds against.
- [AltMate](https://github.com/naco-ff14/AltMate), AGPL-3.0: the placard text nodes, the entry confirmation and the result dialogs.
- [HousingHerald](https://github.com/mashirochan/FFXIV-HousingHerald), AGPL-3.0: the placard text nodes and the entry confirmation.
- [ffxiv-datamining](https://github.com/xivapi/ffxiv-datamining), no license: only the row IDs of the game's Addon and LogMessage sheets are used, and their text is read from your own client.

## Copied code

None yet. Each piece of code copied from another project gets an entry here naming the file it became, the project and commit it came from, and its license.
