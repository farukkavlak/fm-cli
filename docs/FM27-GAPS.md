# FM27: what the tool cannot do yet

From `fm crawl` (TR + EN, Pendikspor, Mar 2028: `docs/reference/fm27/crawl-*.json`), the match screens, and
the enum diff against FM26 (`docs/reference/fm27/new-in-fm27.txt`). An event the code never names is not
always a gap: some commands reach the same thing by data path or row controls. Each line below was checked
against the commands.

## New in FM27, no command

- [ ] **Tactical tweaks in a match** (`docs/reference/fm27/tweaks.txt`): 15 team tweaks plus an Individual tab,
  in the match tactics panel's `#tweaks-button` popup; each `#tweak-row` has `#toggle-overlay`, states
  Instruct / Instructing / Stop / Stopping; Reset sends `ResetTacticalTweaks`. Enum `FM.UI.TacticalTweak`.
- [ ] **TransferRoom** (In, Out, Agent Offers): adverts by type and view
  (`EventFmxTransferRoomChangeTransferType`, `…ChangeView`, `…SelectAdvert`), agents' offered players
  (`EventFmxDiscardPlayerFromAgentsOfferedList`, `…DiscardAll…`).
- [ ] **Squad Planner** (planner, contracts, current best XI): `EventFmxSquadPlannerAddPlayer`, `…MovePlayer`,
  `…RemovePlayer`, `…AskAssistant`, `…Copy`.
- [ ] **Personalised roles** on the tactic: `EventFmxTogglePositionPersonalisedRole`, `…Enable…`, `…Disable…`.
- [ ] **Swap two players** on the tactic: `EventFmxSwapPlayers`; clear a slot: `EventFmxTeamSelectionClearPlayer`.
- [ ] **Tactics advice options** (`AdviceOptionsSelection`) on the tactics page.
- [ ] **Training week card**: copy / paste / reset a week (`EventFmxCopyTrainingWeek`, `…Paste…`, `…Reset…`),
  pick a session (`EventFmxSelectTrainingSession`), confirm or cancel the card (`…CommitTrainingWeekFromCard`,
  `…ConfirmTrainingSessionFromCard`, `…CancelTrainingWeekEditFromCard`). `schedule set/style` change one session.
- [ ] **Analysis pages** (read only): Data Hub, Tactical Effectiveness, Tactics Feedback, Team Analysis,
  Formation / Goal / Set Piece Analysis.
- [ ] **Recruitment Objectives** and **Recruitment Feedback** pages.

## Existing commands, missing a part

- [ ] `captains`: Quick Pick (`EventFmxCaptainQuickPick`), stand-ins (`EventFmxAddCandidatesToCaptainStandin`).
- [ ] `responsibilities`: delegate all to recommended / take control of all (`EventFmxResponsibilityGroup…`),
  undo / redo (`EventFmxUndo`, `EventFmxRedo`).
- [ ] `coaches`: remove all assignments (`EventFmxClearCoaches`).
- [ ] Opposition scouting priorities: prioritise / cancel (`EventFmxPrioritiseScoutingTeam`, `…CancelScoutingTeam`).
- [ ] `search` / player database: quick filters (`EventFmxFilterSelectQuickFilter`), scouted only
  (`EventFmxFilterSetScoutedOnly`); `staff find`: exclude / own staff filters.
- [ ] `shortlist`: more than one shortlist (`EventFmxSelectShortlist`).
- [ ] `inbox`: mark all read (`EventFmxMarkAllHumanMessagesAsRead`); `news`: mark read (`MarkMediaNewsAsRead`).
- [ ] `dugout`: next / previous / dismiss advice (`EventFmxMatchDugout…`).
- [ ] `match`: skip replay (`MatchSkipReplay`), pause (`MatchTogglePlayPause`).
- [ ] `holiday`: the "apply for jobs" choice (`holiday.applyforjobsdropdownselectedoption`).

## Pages no command reads (read only, lower priority)

- [ ] Club history: overview, records, honours, landmarks, manager history, all-time best XI.
- [ ] Club profile: affiliates, facts, stats, club news.
- [ ] Career: my profile, my history, relationships, managerial stats.
- [ ] Competition: past winners, award winners, records, rules, structure, qualification places, club
  coefficients, nation agreements, rankings, season preview, suspensions, injury table, manager movements,
  transfer rumours, transfer window report.
- [ ] Transfers: transfer history, transfer window, clauses; scouting: reports, priorities, range, not interested.
- [ ] Squad: discipline, loans, promises, squad feedback; medical centre: risk assessment, recently returned,
  season summary, player summary.

## Found while crawling, to fix

- [ ] `fm crawl`: two panels leave a popup that holds the game's event queue (`--skip 7bab4b19…,cd095383…`).
- [ ] Calendar and training week still read the venue from `MatchVenueShort` (shown text).
- [ ] `fm stop`: the game once hung on its way out (process stuck exiting, not killable); cause unknown.
- [ ] `tactic new` once said "three slots taken" with the wizard already up; not reproduced.
