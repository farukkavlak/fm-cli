# Porting to FM27

- What carries over from FM26, what must be rebuilt, how to check the first day.
- Details: the code and `docs/reference/`. Open work: `docs/BACKLOG.md`.

## Overview

- `fm` is a terminal client for the real Football Manager. It does not reimplement the game.
- A BepInEx plugin inside the game exposes its data tree and events over HTTP.
- The CLI reads and drives that.

```
fm (C# CLI)  ──HTTP──▶  127.0.0.1:7777  ◀──  FmCliProbe (BepInEx plugin, in the game)
MCP server (same CLI)                        └─ SI.Bindable tree + navigation + events
```

- The game stays open in the background. No screen capture, no mouse, no vision.

## Rules

- **Bind by identity, not by text.** Data path, event name, game id, page GUID. Screen words are
  the last resort.
- **Wait for the game's signal.** A value changed, a row was drawn, `Processing` ended, a page
  bound. A timeout is only an upper bound.
- **Do not replace one dependency with another.** Swapping a fixed delay for a label or a style is
  still a dependency. Remove all of them together.
- **Verify results.** FM often says it did something and does nothing. Read the result back.
- **Keep questions and actions apart.** A path that asks must not change the game.
- **Measure before coding.** Three states (page first open, already open, another page open) and
  two languages (Turkish and English).
- **Readable output first.** A working command is not enough; the terminal output must be clear and quick.

Attach order, strongest first:

1. Data path + number/enum field, event name, game id, page GUID.
2. Data path + a `…String` field whose value comes in the game's language.
3. Control/element name, `up=N`, row order.
4. Screen text or sentence parsing.

New code starts at 1. If it must go lower, say why in a comment.

## What carries over

Most of the plumbing does not depend on the game's content:

| Part | Where |
|---|---|
| HTTP bridge (plugin side) | `plugin/FmCliProbe/src/Bridge/` |
| IL2CPP helpers, spans, key paths | `plugin/FmCliProbe/src/Bridge/`, `Reads/` |
| Game translation table layer | `plugin/FmCliProbe/src/Reads/Text.cs` |
| Bridge client, `Until` (wait for a signal) | `cli/fm/Core/Bridge.cs` |
| CLI language layer (English source, Turkish catalog, `fm lang` / `FM_LANG`) | `cli/fm/Core/Labels.cs`, `Lang.cs`, `cli/fm/labels-turkish.tsv` |
| Game words, money and number format | `cli/fm/Core/GameWords.cs`, `MoneyFormat.cs`, `EngineTexts.cs` |
| Game folders and launch | `cli/fm/Core/GameFolders.cs`, `Launcher.cs` |
| MCP server | `cli/fm/Core/Mcp.cs`, `Mcp.Tools.cs` |
| Bulk dumps and scans | `tools/sigdump/`, `tools/sweep.sh`, `tools/buttons-catalog.py` |
| Bridge batching (many reads in one game frame) | `Core/Bridge.cs` `Batch`, plugin `HttpBridge.cs` |
| Radar drawing: flat styles, 3D scenes, rasterizer, ANSI cells | `cli/fm/Match/RadarStyles.cs`, `RadarScene.cs`, `Raster3D.cs`, `RadarCells.cs` — they take a frame of normalized positions + kit colours, nothing FM26-specific |
| Style catalog (Python prototypes of every look) | `tools/catalog/`, `tools/radar-catalog.py` |
| Live panel capture and GIFs | `tools/live-capture.py`, `tools/live-gif.py` |
| Sentence router, its model and test set | `cli/fm/Nl/`, `tests/nl/`, `tools/nl-*.py` |

Must be rebuilt against the new game: page GUIDs (`Match/Screens.cs`), data paths, event names,
roles, and any feature the game changed.

## Repo plan for FM27

- Stay in this repository.
- Move the FM26 tree to `archive/fm26/` with `git mv` (history is kept).
- Start FM27 code clean at the root; the archive is the reference.
- Review every copied part against the rules. Do not copy text-, timing- or order-based code blindly.
- Apple Silicon: the official BepInEx does not work. FM26 needed the patched package from
  DadMych/fm26-player-export-macos. If FM27 has none, patch it ourselves from the FM26 patch.

## First day with FM27

Work in this order; each answer changes the size of the job.

1. Unity version, IL2CPP, and whether `global-metadata.dat` is encrypted (FM26: Unity 6000.0.52f1,
   not encrypted).
