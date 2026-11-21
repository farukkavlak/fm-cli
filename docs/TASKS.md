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

## 8. Moments still to meet (catch them when they come: `fm crawl out.json --here`)

- [ ] T160 `dugout` mentality card ("Set team mentality"): read back from the match tactic's `Mentality`; see one live
  - Not met in 12 hunt matches (19 Nov), starting attacking or very defensive. A debug event sent to the dugout made no card
- [ ] T171 `dugout apply` cards read back by what they change
  - Opposition instruction card: read back by the man's UniqueId (his Pressing 0 → 2), live TR 1/3. Left: 2 more, and EN
  - A card that takes a team instruction off: not met yet
- [ ] T158 The game's own fatal-error window: the tool says so (exit 70) and `fm stop` ends it
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
- [ ] T154 TransferRoom `responses` and `activity --loan` tables: need real responses to our adverts

## 10. Quality

- [ ] T167 Game froze once for 6+ minutes: main thread inside the .NET JIT, compiling plugin code; not reproduced
- [ ] T168 `read club-history` gave its table as loose lines in 1 of 10 sweeps; not reproduced
- [ ] T169 Each resolution change takes the game longer to recover (about 3 minutes by the 16th)

## 11. Acceptance tests (each in one go, tool only, no bridge)

- [x] T78 Two full seasons through the CLI, Turkish game
  - 2034/35 and 2035/36 (sacked in Jan, new club, stayed up); season-end vision, team meeting and preseason date through the tool; scenario `season-end`
- [ ] T79 Two full seasons through MCP, English game
  - 2035/36 from 20 Mar (Antalyaspor, stayed up); season-end messages, team meeting, vision through MCP; scenario `t79-s1-end`
  - [x] `fm_reply` on a list answer ("1 2"): MCP passed it as one word; now `answer="1 2"` or `option`
  - [ ] `fm_next` while the game is busy: now waits for it (to see live)
  - [x] `fm_table` on the Messages screen also said "a cup has no table": now only the screen's reason (MCP and CLI)
  - [ ] `fm_inbox` had no `all`; the hint said `inbox --all` through MCP (to see live)
  - [x] MCP hints in CLI words: `fm_targets` takes grade/listed/loan_listed/attributes, `fm_register` hint split into commands (both seen through MCP)
  - [x] `fm_status` "squad --fitness" now `fm_squad view="fitness"` (seen with two injured)
  - [ ] `fm_contract` counter-offer hint reads `wage="/" --term`: now whole commands; to see at the next talks
  - [x] `fm_targets` wage budget says it is weekly
  - [ ] Training camp squad: answered Auto Select on 7 Jul, the message was back on 14 Jul (the game asking again, or the answer not kept?)
- [ ] T80 A third language: one month through the CLI, sweep passes
- [ ] T81 Windows (Steam): install from the README, one month, sweep, stop
- [ ] T82 Epic Games Store (macOS and Windows): install, one month, stop
- [ ] T83 A clean machine, README only
- [ ] T84 A new career (another country and league), one month, sweep

## 12. Final check (the last box)

- [ ] T85 Audit `cli/fm` and the plugin: nothing held by text, order, position or a fixed wait
  - Swept clean 19 Nov; run again once the acceptance tests are through

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
