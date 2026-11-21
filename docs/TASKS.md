# Tasks

One list. Pick by id ("T12"). Tick a box in the commit that does it.
Done = every box ticked; acceptance tests (section 11) last. New work is added here first.
Only open work is listed; what was done, and how it was measured, is in git. Port notes: `PORTING.md`. Crawl data: `reference/fm27/`.

## 7. Platform (macOS only today)

- [ ] T49 `fm start` / `stop` on Windows (today: shell script, app bundle, PlistBuddy)
- [ ] T50 Window hiding on Windows (today: osascript)
- [ ] T52 Publishing for other targets (today: `osx-arm64` only)
- [ ] T53 Plugin on Windows x64 (untried)
- [ ] T54 Save and user folders on Windows (untried)

## 7b. FM 27.1.0 (Steam update, 22 Nov)

- [x] T172 Plugin on 27.1: match period from `m_matchStatePeriod` only; planner style from `TacticalStyleDropdown.DisplayValue` (was an id + options); span route dropped. Identity check against the 27.0 catalog: only that path changed. Sweep 63/63 TR and EN, style set/read 3/3 both, instant match EN, live match TR to full time; released v0.9.2

## 8. Moments still to meet (catch them when they come: `fm crawl out.json --here`)

- [ ] T160 `dugout` mentality card ("Set team mentality"): read back from the match tactic's `Mentality`; see one live
  - Not met in 12 hunt matches (19 Nov), starting attacking or very defensive. A debug event sent to the dugout made no card
- [ ] T171 `dugout apply` cards read back by what they change
  - Opposition instruction card: read back by the man's UniqueId (his Pressing 0 → 2), live TR 1/3. Left: 2 more, and EN
  - A card that takes a team instruction off: not met yet
- [ ] T158 The game's own fatal-error window: the tool says so (exit 70) and `fm stop` ends it
  - Seen once 22 Nov (TR; BepInEx's copy of the game was from before Steam's 27.1 update minutes earlier): `status` said so, `fm stop --no-save` ended it; EN still to see
  - Sign: the main thread in `-[NSAlert runModal]`. Its only known trigger went with T92's fix