2. Does BepInEx start on arm64? Is there a patched FM27 package?
3. Run Il2CppDumper. Do `FM.UI`, `FMGame`, `SI.Bindable`, `SI.UI` still exist?
4. Does `SI.Bindable.Bindings` + `BindingPath.Create(string)` still exist? If yes, most of the
   project ports.
5. Did the private fields the plugin uses change (`m_choices`, `m_key`, `m_selectionIndex`,
   `m_boundElements`… about 25)? They are in `plugin/FmCliProbe/src`.
6. Are the page GUIDs (`cli/fm/Match/Screens.cs`, 36) and event names (`OpenSubtabEvent`,
   `SpeakToAction`, `EventFmxClearChanges`…) the same? Compare from the dump.
7. Diff the data paths (`Human.Team…`, `Club.Teams`, `PlayerTraining`…) against the FM26 schema.
8. Is the translation table / `languages.fmf` the same format?
9. Run `tools/sweep.sh`, then the same commands in both languages, and list what broke.
10. Is there still a match simulation entity list (`Match2DSimulation` or its successor) with
    position, rotation, height and kit colour per man? If yes, the whole radar — all seven styles —
    ports by changing only `plugin/.../Radar.cs`.

Approach:

- Diff the static dumps first; rebuild only what broke.
- Identity-based commands should need little work.
- Text- and position-based ones must be rewritten.

Steps 5–7 in one pass, once the plugin answers in FM27 (game in English, between matches):

```bash
FM_BEP=~/fm27_bep python3 tools/identity-check.py catalog /tmp/fm27   # dump the new game's catalogs
python3 tools/identity-check.py check /tmp/fm27                       # what the code uses that is gone
```

- `docs/reference/identities.tsv` lists every identity the code uses (pages, events, element
  names, panels, plugin private fields, data paths, enum members, English words) and where.
- `check` prints the missing ones, most used first.
- Match-screen controls are not in any catalog ("unchecked"): play one `fm match` to cover them.
- After code changes, regenerate the list: `tools/identity-check.py uses`.

## FM27 static check (29 Oct 2026, Launch Preview 27.0.0.2486716)

From disk only, game not modded. Rerun after each FM27 update (full release 10 Nov):
`tools/metadump` (catalogs from `GameAssembly` + metadata) → `identity-check.py check OUTDIR`.

- Unity `6000.3.18f1-custom` (FM26: 6000.0.52f1). IL2CPP, metadata v39, not encrypted.
- Universal binary, arm64 slice present.
- The LibCpp2IL inside our BepInEx 6.0.0-be.755 reads it: 20,009 types. Good sign for BepInEx; not yet launched.
- `SI.Bindable.Bindings`, `BindingPath.Create` still exist → the core ports.
- Radar: `Match2DSimulation.m_data`, `MatchSimulationData`, `PlayerEntityData` (position, rotation,
  height, kit colours) all there → radar should port unchanged.
- Event and action ids keep their FM26 numbers. 208 new events, 14 gone.
- Plugin's compiled game references: 467, all present except:
  - [x] `SI.Bindable.DropdownRowNavigatable` → `SI.Bindable.DropdownRow`; `m_childDropdown` gone
    (`Controls/Submenus.cs`; `SIDropdown.m_childrenDropdowns` is a lead)
  - [ ] `SI.Bindable.TableRowNavigatable` gone (`Controls/TableRows.cs`, `Reads/Registration.cs`)
  - [ ] `BindableTextEditBox.m_textField` moved to `BindableEditBox<T>` (`Controls/TextBoxes.cs`)
  - [ ] `Bindings.GetDescendantCount` gone (`Bridge/FmApi.cs` status line only)
  - [ ] `IdentifierInfo.DisplayName` gone, `Name` remains (`Bridge/Schema.cs`)
- CLI events gone:
  - [ ] `EventFmxMatchFocusSelect*` (4, `Match/Prep.cs`); `EventFmxSelectMatchFocusType` remains
  - Also gone: `EventFmxMatchTacticsChanged`, `EventFmxToggleIndividualTrainingSelection`,
    `EventFmxSetPieceInstrPlayerMove`, `…PlayersTrainingUnit…` (4), MLS `SetGam/SetTamValue`.
- `languages.fmf`: same format (zstd + `ctl.` blocks).
  - [ ] Block names are now native ("Türkçe", "Deutsch", "Français", "Español"); `EngineTexts.BlockNames`
    expects "Turkish"… → engine texts silently skipped. New: "Bahasa Indonesia".
- Needs the running game: page GUIDs, data paths, button events, texts (`identity-check.py catalog`).

## FM27 BepInEx on Apple Silicon (29 Oct 2026, working)

