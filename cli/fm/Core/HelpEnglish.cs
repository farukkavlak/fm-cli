namespace FmCli;

internal static partial class Program
{
	/// <summary>The help text, in English; the Turkish catalog holds each line's translation.</summary>
	internal const string HelpEnglish = """
			fm — run Football Manager from the terminal

			  fm start        start the game, load the save, enter the session   (--menu: leave it at the main menu)
			    --debug       show the window (normally kept out of the Dock and off screen)
			  fm              enter the session (while the game is open)
			  stop            close the game; asks whether to save first (--save / --no-save)

			  continue        load the last save in the open game
			  squad           print the squad
			    --fitness     condition and match sharpness, most tired first
			    --u19         the youth teams' squads (U21, U18…)
			    --morale      morale, hierarchy and social group
			    --stats       season stats: apps, minutes, goals, assists, average rating, cards
			    --views       list every view of the squad table
			    --view "<x>"  print that view's columns (Contract, Injuries, Playing Time…)
			  player <name>   player page: identity, contract, traits, attributes, position ratings, injury history (found by search if not ours)
			    --actions     what can be done for the player: offer, shortlist, scout, agent…
			    --report      scout report: grade, ability / potential, interest, value, pros and cons, place in the squad
			    --career      season by season: club, how they joined (fee / loan), apps, goals, rating
			    --matches     this season match by match: opponent, score, position/role, on/off, xG, goals, assists, rating
			  compare <a> <b> two players side by side: identity, then attribute by attribute, the better one marked (a name of several words: quotes or "vs")
			  search <text>   find it with the game's search: player, club, competition…
			  offer <player> [fee]  transfer offer: shown as a card, edited with the arrow keys in a terminal
			    --term "<term>=<value>"   set or change a term (e.g. "Percentage Of Next Sale=20%",
			                              "Fee=Non-Negotiable"); --loan a loan, --dry do not send;
			                              --close leave an unsent offer screen
			                              for one of ours with an offer waiting: their offer, sent back
			                              with our terms (--negotiate: talk it over with them instead)
			                  cannot be undone: asks in a terminal, wants --yes in a script
			  targets         transfer targets: the budget left and thin positions in the squad
			  targets <position> [--max-value <money>] [the search options of players]   players in that position the budget allows ("targets DL", "targets 'left back' --age 18-24"; --free: free agents only)
			  players         the player search (the rows on screen)
			    --filters / --filter "<x>"   the ready filters, by the game's name or: listed, loan listed, free, expiring, unhappy, goalkeepers…
			    --free                       free agents (contract expired)
			    --scouted / --unscouted      only the players the scouts know / everyone again
			    --clear                      remove the filter (it stays in the game)
			    --views / --view "<x>"       the views that set the columns
			    --position <p> / --role <r> / --age 18-23 / --max-value <money>   search by position, role, age, value
			    --attr "pace>=15"            an attribute condition (>=, <=, >, <); can be repeated
			    --grade <x> / --contract <x>   the scouts' grade; the contract status
			    --listed / --loan-listed     on the transfer list / on the loan list
			  table [competition] league table; with a competition's name, that competition's table (e.g. table europa)
			  stages [competition] [stage]   cup rounds and their ties, a league stage's table; every competition in the world (--all, --list; --refresh: read that list again)
			  scout [#|focus] scouting focuses; the players a focus has found (--matched, --near, --progress)
			  scout new [--position <position>] [--role <role>] [--area "<area>, …"] [--age 18-23] [--ability <stars>] [--potential <stars>] [--by "<scout>, …"] [--analysts "<analyst>, …"] [--name <name>] --priority top|standard|ongoing   a new focus
			  scout edit <#|name> [--area …] [--age …] [--ability …] [--potential …] [--by …] [--analysts …] [--name …] [--priority …]   change a focus
			  scout areas [path] · scout remove <#|name> [--yes] · scout restart <#|name>   area options; stop / restart a focus
			  scout staff     the scouting team: judging ability / potential, how many areas and nations they know, their focuses
			  scout player <name> [--by <scout>] --for <length>   send a scout to a player ("1 week", "2 months", "3 matches", "extensive"); scout who <name>: who can be assigned
			  scout player <name> --cancel [--dry]   call the assignment off (Scout Priorities); --dry only says whether there is one
			  scout teams [top|cancel <#|team>]   the team reports being prepared; put one first or call it off (ask for one: actions "<next opponent>")
			  league [competition]  leaders: top-scoring team, top scorer / assists, highest rating (default: the league)
			  league [competition] --stat <name> [--teams] [--top <n> | --all]   the full ranking: goals, assists, xG, form…;  league --stats: every stat
			  fixtures        the next matches;  --all: the whole season's fixtures
			  calendar [month] calendar: that month's matches and key dates (transfer window, draws…);  --u19 youth matches too
			  report          the last match's report: score, goals, player of the match, stats
			  inbox           the inbox
			    --read-all    mark every message read
			    --all         every message the inbox holds, not only the ones loaded
			    --tasks       only the messages under the inbox's Tasks filter
			    --open        the message last opened, with the game's number for its kind
			    --attachment  the open message's attachment, once the game has drawn it
			  news [#]        what the press writes: the headlines on the news site, in the page's own order; with a number, the whole story
			  reply <#>       open a message: its full text and answers; in a terminal the answer is chosen with the arrow keys
			  reply <#> <no>  answer a message (a number or part of the answer)
			    --pick <n>    first choose what the message itself offers (the board's season expectations)
			  next [n]        move time on (n times; stops when something is in the way), say what happened
			    --until match   go on until match day;  --until message  until a message arrives
			    --skip        go past an open post-match question without answering
			  holiday week|month|match   go on holiday: the assistant runs the club until then
			    --jobs <kind> / --no-jobs   apply for manager jobs while away (any, top, higher, reputation, national …)
			  match           play the match day: asks whether to watch in the terminal or take an instant result
			    --play        watch the match: commentary, goals, result; asks what to say in the talks
			                  (in a script it stops at a talk: talk <no>, then match)
			    --no-talk     skip the talks
			    --stop-at-advice   pause and stop when the assistant gives advice that can be applied (for scripts)
			    --instant     do not play it, leave it to the assistants: the game simulates it in a few seconds
			    --pick        another of our teams playing the same day with nobody picked (the U19s): use its staff's suggested team
			  shout [<x>]     shout from the touchline during a match (Well Done, Up Your Game, Calm Down…)
			  tweak [<x>]     during a match, the tactical tweaks (Take a Breather, Lock It Down…): which are on;  tweak <x> puts one on, --off takes it off
			    tweak [<x>] --player <player>   his individual tweaks;  tweak --reset team|individual|all
			  sub             during a match: on the pitch and on the bench;  sub <off> <on>: make a substitution
			  pause / resume  during a match: pause it, play on;  skip: skip the replay that is playing
			  dugout          during a match, the assistant's advice;  dugout apply (the newest one, says what it turned on);  dugout <no> apply | dismiss
			    --play-on     a match paused to take it plays on, so a team instruction it sets is read back
			    --yes         when the card would take a tweak that is on off, the game asks: yes to that
			  advice          the staff's advice by area (squad, tactics, transfers, training, club);  advice <no>|<name> apply | seen;  advice all seen
			  manager         the manager's record: clubs, transfers, results at this club and over the career
			  read [<page>]   the game's look-only pages as they stand (club history, honours, data hub, competition rules…): without a name lists them; read here: what is on screen
			  read [<page>] --of "<name>"   a club that is not ours, a nation, a national team or a competition: its page, or a page on its bar (it lists them)
			  meeting         hold a team meeting; answer it with talk
			  jobs [<club>] [apply|interest] [--yes] [--all]   job vacancies; apply or say you are interested
			  crawl <out.json> [--here] [--deep] [--only <guid>,…] [--skip <guid>,…]   for developers: a map of the game's pages (--deep: each page's menus too)
			  draw            run a cup draw its message opened, and show the ties
			  friendly [<#|club> [--date yyyy-mm-dd] [--away] [--rules <no>] --yes]   arrange a friendly: the opponents offered, open dates, match rules
			  resign [--yes]  resign from the club (test career; then look at jobs)
			  dialog [answer] show or answer the open confirmation window
			  talk            show the open conversation: what was said, reactions, what you can say
			  talk <no>       say it (a number or part of the sentence);  talk leave: leave the conversation
			  tour            run the open guided tour to the end
			    --skip        skip the tour (if the tour allows it)
			  contract [player]  the contract negotiation card: promises, then terms; edited with the arrow keys in a terminal; --next: on to the terms · --sign --yes: offer it
			                  with a player given (one of ours) it opens a new contract negotiation
			    --term "Term=value"  set / add a term ("Wage=20K", "Appearance Fee=remove",
			                  "Will appoint player as captain", "Entire offer=Non-Negotiable")
			    --wage <amount>  wage, in the game's wage period;  --next: confirm the promises, go on to the terms
			    --sign --yes  put the terms to the agent / sign;  --cancel: cancel the negotiation
			  contract <player> --agent  ask their agent whether they are open to a renewal (then with talk)
			  offers          offers for our players awaiting an answer;  offers <# | player> view | accept | stall | reject
			  propose <player> [fee] [--loan] [--yes]  offer the player to clubs (a sale or a loan)
			  release <player> [--free | --mutual] [--yes]  release on a free (shows the compensation) or a mutual termination
			  training [player]  individual training: position/role, out-of-possession role, focus;  --focus / --position "A>B" / --role;  --control player|month|all takes it over from the assistant
			  training <player> --report [--coach <name>]  ask a coach for a training report and show it
			  training <player> --trait ["<trait>"]  traits and what can be taught; given one, teach it
			                  --trait-remove "<t>" drop one · --trait-cancel · --yes: despite the assistant's objection
			  prep            match preparation: the Match Preparation sessions up to the next match (change one: schedule set)
			  schedule        the training schedule: this week and next, three sessions a day
			  schedule --team <team>   another of the club's teams (the B team, the U19s): its schedule; set / style / copy / paste / clear take --team too
			  schedule set <day> <1|2|3> [<session>]   change a session; with no session given choose it with the arrow keys
			  schedule style <day> [<schedule>]        change that week's schedule (Tactical, Gegenpress…; --json: by number)
			  schedule copy | paste | clear [this|<weeks ahead>]   the training week's own buttons (next week unless told)
			  budget          the transfer, wage and scouting budgets;  budget adjust [<step>]: the transfer ↔ wage split
			  settings        game settings: match display and speed, auto save, language, currency, units
			  settings all    every preference of the game, each with its key
			  settings <setting> [<value>]   change it (e.g. settings "highlight speed" 2x; a title or key from settings all too); with no value it is chosen with the arrow keys
			  lang            CLI language: english, turkish or auto (follow the game); FM_LANG also works
			  settings panel [view full|key|score] [pitch on|off] [events 0-12] [colour on|off] [radar on|off|pixel|braille|colour|ascii|auto]   the live match panel
			  happiness       the players' happiness, unhappiest first
			    happiness <player>   why: morale, their place in the dressing room, what pleases them and what they complain about
			    --why           everyone with a complaint, with their complaints
			  internationals  internationals: nation, caps and goals, who played this season
			  mentoring       mentoring groups and their members;  --auto: have the assistant build the groups (--yes if there are groups)
			    new [<p1> <p2> <p3>] [--name "<name>"]   build a group by hand; without players: who can be chosen
			    remove "<group>" --yes                   break a group up
			  dynamics        the dressing room: atmosphere, support for the manager, hierarchy and social groups
			  contracts       contract calendar: whose contract ends when, soonest first
			  contracts <player>  a player's contract: wage, status, bonuses and clauses
			    --expiring [<player> <choice>]  the "contracts set to expire" message: each player's recommended action; with a player and a choice (its number), that decision
			  club            club summary: the board's grade and objectives, supporters, balance and wages, facilities, gaps in the staff
			  staff           the club's staff: who has which job, their age, wage, contract; what the teams lack and the board's view
			  finances        finances: balance, this month's income/expenditure, debt, FFP, payroll by player status, sponsorship, projection
			  finances income|expenditure [month|last month|season|last season]   income or expenditure item by item
			  supporters      supporters: atmosphere, expectations and reactions, attendance, season tickets, supporter profile
			  responsibilities [filter]   who has the staff duties;  delegate "<duty>" hand it over · take "<duty>" take it back · delegate all / take all "<group or area>"
			  youth           youth: academy levels and stadium, first-team prospects, the intake date; --training: the youth team's training ratings; candidates: the year's intake; preview: the look ahead at it
			  progress        who is getting better, who worse: every player's ability over the last four months and the change
			  staff find [text] [--filter <filter>] [--view <view>] [--own | --no-own]   the staff search: job, contract, wage, attributes; --own: our staff too
			    --role <job> / --free / --employed <Yes|No> / --reputation <x>   search by job, employment, reputation
			  staff hire <name> [--wage <amount>] [--sign --yes]   hire staff: opens the contract negotiation, carried on with `contract`
			  staff fire <name> [--mutual] [--yes]   part ways with staff: the game names the compensation, says "No" without --yes
			  actions <person|club> ["<option>"]   the game's right-click menu for that person or club (assign a scout, shortlist, send on a course, a team report…); an option given is carried out
			  board           the board's and supporters' view: matches, squad, tactics, transfers, vision; what pleases them, their concerns; this season's expectations
			    request ["<request>" --yes]   what can be asked of the board; ask for one by name
			  doctor          what is wrong in the squad: injured, tired, low sharpness, ending contracts, thin positions, the board's concerns
			    --install     check the installation: game, BepInEx, shadow bundle, plugin, fm command, save folder
			  facilities      facilities: stadium (capacity, pitch, size), training facilities, youth, commercial
			  query "<conditions>"   query the squad: "age<23 and potential>140", "contract<1year", "position=ST and goals>2";  on its own it lists the fields
			    --sort <field> / --asc     ordering (default: the first condition's field)
			  health          squad health: injured, tired, low sharpness, highest injury risk
			  depth           squad depth: who can play each position, age profile
			  captains        captain and vice-captain;  --captain <player> / --vice <player>
			    captains --standins [quick | add <player> | remove <player> | clear]   the stand-ins, in order
			  vision          the end-of-season club vision meeting: objectives;  confirm | remove <no> | suggest | undo | reset | end | plan | add <no>
			  penalties       penalty order (composure, penalty taking) and the rules on who leaves the next one;  penalties <1|2|3> on|off
			  penalties move <player|place> <new place> · add <player> [<place>] · remove <player|place>   change the order
			  takers [<kind>] set piece takers: corner, freekick, wide, deep, throw; with a kind, both lists in detail
			  takers <kind> <right|left> move <player|place> <new place> · add <player> [<place>] · remove <player|place>   change that list's order
			  opponent        the next opponent: form, formation, top scorer / assists, squad news
			  world           the loaded save, when it was last saved, time played; how many nations / leagues / players are loaded
			  saves           the saves on disk, newest first
			    delete "<name>" --yes   delete a save (not the loaded one)
			  load "<name>"   load a particular save; if a save is loaded the game closes and opens again (--save / --no-save)
			  diff            what has changed since the last look: league position, balance, budget, in/out, injuries, contracts, apps/goals/assists
			    --keep        do not save this look (the comparison stays with the last one);  --reset: only save the state now
			  export [file.md] [command…]  the commands' output as one Markdown report, section by section (default: status, squad,
			                  table, fixtures, health, contracts, finances);  export <file.csv> "<command>": that command's table
			  --json / --csv / --md   with every command: JSON, CSV or Markdown in place of the table; no questions are asked
			  shortlist       the players on the shortlist (those with offers too)
			    --lists · --list "<name>" · --new "<name>"   the shortlists; show another; make one  (add … --to "<list>" with several)
			    add <name> --for <length> · remove <name>   put on the list ("3 months", "1 year", "indefinitely") / take off it
			  transferroom    clubs' adverts for players you could offer (--loan);  offer <no or club> [player] | activity | wanted | responses | new --position <p> [--role <r>] [--oop-position <p>] [--oop-role <r>] [--min-age <n>] [--max-age <n>] [--time <t>] [--type <t>] (a list's word, or its value as an error or --choices lists it) | delete <no> | agents | discard <player> | discard --all
			  planner         the Squad Planner: the plan at a position (--season current|next|after, --position <p>);  add <player> | remove <player> | move <player> <place> | suggest position|tactic|season|all
			  interest <name> --top|--transfer|--loan [--yes]   declare our interest in a player
			  enquire <name>  ask their club what it would want
			  trial <name> --for <1-4 weeks> [--yes]   invite a player on trial
			  intensity       training intensity player by player;  intensity <player> <double|"rest 3"|"holiday 1">, --auto [--yes]
			  coaches         coaching assignments: who is in which training area, the areas' workload and lead coach
			    coaches <coach> <area> [--off]   assign / remove ("coaches Cohen attacking tactical");  --auto: leave it to the assistant;  --clear: remove them all
			  opposition      player-by-player instructions against the next opponent (marking, pressing, tackling, foot)
			    --advice / --clear   apply / clear the assistant's suggestion;  opposition <player> Pressing=Always
			  register        competition squad list: who is on it, home-grown quotas, rules; add/remove with the arrow keys in a terminal
			    --in <player> / --out <player>   put on / take off the list (can be repeated)
			    --auto        let the assistant choose first;  --send: send it (once the rules are met)
			  numbers         squad numbers: who wears which, who asked for which, free numbers
			    numbers <player> <no>   give a number (while the game takes them: the start of the season or a new signing);  --send: save
			    --auto        let the assistant hand them out and send
			  setpieces new [<no> …]      set up the set piece routines (six questions; at a terminal asks what is not given, in a script stops at it)
			  setpieces <kind>            a routine: delivery, target, the players' duties (corner, freekick, wide, deep, throw)
			  setpieces <kind> delivery <in|out> | aim <option> | <place> <duty> [<otherwise>]   change the routine
			  tactic          the team's tactic: style, mentality, fluidity, eleven positions and roles
			  tactic mentality [<x>]      show or change the mentality
			  tactic style [<x>]          show or change the playing style
			  tactic pick                 have the game pick the starting eleven (Quick Pick)
			  tactic advice [evaluation | xi | opposition]   the assistant's advice on the tactic
			    --youth                   pick from the youth players too (when the first team is short)
			  tactic instructions ["<instruction>" [<value>]]  team instructions: in possession (phase by phase) and out of possession; with no value, the options
			  tactic pi <position|player> ["<instruction>" [<value>]]  player instructions (shooting, runs, pressing, marking…)
			  tactic slot [<n>]           tactic slots; with a number, switch to that tactic
			  tactic role <position> [<role>]  show or change a position's role (--oop: out-of-possession role)
			                              during a match: tactic role <player> [<role>]
			  tactic player <position> <player>  put a squad player in that position (the one there goes to the bench)
			  tactic player <substitute> <player>  swap a substitute (an injured one) for another squad player
			  tactic player <position> --clear  leave the position empty
			  tactic personal [<position> on|off [--dry]]  personalised roles: each starter's, or turn one on / off
			  tactic new ["<style>" ["<in-possession formation>" ["<out-of-possession formation>"]]]
			                  create a tactic; lists the options of the step left out
			  tactic copy [<tactic>]  copy a tactic to a new slot · tactic clear [<n>] --yes: empty a slot
			  tactic save "<name>" [--note "…"]  save the tactic to a file · tactic saved: the saved ones · tactic load "<name>" --yes: load into the active slot

			  pages [text]                every page of the game
			  open "<page>"               open a page
			    --tab <tab>               when several pages share a name
			  buttons [text]              the buttons on screen and what they send
			    --all                     show the unlabelled ones too
			  press "<label>" [--dry]     press a button
			    --name <name>             when several share a label
			  dropdowns [text]            the dropdowns on screen and their options
			  select <list> "<option>" [--dry]   choose from a list
			  type "<text>" [--dry]       write in the box of the window on screen (a nickname)
			    --dry                     for press, select and type: do not do it, say what would be done
			  status          the date and what the game is waiting for now
			  save --yes      save the game (overwrites the file, cannot be undone)
			    --as "<name>" save under another name; later saves go to that file too

			  --json          raw data (scriptable)
			  completion zsh  tab completion: `source <(fm completion zsh)` in ~/.zshrc; in bash `eval "$(fm completion bash)"`

			Short forms: the start of a command is enough when it fits one command (sq → squad, fix → fixtures). Short names:
			s status · n next · i inbox · t table · fx fixtures · m match · p player · tc tactic.
			Turkish command names work too, with or without accents.
			  fm mcp          offer the commands as MCP tools (stdio); for a model to play

			In the session you give the commands without typing `fm`; to leave: `quit`.
			""";
}