- [ ] T14 `tactic advice`: the game always says "no advice" (4 saves, both phases). Needs a moment with a critique
  - 19 Nov: goalkeeper up front and a striker in goal, a day on: still "no advice" (the game's own panel says so too)
- [x] T60 Board meeting and requests; job offers while employed; being sacked
  - Job offer while employed: TR 1/1 (20 Nov, Samsunspor: invite → interview → offer → refused); EN 1/1 (Başakşehir: apply, repeat refused, interview, offer, own board's keep-you meeting → budget raised, offer refused; scenarios `employed-interview`, `employed-offer`); unasked invite EN (Fenerbahçe, declined; scenario `job-invite`)
  - Being sacked: TR 1/1 (20 Nov, Antalyaspor, Jan 2036; scenario `sacked`), then interview and new job (scenario `new-job`)
    - EN from `sacked`: unemployed status, jobs, apply / interest (and their repeats), interview; offer → contract → new club from `job-offer`. Sacking itself in EN 1/1 (scenario `before-sack`, 21 Jan → 28 Jan 2036): forward stops on it, unemployed status, jobs
- [ ] T61 Contract renewals: a player asking, an unhappy player's demand
  - [ ] A player asking for a new contract: not met yet
- [ ] T63 Match: substitution screen, shouts, set pieces edited in a match
  - Left: a shootout in a TR match (its board's score)
- [ ] T98 `talk`: a journalist pressing with the same answers. Not met again yet
  - 19 Nov: evasive answers at a post-match conference (TR): the journalist moved on, no pressing
  - 21 Nov (EN, MCP): signing unveiling, a neutral answer was pressed ("The public will want to hear a proper answer") with the same five lines; shown and answered fine. TR still to see
- [ ] T154 TransferRoom `responses` and `activity --loan` tables: need real responses to our adverts

## 10. Quality

- [ ] T167 Game froze once for 6+ minutes: main thread inside the .NET JIT, compiling plugin code; not reproduced
- [ ] T168 `read club-history` gave its table as loose lines in 1 of 10 sweeps; not reproduced
- [ ] T169 Each resolution change takes the game longer to recover (about 3 minutes by the 16th)

## 11. Acceptance tests (each in one go, tool only, no bridge)

- [x] T78 Two full seasons through the CLI, Turkish game
  - 2034/35 and 2035/36 (sacked in Jan, new club, stayed up); season-end vision, team meeting and preseason date through the tool; scenario `season-end`
- [x] T79 Two full seasons through MCP, English game
  - Still to see live (fixed, unit-tested where it can be): the two `(fixed, to see live)` lines below
  - 2035/36 from 20 Mar (Antalyaspor, stayed up); season-end messages, team meeting, vision through MCP; scenario `t79-s1-end`
  - 2036/37: summer window (4 signings), registration, numbers, captain, camp, bonuses; manager renewal through `fm_board_request` + `fm_contract` (Feb 2037, to 2040); scenario `t79-mgr-renewed`
  - 2036/37 done (14th, stayed up); player renewal through the agent (`fm_contract agent=true` + talk); season-end camp/date ("1 2"), team meeting, vision; scenario `t79-s2-end`
  - [x] `fm_reply` on a list answer ("1 2"): MCP passed it as one word; now `answer="1 2"` or `option`
  - [x] `fm_next` while the game is busy: waits for it (seen 1 Jun 2038 with a save running)
  - [x] `fm_table` on the Messages screen also said "a cup has no table": now only the screen's reason (MCP and CLI)
  - [x] `fm_inbox` takes `all`; its hint reads `fm_inbox all=true`
  - [x] MCP hints in CLI words: `fm_targets` takes grade/listed/loan_listed/attributes, `fm_register` hint split into commands (both seen through MCP)
  - [x] `fm_status` "squad --fitness" now `fm_squad view="fitness"` (seen with two injured)
  - [x] `fm_contract` counter-offer hint now `fm_contract wage=<amount> · fm_contract terms=...` (seen in the manager's renewal)
  - [x] `docs/MANUAL.md` contract-answer image: the agent's counter with the new bargaining hint
  - [x] (seen fixed 14 Feb 2037 replay: "Job: Must be Manager", "The board wants"; a player's counter still "The agent wants") Manager's own renewal card: `Job: — (Must be Manager)` and "The agent wants" for the board's counter
  - [x] (seen fixed: Troncoso "is retiring: these talks are for a job…"; Ömürcan's talks without it) `fm_contract name=<retiring player>` opens a staff job card for after his retirement (Job: Scout, start "Retirement From Playing") without saying it is not a player contract
  - [x] (seen fixed 2 Jun 2038: `fm_offer name="Noah Kalonji #…"`) `fm_offer` on a shared name lists the ids but its hint names `fm_player name="... #id"`, not `fm_offer`
  - [x] (seen fixed 1 Jun 2038) `fm_reply` "Attend Meeting" on the board's future meeting opens a talk but does not say so (other answers print "A conversation opened ... fm_talk")
  - [ ] (play path seen fixed 16 May 2038: report after the full-time talk; instant fallback still to see, now told by the fixture's day, unit-tested; normal path seen 3/3 21 Nov, EN 1 TR 2) `fm_match mode=instant` sometimes says "Match report is not ready yet; the game's last fixture has not changed" though the match was played (fixtures shows the score); 2 of ~100 instant matches (19 Dec 2037 sacking day, 21 Mar 2038 AZ) and once after `mode=play` (9 May 2038: "the game's last fixture is still Sparta Rotterdam 0-1 FC Volendam" — the match just played): the "last fixture before" is read after the result has already landed, so the wait for a change never ends
  - [x] (seen fixed: offers 7 Jun 2038, set pieces `fm_setpieces_new answers=<no>` 25 Dec 2037 replay) More MCP hints in CLI words: `fm_setpieces_new` "setpieces new <no>", `fm_offers` view "reply <#> Reject | Accept" and its on-screen options as "fm_talk say=<no>"
  - [x] (seen fixed 20 Dec 2037 replay) "Attend Interview" (job interview) opens a talk without saying so, like "Attend Meeting"
  - [x] (seen fixed 25 Dec 2037 replay: "The set piece wizard opened … fm_setpieces_new") New club's Set Pieces Introduction: Begin/Continue left it waiting ("still awaiting") until set pieces were created with `fm_setpieces_new`; the tool could say that is what it waits for
  - 2037/38 done: Sparta 16th, play-off lost on penalties, relegated; board kept the manager; scenario `t79-s3-end`
  - 2037/38: renewals of 6 through `fm_contract` (4 straight, 2 via agent), 7 signings, registration add after the window, sacked 19 Dec (ultimatum 6 pts in 5 not met), job applications, Sparta Rotterdam interview + contract, all intro tours, tactic wizard, set piece wizard; scenario `t79-sacked`
  - [x] `fm_targets` wage budget says it is weekly
  - [x] `propose`/`actions` on a one-word name of ours pushed out of the game's search by famous people (Fábio): taken from our squad, menu from the squad page (seen; Veerman and an unknown name still right)
  - [x] `fm_reply` while another conversation is open: the answer did not reach the game yet it said "Answered … A conversation opened" (4 of 5 stayed waiting, 3 Jun 2038); now refused with a word to finish the talk first (seen: refused, then answered once the talk was over)
  - [x] (seen fixed 23 Jun 2038: `fm_talk say="fee <value>"`, used with "fee 5M" then confirm) talk fee window hint `talk fee <value>` → quoted so MCP reads `fm_talk say="fee <value>"`
  - [ ] (fixed, to see live) `next` stop on our own bid accepted ("<club> accept bids for <player>") says "Transfer offer waiting … reject: fm_offers …", the incoming-offer hint
  - [x] (seen fixed 21 Nov, EN 3/3, TR 3/3) `contract` with transfer talks left for another page said `contract <player>`, which cannot reach another club's player; now it says the talks reopen from their message and names the one waiting
  - [x] Training camp squad: the game sends a second message on the camp's first day; the first stays answered (seen twice, 2036 and 2037)
- [ ] T80 A third language: one month through the CLI, sweep passes
- [ ] T81 Windows (Steam): install from the README, one month, sweep, stop
- [ ] T82 Epic Games Store (macOS and Windows): install, one month, stop
- [ ] T83 A clean machine, README only
  - 21 Nov: package installed into an empty home from a zip marked as downloaded: fm was refused (quarantine) until install.sh cleared the mark; now it does, and the README says `zsh install.sh`, PATH, a career saved first, MCP setup
  - [x] Release: repository public; v0.9.0 with the zip and `nl-mpnet-l-q` (21 Nov: the zip downloaded without login, installed into an empty home, model downloaded)
  - [x] One-line install (`curl … | zsh`) and Homebrew (`brew tap` + `brew trust` + `brew install`, then `fm doctor --install`): both from GitHub into an empty home, 21 Nov (v0.9.1)
  - [x] This user cleaned (dev folders renamed aside): `curl … | zsh`, first `fm start`, `fm status` (22 Nov). Found and fixed: no `steam_appid.txt` in the package (the launcher now reads the id from Steam's app manifest), Steam closed (now said up front), `fm` aborting when the game broke off a reply, "game updated" said on a first start
  - [ ] A real second Mac: download, install, first `fm start`
- [ ] T84 A new career (another country and league), one month, sweep

## 12. Final check (the last box)

- [ ] T85 Audit `cli/fm` and the plugin: nothing held by text, order, position or a fixed wait
  - Swept clean 19 Nov and 21 Nov (after T78, T79): no new text, order or fixed-wait dependency; run again after T80–T84
  - 21 Nov: the instant result's "match before" check held by the fixture's day (`FixtureDate`), not the opponent's name; the old key read a `Date` the fixture does not have (empty, and a 1.5 s wait each read)
  - Weakest still held: game words asked of the game's own table (registration flags, offer locks, `Response Required`); they work in every language, but lean on the game's English keys

## 13. Later (v2)

- [ ] T21 Opposition scouting priorities (`ScoutTeam`: `actions <next opponent> "Get Team Report›…"` asks for it; the priorities screen not found)
- [ ] Pages only under a "More" menu
- [ ] Read one value without opening its page
- [ ] Swap two starters; a player from outside the squad
- [ ] Youth training
- [ ] Season comparison and trends
- [ ] `fm watch`: report when a player or value changes
- [ ] Condition trend sparkline
- [ ] A web dashboard (the plugin already speaks HTTP)
- [ ] AI v2: see `AI.md`