Layout, Steam's folder untouched:

- `~/fm27_bep/game/`: `BepInEx/` (be.755 from the FM26 package), `run_bepinex_arm64.sh`
  (copy in `tools/fm27-bep/`), `libdoorstop.dylib`, `dotnet_arm64/`, `steam_appid.txt` (4530370).
- `~/fm27_bep/fm.app`: shadow bundle (LSUIElement, JIT entitlements, ad-hoc signed).
- `fm start` launches it: `FM26_BEP=~/fm27_bep run_bepinex_arm64.sh "<Steam>/fm.app"`.

What it took (each one blocked the next):

1. UnityDoorstop **v4.6.0** (NeighTools). The FM26 doorstop read UnityPlayer's chained fixups as
   zeros → no hook, the game booted vanilla, no `LogOutput.log`.
2. `dotnet_arm64/` = a copy of the .NET 10 runtime + Microsoft.Extensions.* 6.0.0 (NuGet). The
   FM26 package lacked them: `FileNotFoundException Microsoft.Extensions.Logging.Abstractions`.
3. Interop generation reads a **thin x86_64** GameAssembly. On the universal file Cpp2IL throws
   "Stream does not support writing"; on arm64 "Instruction set handler not registered".
4. `ScanMethodRefs = false` in `BepInEx.cfg`: the xref pass throws "Sequence contains no elements".
5. Running loads the **real** GameAssembly. The launcher swaps: interop missing or older than the
   game → x86_64 copy + generate (that launch then fails, start again); else real file +
   `UpdateInteropAssemblies = false`.

Live-verified on FM27: plugin loads, bridge on 7777, `health`, `state`, `panels`, `fm start`
(hidden, loads the save), `fm continue`, `fm squad`. Main menu = 200 nodes (> 150 holds).

The bridge's moment in the frame (30 Oct 2026, the cause of the "random" settings crashes):

- BepInEx's `MainThreadTick` fires from its `il2cpp_runtime_invoke` hook: any engine call into game code, UI repaint
  included. A click or value set there broke the page being drawn ("VisualElements cannot change their render data …
  during visual tree rendering"): lists that never filled, the game's own binding errors, then the game quitting.
- Now the bridge runs from each UI panel's scheduler (`visualTree.schedule.Execute(...).Every(0)`), once a frame; the
  tick only schedules new panels and stands in after 3 s with no panel tick (logged). `health` says `runs from: ui|tick`.
- `Application.onBeforeRender` and `PlayerLoop` are stripped in FM27 ("Method unstripping failed").
- One run a frame: a press while the UI event queue still drains is retried next frame (`HttpBridge.NotYet`, 5 s cap).
- Never schedule twice on one panel: copies piled up when frames ran long and the game slowed to a halt.
- FM's own log: `~/Library/Logs/Sports Interactive/Football Manager 27/Player.log` (exceptions with game stacks).

Plugin fixes in place: Bind takes an owner (`FmApi.Owner`; null throws), node count walk instead of
`GetDescendantCount`, `DropdownRow`, `OnClickedOrSubmitted`, no `DisplayName`, `ShowIDs` read-only.

Still open:

- [x] `fm status` / `inbox` / `reply`: rows found by `NewsItemHasBeenRead` (the path no longer names the list);
  rows pressed with `press?has=<title>` (the sender is now the row's first text); the open message read
  `within=MessagesToolBlockMessageContent`. Training report (`TrainingReport.cs`) uses `has=` too, not tried yet.
- [x] Table rows (`rowselect`, `checkrows`, `registration`): rows come from the table's own `m_boundElements`
  (row element → place in view; FM26 rows knew it themselves). It holds the view place, not the source's
  (squad table: Archie Brown row 9, source 5), so `RowControls` reads `GetItemFromView`. Registration's name
  text is `#primary-text`. Tried live: select, tick/untick by text and by table+index, registration read + toggle.
  - `fm register` opens only from the squad overview tile: its GUID is now a Squad submenu page (see Submenus).
- [x] Submenus: menu reads go through the rows' `ChildList` and still work; `m_childDropdown` was only a debug column.
  - Pages FM27 moved into a tab menu's submenu (Squad Registration, Captains, Board…): `/pageparent?guid=` names the
    parent; `OpenPage` opens it, then chooses on its bar. Registration from cold: 14 s.
  - Pages only on another page's bar: `Screen.OnBarOf` (Stages ← the league page's "Stages ›"). `fm stages`: 5 s.
  - Lazy submenus (`SIDropdown.HasLazyChildren`, `/lazymenus`): only `YourWorldDropdown` on 3 screens.
- [x] Page GUIDs gone in FM27: `/barpages?all=1` dumps every menu and bar with GUIDs.
  - News → Portal › News (`2d4ceb24…`); an item's fields now come before its title, age is `NewsItemAgeRoundedString`.
  - Training → the Training tab's Overview (`ceb0e533…`); the calendar is the month, read from where today falls.
  - Budgets → Club › Finances › Transfer Budget; figures from `Human.Club` data, scouting `Human.ScoutingInfo.ScoutingBudget`.
  - Youth Performance → the youth team's own bar › Individual Training (`/open?bar=1`: the Training tab lists it too).
  - Youth environment: now the youth team's Squad Dynamics; no staff counts, the level has no path.
  - Youth level: no path; the `#description` inside the `#youth-level` tile (`/texts?within=youth-level&named=description`).
  - Youth training: the team is `Human.Club.YouthTeam`, else `HighestLevelYouthTeam`; the table scrolls by `/scroll?anchorpath=`.
  - Youth rating: FM27 has none; dropped.
  - Youth Performance page is gone (FM26 fallback removed).
- [ ] `fm youth candidates`: the page (`e88d1df4…`) is still there, its rows not seen yet (intake each March); map them by data path.
  - FM27 data is in pounds, shown converted: `MoneyFormat.Rate` is learnt from `/money`; wrap data amounts in `Shown()`.
    Thousands are cut, millions rounded.
- [x] A tab's own menu is its bar (Training › Coach Assignments); `Subpage` tries real bars first.
- [x] First-visit tab intros: `/closeintro` presses `#EndTutorialButton`; `OpenPage` calls it. A chosen page does not open under one.
- [ ] `fm budget adjust`: the tile sends a `GenericAction` (ActionID `ClubBudgetAdjustmentTransfers`, 894198379);
  the plugin puts its `m_actionIdBinding` at the button's `Sends`. FM26's id (1885623898) is now in the board requests menu.
  - The card is read by data paths (`budget.BudgetAdjustmentEdited{Transfer,Other}Value`, `…BoardString`).
  - Its buttons need `/press` (a click does nothing); the card is read back as gone.
  - Before the season expectations meeting the slider is shut: the board's sentence is said and the card closed.
  - Tried: TR only, refusal 6/6 (page open, card open, from Squad and Inbox). Moving the slider and confirming: not yet.
- [ ] Set-piece creator (FM27, first season): the set-piece intro message's only answer opens a 6-step wizard; the game
  waits on it (`NeedAction`, `Respond` 14) and Continue does nothing.
  - Each step has the staff's choice ready; `EventToPlugin` on `buttons-next-primary-default`
    (`EventFmxSetPieceCreatorNextStep`) goes on, 6 presses end it (`Respond` 0). Back: `…CreatorBackStep`.
  - Done once by hand (TR). `setpieces new` (six questions) may be this wizard: not tried on it yet; `next` should name it.
- [x] New continue stop `Messages` (new mail, nothing to answer): one press goes on; `next` passes it.
  - `AroundTheWorld` (world news page): one press goes on, seen once (TR).
  FM27 has more page stops (`NextUnreadMessage`, `Training`, `NewsForYou`, `DataHub`…): not seen yet.
- [x] FM27's match-day screens before kick-off (`SubmitTeam`, `Tactics`) bind no game date: `InGame` took them for
  "no save" (the node count is capped at 1000, so only the date decided). Match-day stops now count as a loaded save.
- [x] Squad short on match day (players on holiday after a tournament): `tactic pick --youth` sends
  `EventFmxQuickPickYoungsters` (the Quick Pick menu's youth row carries only that number) to the Quick Pick
  button's target, via `/pluginevent`. The target is resolved only once Quick Pick has fired, so it is pressed first.
  TR, cold and warm, 4/4: 11 picked; plain Quick Pick still 8. The match-day squad question offers it too.
  - `tactic player <slot>` without a name still lists nobody.
- [x] First live match (Viborg 2-0, TR): speed keys + + - went 2x → 2.5x → 3x → 2.5x, `settings` read 2.5x after.
  `fixtures` shows the played result.
- [x] Live stats line: `NextFixture` and the live `MatchReference` keep their `Home*`/`Away*` numbers at 0 until the whistle.
  The root `Match` binding's `Stats` answers live, per side by the `isHomeTeam` variable beside it (`/contexts` names it).
  The first pull of a new path is empty for a frame. Tried TR, one match, panel closed: last live line = `report`.
- [x] The bridge's main-menu Continue is `/loadlast` (was `/continue`) and refuses once a save is loaded:
  called in game it reloaded the last save over five days of play. In-game Continue is `/next`.
- [x] Board meeting on expectations and promises (FM27, first season): the message's answer opens a talk;
  `talk "<part of a line>"` goes through it (targets, then promises, then "Görüşmeyi Sonlandır"). Options change
  between visits, so parts of lines are safer than numbers. Every promise offered got 17 negative reactions here.
- [x] Inbox titles came back in English when they are translation ids ("Fitness tests", "Regular News Round-up" in a
  Turkish game): the title is now read as shown (`Text.ShownOf`). TR 3/3; EN, DE not tried.
- [x] `InGame`: the in-game Continue's flow (`continue.State`) is bound in a save on every screen and not on the
  main menu (measured once, cold start), so it replaces the list of match-day stops (`SquadNumbers` was missing).
- [ ] Squad numbers before a match: the screen stays up a moment after the numbers are sent and `match` sent them
  again into a shut button ("is disabled"). Now waits for the screen to change: not tried yet.
- [x] NeedAction with nothing to answer (`Respond` 0, no priority message): FM27 wants a message read (a forwarded
  match story, squad selection advice; not always in the inbox's first window). One Continue opens it; `next` presses,
  says "opened: <title>" and goes on, also through two in a row.
  - `Respond` tells the stops apart: 0 read, 1 answer a message, 3 an offer, 14 set-piece wizard.
    `Game.ImmediateUserRequiredTask.HasTask` is false in all of them: not a sign.
  - A message wanting an answer (pitch size, registration, squad numbers) is still left to `reply`.
  - Tried: TR, 6 read stops (two in a row twice), 3 answer stops; DE 2 in a row. EN not met (the stop is rare: ~1 in 15 steps).
- [x] `fm offers`: read from the club's own records, no page opened: `Human.Club.TransferActivity.TransferActivityPlayerListAll`
  › each player's `TransferOffers` (`UniqueId`, `BuyingClub`/`SellingClub`, `TransferOfferTypeString`, `TransferOfferStatusString`,
  fee, clause and option strings, `CanAcceptOffer` / `CanStallOffer` / `CanRejectCancelWithdrawOffer`).
  - To us: `SellingClub.UniqueId` is `Human.Club`'s. Words in the game's language (`shown=`); fee lines lose their `<b>`.
  - Answers: the row's buttons by the event they send (`EventFmxTransferCentreResponse{Accept,Stall,CancelReject}`), their row
    by its table item's `UniqueId` (`/rowcontrols?buttons=1`); view: `transfer-link-button` the same way. When the offer's player
    is not chosen on the page, each player in its list is tried until the row is up.
  - FM26's reading (club `Club.Name`, buttons by place after the row) is gone: FM27's club text is `primarytext`.
  - Tried: TR list / view / reject / stall / stall refused / unknown; EN + DE list (3/3), view, stall refused; cold and page open.
  - Right after a language switch the first read is in the old language; the game's own texts change on restart.
  - `offers <name> <answer>` went to the language layer: a text argument beside a choice was not seen (`Mcp.TakesText`).
- [x] Counter-offer on an offer for our player: `offer "<player> [club]" <fee|--term …> --yes [--negotiate]` opens its card
  (as `offers view`), changes the terms, sends them back. The director's answers all send `Event` 32
  (`EventFmxTransferOfferResponse`); they differ by `ActionResponseValue`: `ngap` accept, `ngrj` reject, `ngts` Suggest Terms,
  `ngng` Negotiate Offer. The two sends appear only once a term is changed.
  - The send used to be found by the label "Make Offer": in Turkish that matched "Accept Offer" and **accepted** Brugge's
    offer (save reloaded). Their offer is now answered only by these codes; read back from its record.
  - Tried TR: unchanged card refused 3/3; Suggest Terms (Brugge withdrew at once); Negotiate (Palermo "Görüşüldü", then
    withdrew 1 day later); an offer waiting on their answer refused. EN/DE not tried: no offers left.
- [ ] `fm offer` for another club's player: the player card's `Value` is empty in FM27, so his actions do not open.
  Our own bid still finds "Make Offer" by its label: give it its `ActionResponseValue` once the screen opens.
- [ ] `fm load "<save>"` from a loaded save: "save list did not open" (the game closed and reopened); `fm continue` loaded it.
- [x] `next` listed old messages as new: the inbox binds a window of its list that grows as it is scrolled
  (14 rows, then 41). New now means unseen and a newer id than any held before. TR + EN + DE, 4–5 steps each.
- [x] Inbox read cut short (19 of 41, the oldest) when another page was bound beside it: the walk's node limit.
  The list a row sits in is now read whole.
- [x] `reply "<title>"` failed on titles that are translation ids ("Maç sonu basın toplantısı"): `press?has=`
  compared the row's text in English. It now matches the text as shown or in English. TR + EN + DE, 3/3 each.
- [x] `save` on match-day screens (no game date bound) says "today" instead of "()".
- [x] `dugout uygula` / `dugout 2 kaldır` went to the language layer (a command word that is not a number made it a
  sentence); a command's own words (`CommandFlags`) now keep it a command.
- [x] `settings sound` (`settings.AudioMasterEnabled`): turned off, read back False. Not during a match.
- [x] `fm fixtures`: the home page binds no fixtures in FM27. Now read from `Human.Team.FixtureList`, no page opened.
  - The list asks `previousamount`, `futureamount`, `fixturepadding`, `yearindex` beside the team (3 / 7 / 0 / 0).
  - Venue, result mark and opponent ask `team` and `human` beside each row; venue, opponent, competition in game words.
  - Tried TR + EN + DE, from three pages. Played results (`HasResult`, `ResultLinkableNoSpaces`) not seen yet: no match played.
  - Friendlies keep the name they got when made ("Hazırlık" in an English game): the game's own page shows it so too.
- [x] `fm training`: `PlayerTraining` now asks for `human` and `team` beside the player rows (its parts still a `Judge`).
  - The page shows one player's lists beside the table. His row is chosen by `UniqueId` (`/checkrows`, other rows unticked:
    the table keeps several and the lists are the first's); the lists by the event they send, whose they are by the
    page's `person` (`/rowcontrols?var=person`, read from the scope the list writes into).
  - The assistant looks after individual training by default: the lists are shut, choosing on one brings up dialog
    `TrainingControl`. `--control player|month|all` answers it by `EventID`; without it the change is refused.
  - An answer can lead to the player's own page, which has the same table and lists: only the training page's are taken.
  - After a change every other player is read again; a change that reached someone else is said.
  - Tried: TR only (cold start, page open, other page open), 9/9 + 5/5 in a row, all three parts, submenus.
- [x] `fm intensity <player> <level>`: the list is not the assistant's; it is shut when `TrainingIntensityOrRest.CanEdit`
  is false, which matches `AvailableForTraining` (injured, on leave after a tournament). Refused before the page is opened.
  - Tried: TR only; 3/3 change and back, rest, "already", 2 refusals; from Squad and Inbox.
- [x] `fm budget`: "spent this season" is `Human.Club.TransferExpenditureThisSeason`.
- [x] `fm stages`: a table row's team is now `LeagueTableName`, its short name `AbbreviatedName` (were `Name`, `ShortName`).
  - Tried: TR, Süper Lig / 1. Lig / Premier League, 3/3 same, JSON. The settled mark ("C", "Pl") not seen yet (pre-season).
- [x] `fm table`: the competition is held by its data path (`Human.Team.Division`, `CurrentCompN`), never its name.
  - `/select?record=<path>` chooses the menu row whose `RowData.PropertyValue` is that database record (`CombinedIndexAndType`);
    "already chosen" when the list's binding or the card's `comp` holds it (a fresh page has chosen nothing).
  - Stages lists by what they write: competition `pairData` inside `BindingExpect_StagesBlock`, stage `selectedstagemenu`.
  - A stage row's kind is its `RowData.Stage.StageType` (put with `/rowput`): league 2, xG table 4, knockout round 0
    (seen, not from an enum). The table is looked for only in rows that are not 0 or 4.
  - The portal tile is used only when the next fixture's competition has the same `UniqueId`.
  - Numbers (`DynamicNumber`) are no longer taken back through the translation table: German "6" came back "F".
  - Tried: TR, EN, DE; cold / page open / other page / after another competition; 10/10 + 11/11; cup and UCL refused.
  - Not yet: a league over (play-offs shown), UCL league phase. `stages <name>` still breaks ties by `Men's`/`Under` text.
- [x] `fm happiness`: read from each player's data, with `human`/`team`/`Judge` beside the rows (as training):
  `HierarchyOverallHappinessLevel`, `MoraleString`, `HierarchyInfluenceCategoryString`, reason lists
  `HappinessPositives`/`HappinessNegatives` (each reason's `Name`). No row selecting, no "Negatives" heading.
  - The level's word has no path: `/rowtexts?has=Person.PlayerDescription` gives each laid-out row's texts and its
    table item (→ `UniqueId`); the level is the row's one text bound to nothing. Turkish "Biraz Mutlu" is not in the table.
  - Tried: TR, EN, DE; page open / other page / cold; 3/3 same each; one player, `--why`, unknown name.
- [x] `fm responsibilities`: read from the page's edit session (`TeamSettingsEditSession`), not the page's layout.
  - The session is what the page's Confirm (`EventFmxResponsibilitySettingsChangeConfirm`) is sent to: buttons
    (`gen=`) now give `Target`, the value their `m_targetHandler` names.
  - `ResponsibilityTabList` › `ResponsibilityGroupList` › `ResponsibilityList`: `IsUserInControl`, `CanHumanTakeControl`,
    `CanHumanChange`, `StaffMemberInCharge.Name` (dotted props work in `/pull`). All 79 duties in ~2 s, no scrolling.
  - Names come raw: `/pull?shown=<props>` keeps them in the game's words (English → game words chose "Übersicht" for "Training").
  - Take/delegate: `EventFmxResponsibilityTakeOrDelegateControl` to the duty; give: its `StaffMemberInCharge` set from
    `AssignableStaffList`; then Confirm, read back. A failed change is reverted (`EventFmxRevert`).
  - The duty's tab must be up (radio group, checked by `SelectedResponsibilityTab.ResponsibilityTabID`): otherwise the
    change reaches the counts but not the duty's record.
  - A duty has no id: held by tab id, group id + team type, and its title within one command.
  - Tried: TR, EN, DE; cold / other page / page open; take+delegate 3/3, give and back, locked, unknown, ambiguous.
- [x] `fm settings`: FM27's FM menu is a list of row keys (`FMMenuOptionQuantiseAndMap`); `/menuoptions` maps each to
  its `MenuOptionID` (Preferences…) and page GUID, `/menuoption?option=Preferences` chooses it with the list's own events.
  - Each control's preference by the game's key: the `Name` beside it (`current_language`) or `settings.X` (`/form?page=`).
  - Tabs by element name: overview `navigation-tab-default-default`, then `AllPreferencesTabButton` › `DetailedPreferencesTabButton`;
    each clicked once, when drawn and `Processing` is false; after Confirm the page is opened once, not again over itself.
  - Tried: TR, EN, DE (with switches between); cold / other page / page open; 20/20 change + read back; bad value, unknown name.
- [ ] `/translate`, `EngineTexts` block names, `EventFmxMatchFocusSelect*`.
- [ ] `identity-check.py catalog` (game must be in English for the word list).

New in FM27 (from the new events):

- Fantasy Draft / Shuffle Draft mode, cup draw screen, Steam Workshop.
- Tactical Tweaks (apply / undo / reset), tactic Data Visualisations pinning.
- Training: week editing from a card, copy/paste week, custom schedules, assistant individual training.
- Set pieces: edit sessions with undo, captain stand-in candidates, penalty takers quick pick, foot preference.
- Recruitment Focus rebuilt (stats, objectives, conditions), Scouting Centre acknowledge/discard.
- Match: replays skip/toggle, speed change event, touchline talk cancel, post-match reactions.
- Kit editor (shirt/shorts/socks styles), store items, favourite players, "forced screen" onboarding.

## FM26 traps that may return

Game behaviour:

- The game reports success and does nothing. Read the result back.
- An unbound field returns `{}`; "no path" and "not ready yet" look the same.
- Scrolling fills table rows in steps; wait for two equal reads in a row.
- Some actions (mutual termination) apply without asking. Re-read a list before answering by number.
- The game's auto-save overwrites the main save; "quit without saving" loses progress.
- Deleting and recreating a mentoring group on the same day mixes the groups up.
- `IsInjured` is not "cannot play": Quick Pick started a man with a knock. Say he may play injured.
- An injured man on the bench stops the match from starting ("… bu maçta kullanılamaz").
  - Each selection slot has an "All Players" submenu.
  - `pickable?slot=N` lists who it offers; `pickplayer` puts one there.
- Once (Sampdoria away) the team talk's `MatchContinue` did nothing; only a restart cleared it.

Things that lock or break the game:

- Mutating the game's `TypedValue`s in place (they come from a pool).
- Clicking a disabled button.
- Walking the whole tree in a polling loop (~40,000 nodes).
- Opening a page from the post-match `Results` screen (breaks Continue).
- Opening Match Day › Overview (the game stops drawing).
- Mods that force a resolution (FM26DisplayFix): pages draw empty.

Our side:

- The plugin DLL loads only at game start (~1.5 min). Never copy it while the game runs.
- Use `127.0.0.1`, not `localhost` ("Not Found").
- On macOS put `pkill` flags before the pattern; it once killed the wrong processes.
- `activate` puts the game window over the user's work. Hide it again after.
- Publishing `fm` over the file a running `fm mcp` uses breaks that process. `tools/publish.sh`
  publishes to `dist/fm.next` and renames into place.
- `Report.Json` and `find` go through the English table. Turkish stock commentary came out in
  English. Text only shown to the user skips it (`Report.Json(fields, ["Commentary"])`).

Match speed and staff advice:

- Match speed is two sliders in the match screen's settings panel, not a dropdown.
  - Open the button inside the template's `#settings` block; close with `PanelHistoryClose`.
  - A change holds at once.
  - Reopened a few seconds later, the slider still shows the old speed. Count from the last speed set.
- At 10x highlights the radar jumps ~3 game seconds per redraw; at 1x it is smooth.
- Staff suggestions on the pre-match team news: `DoItDugoutEvent`, text at
  `content.Description` / `content.Text`. `DuringMatch` is already true there.

## Fragility review (FM26)

What can break when the language, theme, terminal, machine or game version changes.

**English labels in the match flow**

- `Confirm Changes`, `Continue` (tour), `Confirm` (set-piece wizard) and `Very Attacking` are matched by text.
- They work while the plugin's English table is complete.
- When porting, prefer event ids (`EventFmxConfirmChanges`) and element names.

**Live panel**

- ANSI colours and `Console.KeyAvailable`: POSIX terminal, macOS.
- Width adapts from 56 to 86 columns; narrower wraps.
- Keys are off when input is redirected, so scripts and MCP stay plain.
- Each bridge request waits for a game frame. Several reads per redraw froze the panel (8 s).
- One `batch` request per redraw: 0.8 s, radar ~0.3 s. Batch from day one in FM27.
- The panel draws over itself (`ESC[H` + `ESC[K`, synchronized output) so it does not flicker.

**Radar**

- Read from `FM.Match.Simulation.Match2DSimulation.m_data`; fallback: binding tree,
  `MatchSimulationData.GetInstance`. The type moved between versions; re-check `FM.Match.dll`.
- Positions are normalized to the pitch. No team id in the data.
- Sides go by kit, not list position (`RadarTeams`). The keeper's kit differs from his side's.
- Read in the background every game frame (~33 ms, 30 redraws a second, fm ~36% CPU).
- `RadarMotion` moves men between readings, matched by entity index; a jump over a quarter
  pitch is a cut or a substitute.
- `FM_RADAR_LOG=<file>` keeps the readings.

**Radar styles and terminals**

- Anywhere: ASCII, coloured ASCII, braille (256 colours; `Ansi.Apart` keeps close greens apart).
- 24-bit colour: pixel and the 3D iso / raycast / voxel scenes.
- Image protocols (Kitty, OSC 1337): not done.
- Terminal.app on macOS 14 has no 24-bit colour.
- 3D scenes are pure C#, 30–90 ms a frame at 120–200 columns.
- Judge looks in a real terminal, not on a PNG.
- A low camera must skip men right in front of it.

**Other**

- Key moments: Turkish and English marker words only; other languages show full commentary
  (`MatchLive.KeyWords`).
- Live stats: no live possession or shots during a match; the post-match report has them.
- `Labels.Source` maps Turkish back to English; duplicate Turkish values can map wrong. Keep new
  labels unique.
- MCP output follows the CLI language. Use `FM_LANG=english` when a model needs English. Write
  tools are not tested with the Python SDK.
- Install and launch: macOS arm64 + Steam only. `fm start` minimising needs Accessibility.
- Page GUIDs, data paths and event names are FM26-specific.

## State at the FM26 stop

- Working: 100+ commands and MCP tools; one full season played end to end.
- Sentences in place of commands (Turkish and English), with a local model.
- Unfinished: parts of match narration and a few screens still depend on text, timing or order.
  `tools/sweep.sh` shows what breaks.
- Tested only on macOS arm64 + Steam.

## FM27 plan

1. Identity-based from day one.
2. Test every command in Turkish and English from day one.
3. Keep a second save from day one: another country, league, career.
4. No fixed waits. Only `Until(signal, upperBound)`.
5. Install and release early: one place for paths, a version guard, `fm doctor`.
6. Readable, quick output before more commands.
7. The sentence layer ports as it is: it depends on the tool definitions, not the game.
