#!/usr/bin/env python3
"""The one live test tool: scenario saves and rounds of tests.

  fm-round.py scenarios                       the scenario saves kept
  fm-round.py make <scenario> "<moment>"      keep the game's present moment as a scenario
  fm-round.py forward <yyyy-mm-dd> [--answer] days forward, matches simulated, until then or until the game asks;
                                              --answer: opens what needs no choice; stops at a choice
  fm-round.py enter <scenario> [--lang en]    a fresh copy of it, loaded, in that language
  fm-round.py dry <list.toml>                 check a test list; changes nothing in the game
  fm-round.py run <list.toml> [--lang en,tr] [--only T106 | a piece of a test's name]
  fm-round.py hunt <hunt.toml> [--lang en,tr] [--hours 6] [--dry]
                                              matches played and watched for every rare moment at once (below)
  fm-round.py home "<save>"                   back to a real save after a round

Scenarios are kept outside the game's folder, so nothing can write over them. Each entry copies one in as
`scn-<name>`, and the game's own saves land on that copy.

A test list (tests/live/*.toml):

  [[test]]
  task = "T106"
  name = "an unknown captain is refused"
  at = "calm"              # scenario
  fresh = true             # load it again first (a test before this one changed the game)
  repeat = 3
  fast = true              # the match speeds at their highest for this test, put back after
  steps = [
    { read = "captains", keep = "before", path = "Captain" },
    { run = "captains --captain zzzq --yes", rc = "fail" },
    { read = "captains", path = "Captain", same_as = "before" },
  ]

Steps:
  run = "<command>"        rc = 0 (default), a number, "fail" (any non-zero) or "any"; within = seconds it must answer in
  read = "<command>"       run with --json; path = "A.B.0.C", "#" is a list's length, "?Field" keeps a list's entries with it set, "?Field=v" with that value, "?Field>n" above n. Then is / not / same_as /
                           differs_from / present = true, and keep = "<name>" remembers the value
  until = "<command>"      a read asked again until it holds; seconds = the upper bound (default 300), every = the pace of asking (default 1)
                           doing = "<command>" runs between the readings instead of waiting (`talk 1` until the talk is over)
  start = "<command>"      runs beside the next steps (a match); `finish = true` waits for it to end
  stop = true              ends what `start` began, the match staying where it is (the next test starts fresh)
  live = true              on a read: it can only be read at that moment, so a dry run does not ask it
  moment = true            on an `until` beside a match: the match ending first is a try without the moment, not a
                           failure; `tries = N` on the test bounds the tries taken to reach `repeat` passes
  steer = true             on an `until` beside a match: our side steered towards a level score by tweaks (and our
                           goalkeeper off when ahead from 60'), for extra time and shootouts
  A match beside the steps that stops is answered and played on: a question by its primary button, a hurt man of
  ours for a substitute, then the step's `again`. A game that dies in a try is started again and the try counted
  as without the moment (T92), not as a failure.
  {name} in a command      the value kept under that name

A hunt list (tests/live/hunt*.toml): scenarios taken in turn while a moment they serve is short of its quota;
each match played to its end, every card applied, every stop answered, each moment checked when it shows:

  [[scenario]]
  at = "kickoff"
  serves = ["T160", "T156"]
  speeds = { "highlight speed" = "1x" }   # or fast = true; steer = true for a level score
  [[moment]]
  task = "T160"
  name = "a mentality card read back"
  probe = "dugout apply"   # or "status" (read each look), "injury" (a hurt man taken off at a stop)
  seen = "Changes.?Kind=mentality.#"
  not = "0"
  quota = { en = 2, tr = 2 }
  checks = [ { path = "Done", is = "True" } ]   # on the same reading; steps = [...] run / read after it

Checks read only --json and exit codes, never printed words: a test holds in every language.
"""
import argparse
import datetime
import json
import os
import pathlib
import re
import shlex
import shutil
import subprocess
import sys
import time
import threading
import tomllib

ROOT = pathlib.Path(__file__).resolve().parent.parent
STORE = pathlib.Path(os.environ.get("FM_SCENARIOS", pathlib.Path.home() / "fm27_bep" / "scenarios"))
RESULTS = ROOT / "tests" / "live" / "results"
PREFIX = "scn-"
LANGUAGES = {"en": ("english", "English"), "tr": ("turkish", "Türkçe")}
COMMAND_LIMIT = 1800
NOT_RUNNING = 69  # fm's exit code when the game is not running
FATAL = 70  # fm's exit code when the game sits in its own fatal-error window (T148)
DEATHS = 5  # games dead under one test (T92) before it is given up for the round
PLAYER_LOG = pathlib.Path.home() / "Library/Logs/Sports Interactive/Football Manager 27/Player.log"


def watch_memory(file, stop):
    """The game's memory (its phys_footprint, what macOS charges it; `ps` shows the compressed part only) and the Mac's
    swap, a line a minute while a round runs, so growth over a long-lived game can be seen (T92). Measuring only."""
    with open(file, "a") as out:
        out.write("time\tpid\tgame_mb\tswap_used\n")
        while not stop.is_set():
            try:
                pid = subprocess.run(["pgrep", "-f", "fm27_bep/fm.app/Contents/MacOS/Football Manager"],
                                     capture_output=True, text=True, timeout=10).stdout.split()
                mb = ""
                if pid:
                    found = re.search(r"Footprint: (\d+) MB", subprocess.run(["/usr/bin/footprint", "-p", pid[0]],
                                                                              capture_output=True, text=True, timeout=60).stdout)
                    mb = found.group(1) if found else ""
                swap = re.search(r"used = ([\d.]+M)", subprocess.run(["sysctl", "vm.swapusage"], capture_output=True,
                                                                      text=True, timeout=10).stdout)
                out.write(f"{datetime.datetime.now():%H:%M:%S}\t{pid[0] if pid else ''}\t{mb}\t{swap.group(1) if swap else ''}\n")
                out.flush()
            except (OSError, subprocess.SubprocessError):
                pass
            stop.wait(60)


def keep_log(name):
    """The dead game's Player.log, before the next start writes over it (T92)."""
    try:
        shutil.copy(PLAYER_LOG, RESULTS / name)
    except OSError:
        return ""
    # The Mac's memory at that moment: an 8 GB Mac ran 4.9 GB into swap the night deaths came in a row (T92).
    try:
        memory = subprocess.run("sysctl vm.swapusage; memory_pressure | tail -1; top -l 1 -o mem -n 8 -stats command,mem",
                                shell=True, capture_output=True, text=True, timeout=30).stdout
        (RESULTS / f"{name}.memory.txt").write_text(memory)
    except (OSError, subprocess.SubprocessError):
        pass
    return f" (its log: {name})"


class Failed(Exception):
    pass


class Fatal(Exception):
    """The game put up its own fatal-error window: not a step's failure, and not caught where a step waits."""


class Missed(Exception):
    """The moment a step waits for did not come (a match ended level-less, no card): not a failure, not a pass."""


LANG_NOW = None  # the language a run is in, for a step written for it (run_<lang>)
LOG = None  # a run keeps every command's answer here: a failed try can be read without playing it again


def fm(command, timeout=COMMAND_LIMIT):
    """One command through the tool. Stdin is shut, so it never stops at a prompt."""
    start = time.time()
    try:
        done = subprocess.run(["fm", *shlex.split(command)], stdin=subprocess.DEVNULL, capture_output=True, text=True, timeout=timeout)
        result = done.returncode, done.stdout, done.stderr, time.time() - start
    except subprocess.TimeoutExpired:
        result = 124, "", f"no answer in {timeout} s", time.time() - start
    if LOG:
        LOG.write(f"{datetime.datetime.now():%H:%M:%S} fm {command} rc={result[0]} {result[3]:.1f}s\n{result[1][:6000]}{result[2][:500]}\n")
        LOG.flush()
    if result[0] == FATAL:
        raise Fatal(f"fm {command}: {result[2].strip()[:200]}")
    return result


def must(command, timeout=COMMAND_LIMIT):
    rc, out, err, took = fm(command, timeout)
    if rc != 0:
        raise Failed(f"fm {command}: rc={rc} {(err or out).strip()[:300]}")
    return out, took


def state():
    """The game's state. Date can be missing after a simulated match, until a page is opened or a day goes by (T108)."""
    return json.loads(must("status --json", 300)[0])


def saves_folder():
    rows = must("saves")[0].strip().splitlines()
    folder = pathlib.Path(rows[-1].strip())
    if not folder.is_dir():
        raise Failed(f"no save folder: {rows[-1]}")
    return folder


def dig(data, path):
    """A value by its path; "#" is a list's length, "?Field" a list's entries with it set, "?Field=value" those with
    that value, "?Field>n" those with a number above n. Raises KeyError when the path is not there."""
    for part in path.split(".") if path else []:
        if part == "#":
            data = len(data)
        elif part.startswith("?") and ">" in part:
            field, value = part[1:].split(">", 1)
            data = [entry for entry in data if float(entry.get(field) or 0) > float(value)]
        elif part.startswith("?") and "=" in part:
            field, value = part[1:].split("=", 1)
            data = [entry for entry in data if str(entry.get(field)) == value]
        elif part.startswith("?"):
            data = [entry for entry in data if entry.get(part[1:])]
        elif isinstance(data, list):
            data = data[int(part)]
        else:
            data = data[part]
    return data


def read_json(command, timeout=COMMAND_LIMIT):
    rc, out, err, _ = fm(f"{command} --json", timeout)
    if rc != 0:
        raise Failed(f"fm {command} --json: rc={rc} {(err or out).strip()[:300]}")
    try:
        return json.loads(out)
    except json.JSONDecodeError as error:
        raise Failed(f"fm {command} --json: not JSON ({error}): {out[:200]}") from error


# ---- scenarios


def meta(name):
    file = STORE / f"{name}.json"
    return json.loads(file.read_text()) if file.exists() else None


def scenarios(_):
    kept = sorted(STORE.glob("*.fm")) if STORE.exists() else []
    if not kept:
        print(f"No scenarios in {STORE}")
    for file in kept:
        about = meta(file.stem) or {}
        print(f"{file.stem:12} {about.get('date', '?'):12} {file.stat().st_size / 1048576:6.1f} MB  {about.get('moment', '')}")
    return 0


def make(args):
    if not re.fullmatch(r"[a-z0-9-]+", args.name):
        raise Failed("a scenario's name is lower-case letters, digits and -")
    before = state()
    if before.get("CanSave") != "True" or not before.get("Date"):
        raise Failed(f"the game cannot save now, or has no date: {before}")
    save = f"{PREFIX}{args.name}"
    _, took = must(f'save --as "{save}" --yes')
    source = saves_folder() / f"{save}.fm"
    if not source.exists():
        raise Failed(f"saved, but there is no file {source}")
    STORE.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, STORE / f"{args.name}.fm")
    (STORE / f"{args.name}.json").write_text(json.dumps({
        "moment": args.moment, "date": before["Date"], "state": before["State"], "made": datetime.datetime.now().isoformat(timespec="seconds"),
        "save_seconds": round(took, 1),
    }, indent=1))
    print(f"{args.name}: {before['Date']} kept ({took:.0f} s to save). The game now saves to {save}.")
    return 0


def answer_something():
    """Only to reach a moment: what needs no choice is opened or handed to the assistant, each step printed.
    A conversation or a message with choices is not answered: the first choice once declined three job
    interviews and lost a job at a board meeting (Mar 2030). The game's own `CanRespond` says which has them.
    True only when the game's state moved, so a step the game ignores cannot loop forever."""
    _, shown, _, _ = fm("status")
    moved = lambda: fm("status")[1] != shown
    rc, out, _, _ = fm("talk --json")
    if rc == 0 and out.lstrip().startswith("{") and json.loads(out).get("Answers"):
        print(f"  a conversation waits: {json.loads(out)['Says'][:1]}", flush=True)
        return False
    rc, out, _, _ = fm("inbox --json")
    choices = {m.get("NewsTitleString") for m in json.loads(out) if m.get("CanRespond") == "True"} if rc == 0 and out.lstrip().startswith("[") else set()
    names = list(dict.fromkeys(re.findall(r'reply "([^"]+)"', shown) + [line.strip() for line in shown.splitlines() if line.startswith("  ")]))
    for name in names:
        if name in choices:
            print(f"  a message with choices waits: {name}", flush=True)
            return False
        print(f"  opened: {name}", flush=True)
        if fm(f'reply "{name}"')[0] == 0 and moved():
            return True
    # Screens a message cannot answer: squad numbers at the season's start, the set-piece wizard of a new job.
    for step in ("numbers --auto", "setpieces new", "next --skip"):
        if fm(step)[0] == 0 and moved():
            print(f"  {step}", flush=True)
            return True
    return False



CAUGHT = STORE / "caught.json"


def moments_waiting():
    """What the game waits on now, each by its kind in the game's own terms, never its words: a message with
    choices by its tags (the game's category numbers) and how many answers it offers, a window by its type, an
    open conversation. The title is kept only for reading the log."""
    found = []
    rc, out, _, _ = fm("dialog --json")
    if rc == 0 and out.lstrip().startswith("["):
        found += [(f"dialog:{d.get('Type', '')}", d.get("Title", "")) for d in json.loads(out)]
    rc, out, _, _ = fm("talk --json")
    if rc == 0 and out.lstrip().startswith("{") and json.loads(out).get("Answers"):
        found.append((f"talk:{len(json.loads(out)['Answers'])}", (json.loads(out).get("Says") or [""])[0]))
    rc, out, _, _ = fm("inbox --json")
    if rc == 0 and out.lstrip().startswith("["):
        for m in json.loads(out):
            if m.get("CanRespond") != "True" or m.get("NewsHasResponded") == "True":
                continue
            if not m.get("TagsList"):
                continue  # its tags not bound yet: no kind to tell it by
            _, shown, _, _ = fm(f'reply "{m.get("NewsTitleString", "")}"')
            # The message's answers, at two spaces; a choice inside the message ("    1  [x] Normal") is not one.
            answers = len(re.findall(r"^  \d+  \S", shown, re.M))
            if answers == 0:
                continue  # read and put away: nothing to choose
            found.append((f"message:{m.get('TagsList', '')}:{answers}", m.get("NewsTitleString", "")))
    return found


def catch():
    """One scenario kept where kinds of moment not kept before wait; caught.json says which kinds, when, their titles."""
    caught = json.loads(CAUGHT.read_text()) if CAUGHT.exists() else []
    kinds = {c["kind"] for c in caught}
    new = [(kind, title) for kind, title in moments_waiting() if kind not in kinds]
    new = list({kind: (kind, title) for kind, title in new}.values())
    if not new:
        return
    # A kept save is ~175 MB: the disk is not filled for it.
    if shutil.disk_usage(STORE).free < 4 * 1024 ** 3:
        print(f"  not kept (under 4 GB free): {new}", flush=True)
        return
    now = state()
    if now.get("CanSave") != "True":
        return
    name = f"caught-{now.get('Date', 'x')}"
    while (STORE / f"{name}.fm").exists():
        name += "b"
    try:
        make(argparse.Namespace(name=name, moment="caught: " + "; ".join(f"{k} ({t})" for k, t in new)))
    except Failed as error:
        print(f"  could not keep {new}: {error}", flush=True)
        return
    # The game now saves to this one; the walk's earlier working copies are kept in the store already.
    for old in saves_folder().glob(f"{PREFIX}caught-*.fm"):
        if not old.name.startswith(f"{PREFIX}{name}"):
            old.unlink()
    for kind, title in new:
        caught.append({"kind": kind, "title": title, "date": now.get("Date"), "scenario": name})
        print(f"  caught {kind}: {title} -> {name}", flush=True)
    CAUGHT.write_text(json.dumps(caught, indent=1, ensure_ascii=False))


POOL = STORE / "pool"
WALK_LOG = STORE / "walk.jsonl"


def walk_note(entry):
    """The walk's log, for reading afterwards only: what came each day, so a moment passed by can be found and its
    pool copy kept. Nothing in the walk decides by it."""
    entry = {"at": datetime.datetime.now().isoformat(timespec="seconds"), **entry}
    with WALK_LOG.open("a") as file:
        file.write(json.dumps(entry, ensure_ascii=False) + "\n")


def note_inbox(now, seen):
    rc, out, _, _ = fm("inbox --json")
    if rc != 0 or not out.lstrip().startswith("["):
        return
    for m in json.loads(out):
        key = m.get("UniqueId") or m.get("NewsTitleString")
        if key in seen:
            continue
        seen.add(key)
        walk_note({"date": now.get("Date"), "message": m.get("NewsTitleString"), "tags": m.get("TagsList"),
                   "choices": m.get("CanRespond"), "answered": m.get("NewsHasResponded")})


def pool_copy(tag, size):
    """The save just written, copied into the pool; the oldest copies go past `size`. A moment noticed only in
    the log afterwards is taken up from here with `keep`."""
    if size <= 0 or shutil.disk_usage(STORE).free < 6 * 1024 ** 3:
        return
    saves = sorted(saves_folder().glob(f"{PREFIX}*.fm"), key=lambda f: f.stat().st_mtime)
    if not saves:
        return
    date = state().get("Date") or "x"
    POOL.mkdir(parents=True, exist_ok=True)
    target = POOL / f"{date}-{tag}.fm"
    shutil.copy2(saves[-1], target)
    for old in sorted(POOL.glob("*.fm"), key=lambda f: f.stat().st_mtime)[:-size]:
        old.unlink()
    walk_note({"date": date, "pool": target.name})


def keep(args):
    """A pool copy made a scenario, under a name of its own."""
    source = POOL / f"{args.copy}.fm"
    if not source.exists():
        raise Failed(f"no pool copy '{args.copy}': " + ", ".join(f.stem for f in sorted(POOL.glob("*.fm"))))
    if not re.fullmatch(r"[a-z0-9-]+", args.name):
        raise Failed("a scenario's name is lower-case letters, digits and -")
    shutil.copy2(source, STORE / f"{args.name}.fm")
    (STORE / f"{args.name}.json").write_text(json.dumps({"moment": args.moment, "date": args.copy[:10], "state": "",
        "made": datetime.datetime.now().isoformat(timespec="seconds")}, indent=1))
    print(f"{args.name}: kept from the pool ({args.copy})")
    return 0


def newest_message():
    """The inbox's newest message by the game's own id; None when it cannot be read."""
    rc, out, _, _ = fm("inbox --json")
    if rc != 0 or not out.lstrip().startswith("["):
        return None
    ids = [int(m["UniqueId"]) for m in json.loads(out) if str(m.get("UniqueId", "")).isdigit()]
    return max(ids) if ids else None


def reject_bids(hold=()):
    """In a walk the squad is kept: each incoming bid the game lets us refuse is refused. True if one was. A bid for
    a player named in `hold` (a promise made to him) is left for the manager and logged."""
    tried, done = set(), False
    while True:
        rc, out, _, _ = fm("offers --json")
        if rc != 0 or not out.lstrip().startswith("["):
            return done
        left = [o for o in json.loads(out) if o.get("Incoming") and (o.get("Player"), o.get("Club")) not in tried]
        if not left:
            return done
        tried.add((left[0].get("Player"), left[0].get("Club")))
        if any(h.lower() in str(left[0].get("Player", "")).lower() for h in hold):
            print(f"  held: {left[0].get('Club')} for {left[0].get('Player')} (left for the manager)", flush=True)
            walk_note({"moment": "held bid", "club": left[0].get("Club"), "player": left[0].get("Player"), "offer": left[0]})
            continue
        if fm(f"offers {left[0]['No']} reject --yes")[0] == 0:
            print(f"  refused: {left[0].get('Club')} for {left[0].get('Player')}", flush=True)
            done = True


# Kinds of message a walk stops at, by the game's own number for the message (`inbox --open`, `NewsItemId`),
# never its words: what a manager acts on there or loses the squad (T139: 30 Jun 2034, 12 players left).
STOP_KINDS = {"125": "contracts set to expire"}


def kind_waiting(checked):
    """A message with choices, not answered, of a kind in STOP_KINDS: (kind's note, title, id), else None. Each
    message is opened once (`reply` with no answer changes nothing) and its kind read; `checked` keeps its id."""
    rc, out, _, _ = fm("inbox --json")
    if rc != 0 or not out.lstrip().startswith("["):
        return None
    for m in json.loads(out):
        key = m.get("UniqueId")
        if m.get("CanRespond") != "True" or m.get("NewsHasResponded") == "True" or key in checked:
            continue
        if fm(f'reply "{m.get("NewsTitleString", "")}"')[0] != 0:
            continue
        rc, out, _, _ = fm("inbox --open --json")
        opened = json.loads(out) if rc == 0 and out.lstrip().startswith("{") else {}
        if opened.get("UniqueId") != key:
            continue  # another message came up: looked at again next time
        checked.add(key)
        if opened.get("NewsItemId") in STOP_KINDS:
            return STOP_KINDS[opened["NewsItemId"]], m.get("NewsTitleString", ""), key
    return None


def forward(args):
    """Days forward through the tool, matches simulated, up to a date. Stops where the game asks something.
    With --answer the first answers can lose the job (a board meeting answered blindly, 1 Mar 2030): while
    employed it saves once a week, and stops the moment the job is gone, so the week's save is the way back."""
    employed = fm("squad")[0] == 0
    saved_week = None
    answered = False
    # Answers that do not move the game on (offers opened again and again, 6 Nov) end the run instead of looping.
    idle, last_moment = 0, None
    seen, noted_day = set(), None
    checked, kinds_day = set(args.past or ()), None
    while True:
        now = state()
        if args.answer and now.get("Date") and now.get("Date") != kinds_day:
            kinds_day = now.get("Date")
            found = kind_waiting(checked)
            if found:
                print(f"Stopped at {now.get('Date')}: a message a manager acts on waits ({found[0]}): {found[1]}\n"
                      f"  answered or dealt with, go on with: --past {found[2]}")
                if args.pool:
                    walk_note({"date": now.get("Date"), "moment": "stopped", "kind": found[0], "message": found[1]})
                return 4
        if args.pool and now.get("Date") and now.get("Date") != noted_day:
            noted_day = now.get("Date")
            note_inbox(now, seen)
        moment = (now.get("Date"), now.get("Clock"))
        idle = idle + 1 if moment == last_moment else 0
        last_moment = moment
        if idle > 3:
            print(f"Stopped at {now.get('Date')}: answering does not move the game on; it waits for:\n{fm('status')[1].strip()[-600:]}")
            return 2
        if args.answer and employed:
            # The job first: a save taken after it is gone would write over the way back.
            week = datetime.date.fromisoformat(now["Date"]).isocalendar()[:2] if now.get("Date") else None
            saving = week and week != saved_week
            if (answered or saving) and fm("squad")[0] != 0:
                print(f"Stopped at {now.get('Date')}: the job is gone. The save from the week of {saved_week} is the way back.")
                return 3
            answered = False
            if saving and fm("save --yes")[0] == 0:
                saved_week = week
                pool_copy("week", args.pool)
        if now.get("Date", "") >= args.date:
            print(f"Reached {now['Date']} ({now['State']}).")
            return 0
        before = now.get("Date"), now.get("Clock")
        match_day = now["State"] in ("Tactics", "SubmitTeam", "SquadNumbers")
        # Before each match: one way back to its kick-off (a cup tie that went to penalties, a card, a goal).
        if match_day and args.pool and now.get("CanSave") == "True" and fm("save --yes")[0] == 0:
            pool_copy("match", args.pool)
        newest_before = None if match_day else newest_message()
        rc, out, err, _ = fm("match --instant --pick" if match_day else "next")
        if match_day and args.pool:
            walk_note({"date": now.get("Date"), "match": (out or err).strip()[-400:], "rc": rc})
        # A youth team's match the manager must pick for (no U-19 manager) is not given an instant result (T135):
        # it is played through instead.
        # A player missing (injured, banned) refuses it too: the assistant picks again first, youth players if short.
        if match_day and rc != 0 and state()["State"] in ("Tactics", "SubmitTeam") and (fm("tactic pick")[0] == 0 or fm("tactic pick --youth")[0] == 0):
            rc, out, err, _ = fm("match --instant --pick")
            if args.pool:
                walk_note({"date": now.get("Date"), "match": (out or err).strip()[-400:], "rc": rc, "after": "picked again"})
        if match_day and rc != 0 and state()["State"] in ("Tactics", "SubmitTeam"):
            print("  instant result refused: the match played through", flush=True)
            if args.pool:
                walk_note({"date": now.get("Date"), "moment": "instant result refused (a youth match, T135, or a player missing)", "said": (out or err).strip()[-300:]})
            rc, out, err, _ = fm("match --play --no-talk", timeout=1800)
            if args.pool:
                walk_note({"date": now.get("Date"), "match": (out or err).strip()[-400:], "rc": rc, "after": "played through"})
        stopped = rc != 0 or (not match_day and (state().get("Date"), state().get("Clock")) == before)
        # Deadline day goes on hour by hour with the date and clock read the same (4 Feb 2033): a new message
        # come in is the game moving on.
        if stopped and rc == 0 and not match_day and newest_message() != newest_before:
            stopped = False
        if stopped and not match_day and state()["State"] in ("Tactics", "SubmitTeam", "SquadNumbers"):
            continue  # opening the last message led on to the match: not a stop
        # Not on a match day: the tool's own windows (the result handed over) would be caught and stop a save.
        if stopped and args.catch and not match_day:
            catch()
        if stopped:
            # Short of eleven (injuries, illness, loans): the youth players too, as a manager would.
            if args.answer and match_day and (fm("tactic pick")[0] == 0 or fm("tactic pick --youth")[0] == 0):
                continue
            if args.catch and reject_bids(args.hold or ()):
                continue
            if args.answer and answer_something():
                answered = True
                continue
            print(f"Stopped at {state().get('Date')}, the game asks:\n{(out + err).strip()[-900:]}")
            return 2


def working_copies(folder, name):
    """The entry's file and the game's own rolling copies of it: `scn-x.fm`, `scn-x (v02).fm`."""
    return [f for f in folder.glob(f"{PREFIX}{name}*.fm") if re.fullmatch(rf"{re.escape(PREFIX + name)}( \(v\d+\))?\.fm", f.name)]


def in_scenario(folder, name):
    """Copies the kept scenario over its working copy. The game must be shut or in another save."""
    source = STORE / f"{name}.fm"
    if not source.exists():
        raise Failed(f"no scenario '{name}' (fm-round.py scenarios)")
    for old in working_copies(folder, name):
        old.unlink()
    shutil.copy2(source, folder / f"{PREFIX}{name}.fm")


def set_language(lang, times):
    """In a loaded game only: at the main menu the tool has no way to it. True if it changed."""
    rc, out, _, _ = fm("status --json", 120)
    current = json.loads(out).get("Language") if rc == 0 and out.strip() else None
    if not lang or current in (None, LANGUAGES[lang][0]):
        return False
    step = time.time()
    # A match on (or a screen that takes no settings) refuses; the load after it asks again.
    if fm(f'settings language "{LANGUAGES[lang][1]}"', 300)[0] != 0:
        return False
    times["language"] = times.get("language", 0) + round(time.time() - step, 1)
    return True


def fresh_load(name, folder, times):
    # Shut first: a game holding this file as its current save would write over the copy on its way out.
    step = time.time()
    must("stop --no-save", 300)
    times["stop"] = times.get("stop", 0) + round(time.time() - step, 1)
    in_scenario(folder, name)
    step = time.time()
    # Starting the game for the load failed now and then (6 Nov: "did not reach the main menu", "FM is not
    # running"), and the next try passed: one more try, said in the times.
    rc, out, err, _ = fm(f'load "{PREFIX}{name}" --no-save', 600)
    if rc != 0:
        times["load_retry"] = (err or out).strip()[:80]
        must(f'load "{PREFIX}{name}" --no-save', 600)
    times["load"] = times.get("load", 0) + round(time.time() - step, 1)


def enter(name, lang=None):
    """Fresh copy, right language, loaded and read back; returns the seconds each part took."""
    start = time.time()
    folder = saves_folder()
    times = {}
    set_language(lang, times)
    fresh_load(name, folder, times)
    # A game that was at the main menu could not be told its language before the load.
    if set_language(lang, times):
        fresh_load(name, folder, times)
    now = state()
    expected = (meta(name) or {}).get("date")
    if expected and now["Date"] != expected:
        raise Failed(f"loaded '{name}' but the date is {now['Date']}, it was kept at {expected}")
    if lang and now.get("Language") != LANGUAGES[lang][0]:
        raise Failed(f"loaded '{name}' but the game's language is {now.get('Language')}")
    times["total"] = round(time.time() - start, 1)
    return times


def enter_command(args):
    print(json.dumps(enter(args.name, args.lang)))
    return 0


def home(args):
    must("stop --no-save", 300)
    print(must(f'load "{args.save}" --no-save', 600)[0].strip())
    return 0


# ---- test lists


def load_list(path):
    with open(path, "rb") as file:
        return tomllib.load(file).get("test", [])


STEP_KINDS = ("run", "read", "until", "start", "finish", "stop")
READ_KEYS = {"read", "until", "path", "keep", "is", "not", "same_as", "differs_from", "present", "absent", "seconds", "every", "live", "note", "doing", "again", "moment", "steer"}


def problems_in(tests):
    """What can be told without the game: the list's own shape. A wrong step found here costs no match."""
    found = []
    for at, test in enumerate(tests, 1):
        where = f"test {at} ({test.get('task', '?')} {test.get('name', '?')})"
        for key in ("task", "name", "at", "steps"):
            if key not in test:
                found.append(f"{where}: no {key}")
        if not (STORE / f"{test.get('at')}.fm").exists():
            found.append(f"{where}: no scenario '{test.get('at')}'")
        kept = set()
        running = False
        for number, step in enumerate(test.get("steps", []), 1):
            # A kept value is put in already quoted: quotes around it make the command see the quotes (13 Nov, twice).
            for text in [v for k, v in step.items() if isinstance(v, str)] + [d for d in step.get("doing", []) if isinstance(d, str)]:
                if re.search(r"""["']\{\w+\}["']""", text):
                    found.append(f"{where} step {number}: no quotes around a kept value, it comes quoted: {text}")
            kinds = [k for k in STEP_KINDS if k in step]
            if len(kinds) != 1:
                found.append(f"{where} step {number}: one of {STEP_KINDS}, found {kinds}")
                continue
            kind = kinds[0]
            if kind in ("read", "until"):
                unknown = set(step) - READ_KEYS
                if unknown:
                    found.append(f"{where} step {number}: unknown {sorted(unknown)}")
                if "path" not in step:
                    found.append(f"{where} step {number}: a read needs a path")
                for key in ("same_as", "differs_from"):
                    if key in step and step[key] not in kept:
                        found.append(f"{where} step {number}: {key} '{step[key]}' was not kept before")
                if "keep" in step:
                    kept.add(step["keep"])
                if kind == "until" and not {"is", "not"} & set(step):
                    found.append(f"{where} step {number}: until waits for is / not")
            # `run_<lang>`: the same step as a player of that language types it (a term's value in the game's
            # words: "2 years", "2 yıl"); `run` is taken in the others.
            allowed = {"run", "rc", "within", "note"} | {f"run_{lang}" for lang in LANGUAGES}
            if kind == "run" and set(step) - allowed:
                found.append(f"{where} step {number}: unknown {sorted(set(step) - allowed)}")
            if kind == "start":
                running = True
            if kind in ("finish", "stop"):
                if not running:
                    found.append(f"{where} step {number}: finish with nothing started")
                running = False
        if running:
            found.append(f"{where}: started something and never finished it")
    return found


def known_flags(command):
    """A command's flags as its help names them, or None if the help says nothing about it."""
    word = shlex.split(command)[0]
    _, out, _, _ = fm(f"help {word}", 30)
    return set(re.findall(r"--[a-z][a-z-]*", out)), bool(out.strip())


def commands_of(test):
    for step in test["steps"]:
        for kind in ("run", "read", "until", "start"):
            if kind in step:
                yield step, kind, step[kind]


def worst_case(tests):
    """Minutes a list can take at most: each entry about 2 minutes, each `until` its upper bound, every repeat."""
    seconds = 0
    for test in tests:
        steps = test.get("steps", [])
        seconds += test.get("repeat", 1) * (120 * bool(test.get("fresh")) + sum(s.get("seconds", 300) for s in steps if "until" in s))
    return round(seconds / 60)


def dry(args):
    tests = load_list(args.list)
    found = problems_in(tests)
    print(f"Worst case {worst_case(tests)} minutes a language.")
    seen = {}
    asked = {}
    for test in tests:
        for step, kind, command in commands_of(test) if "steps" in test else []:
            word = shlex.split(command)[0]
            if word not in seen:
                rc, out, err, _ = fm(f"{word} --help", 30)
                seen[word] = known_flags(command)
                if (rc == 64 and not out.strip()) or not seen[word][1]:
                    found.append(f"{test['task']} {test['name']}: `{word}` has no help; is it a command?")
            flags = set(re.findall(r"(?<!\S)--[a-z][a-z-]*", command)) - {"--yes", "--json"}
            for flag in sorted(flags - seen[word][0]):
                if seen[word][1]:
                    found.append(f"{test['task']} {test['name']}: `{word}` does not name {flag} in its help")
            # Reads are asked, once each: their path must be there, unless only that moment has it.
            if kind in ("read", "until") and not step.get("live") and "{" not in command:
                key = f"{command}|{step.get('path')}"
                if key not in asked:
                    asked[key] = True
                    try:
                        dig(read_json(command, 300), step.get("path", ""))
                    except (Failed, KeyError, IndexError, ValueError, TypeError, AttributeError) as error:
                        found.append(f"{test['task']} {test['name']}: read `{command}` path `{step.get('path')}`: {error}")
    for problem in found:
        print(f"  ! {problem}")
    print(f"{len(tests)} tests, {len(asked)} reads asked, {len(found)} problems.")
    return 1 if found else 0


def holds(step, value, kept):
    if "is" in step and str(value) != str(step["is"]):
        return f"is {value!r}, wanted {step['is']!r}"
    if "not" in step and str(value) == str(step["not"]):
        return f"is {value!r}, wanted anything else"
    if "same_as" in step and value != kept[step["same_as"]]:
        return f"is {value!r}, was {kept[step['same_as']]!r}"
    if "differs_from" in step and value == kept[step["differs_from"]]:
        return f"is still {value!r}"
    if step.get("present") and value in (None, "", [], {}):
        return "is empty"
    return None


def filled(command, kept):
    return re.sub(r"\{(\w+)\}", lambda m: shlex.quote(str(kept[m.group(1)])), command)


SPEEDS = ("highlight speed", "speed between highlights")


def fast_matches():
    """The game's two match speeds at their highest for a test; what they were, to put back. The player's own settings."""
    was = {}
    for name in SPEEDS:
        now = read_json(f'settings "{name}"')
        was[name] = now["Value"]
        must(f'settings "{name}" "{now["Choices"][-1]}"')
    return was


def put_back(speeds):
    """The player's speeds again. During a match the page does not open: what is left stays in `speeds` for later."""
    for name, value in list(speeds.items()):
        if fm(f'settings "{name}" "{value}"')[0] == 0:
            del speeds[name]


# ---- a match beside the steps: its stops answered, its score steered


SEEN_DIALOGS = []  # the Type of each question answered during a match this run (T29: cancelTweaks, cancelChanges)
KEEPER = 1  # the goalkeeper's bit in the game's position mask (slot IPPosition, a player's CoachBestPosition)


def keeper_of(subs, name):
    """True if `name` stands in our eleven's goalkeeper slot. The planner can hold a short name for the full one."""
    for man in subs.get("Eleven", []):
        if (man["Name"] == name or name.endswith(" " + man["Name"])) and int(man.get("Position") or 0) & KEEPER:
            return True
    return False


def bench_man(subs, keeper):
    """A substitute: a goalkeeper for a goalkeeper, an outfield man otherwise, told by his CoachBestPosition
    (`sub`'s own Position, from the team's data: the squad page cannot open in a match, so a squad reading there
    left every man unknown and the first, often the goalkeeper, was sent on). Any man unknown is a last resort outfield choice."""
    squad = {b.get("Id"): int(b["Position"]) for b in subs.get("BenchPlayers", []) if str(b.get("Position") or "").isdigit()}
    known = [b for b in subs.get("BenchPlayers", []) if b.get("Id") in squad]
    fitting = [b for b in known if bool(squad[b["Id"]] & KEEPER) == keeper]
    if fitting:
        return fitting[0]["Name"]
    unknown = [b for b in subs.get("BenchPlayers", []) if b.get("Id") not in squad]
    return unknown[0]["Name"] if unknown and not keeper else None


def answer_stop(again, kept):
    """The match stopped beside the steps: a question answered with its primary button (its slot, not its words),
    each hurt man of ours taken off for a substitute, then the step's own `again` (staff advice before kick-off)."""
    try:
        dialogs = read_json("dialog", 120)
    except Failed:
        dialogs = []
    if dialogs:
        dialog = dialogs[0]
        SEEN_DIALOGS.append(dialog.get("Type") or "?")
        buttons = dialog.get("Buttons", [])
        yes = next((b for b in buttons if b.get("Slot") == "primary"), None) \
            or next((b for b in buttons if "-primary-" in (b.get("Name") or "")), None)
        if yes and yes.get("Label"):
            fm(f'dialog "{yes["Label"]}"', 120)
    try:
        subs = read_json("sub", 120)
    except Failed as error:
        subs = {}
        if dialogs:
            print(f"       at a {dialogs[0].get('Type')} stop, `sub` was not read: {str(error)[:200]}", flush=True)
    taken = []
    for hurt in subs.get("Injured", []):
        if on := bench_man(subs, keeper_of(subs, hurt)):
            rc = fm(f'sub "{hurt}" "{on}"', 300)[0]
            # Read back: the hurt man is out of the eleven (the game often says yes and does nothing).
            try:
                eleven = [m["Name"] for m in read_json("sub", 120).get("Eleven", [])]
            except Failed as error:
                eleven = [f"not read: {error}"]
            still = any(name == hurt or hurt.endswith(" " + name) for name in eleven)
            taken.append({"hurt": hurt, "on": on, "rc": rc, "still_on": still, "eleven": eleven})
        else:
            taken.append({"hurt": hurt, "on": None, "rc": None, "still_on": True, "eleven": [], "no_substitute": True})
    if again:
        fm(filled(again, kept), 120)
    return taken


# Tweaks by the game's own ids (FM.UI.TacticalTweak), for the score we have against the one we want: level.
STEER = {"ahead": {"TakeABreather", "PressLessAggressively", "PlayOutTheBack"}, "behind": {"ThrowTheKitchenSink", "GkUpForSetPieces"},
         "level": {"LockItDown"}}
KEEPER_OFF = 60  # from this minute a lead takes our goalkeeper off (earlier would lose the tie outright)
PLAYING = ("FirstHalf", "SecondHalf", "ExtraTimeFirstHalf", "ExtraTimeSecondHalf")


def steer(memo):
    """Our side steered towards a level score, for a moment that needs one (extra time, a shootout): ahead, we ease
    off, and from KEEPER_OFF our goalkeeper goes off for an outfield man; behind, everything forward; level late, locked down.
    Read from the status's numbers only; each change made once when the score's state changes."""
    now = read_json("status", 120)
    if now.get("Period") not in PLAYING:
        return
    ours, theirs = (now.get("HomeScore"), now.get("AwayScore"))
    if now.get("HumanIsHome") != "True":
        ours, theirs = theirs, ours
    # At a goal the status comes without its clock for a moment: read as minute 0 it took the lock off (a 1-1 at 90').
    if not now.get("MatchTime"):
        return
    try:
        lead = int(ours) - int(theirs)
        minute = int(now["MatchTime"].split(":")[0])
    except (TypeError, ValueError):
        return
    extra = now["Period"].startswith("ExtraTime")
    side = "ahead" if lead > 0 else "behind" if lead < 0 else "level" if minute >= 65 or extra else None
    wanted = STEER.get(side, set())
    if side == "ahead" and (minute >= KEEPER_OFF or extra) and not memo.get("keeper"):
        memo["keeper"] = True
        subs = read_json("sub", 120)
        keeper = next((m["Name"] for m in subs.get("Eleven", []) if int(m.get("Position") or 0) & KEEPER), None)
        if keeper and (on := bench_man(subs, keeper=False)):
            fm(f'sub "{keeper}" "{on}"', 300)
    if memo.get("side") == side:
        return
    memo["side"] = side
    for tweak in read_json("tweak", 120):
        if tweak.get("Id") in wanted and not tweak.get("WillBeOn"):
            fm(f"tweak {tweak['Id']}", 120)
        elif tweak.get("Id") in set().union(*STEER.values()) - wanted and tweak.get("WillBeOn"):
            fm(f"tweak {tweak['Id']} --off", 120)


def plain_step(number, step, kept):
    """A `run` or a `read` step, its command already filled. Raises Failed with what was wrong."""
    if "run" in step:
        rc, out, err, took = fm(step["run"])
        wanted = step.get("rc", 0)
        if wanted != "any" and ((wanted == "fail" and rc == 0) or (wanted != "fail" and rc != wanted)):
            raise Failed(f"step {number} `{step['run']}`: rc={rc}, wanted {wanted}: {(err or out).strip()[:200]}")
        if "within" in step and took > step["within"]:
            raise Failed(f"step {number} `{step['run']}`: took {took:.0f} s, wanted within {step['within']} s")
        return
    try:
        value = dig(read_json(step["read"]), re.sub(r"\{(\w+)\}", lambda m: str(kept[m.group(1)]), step["path"]))
    except (KeyError, IndexError, ValueError, TypeError, AttributeError) as error:
        if step.get("absent"):
            return  # the step asks for the path not to be there
        raise Failed(f"step {number} `{step['read']}` {step['path']}: not there ({error})") from error
    if step.get("absent"):
        raise Failed(f"step {number} `{step['read']}` {step['path']} is {value!r}, wanted it not there")
    if "keep" in step:
        kept[step["keep"]] = value
    if wrong := holds(step, value, kept):
        raise Failed(f"step {number} `{step['read']}` {step['path']} {wrong}")


def play(test):
    """One pass of a test's steps. Raises Failed with what was wrong; nothing printed is compared."""
    kept = {}
    side = None
    steering = {}
    try:
        for number, step in enumerate(test["steps"], 1):
            if f"run_{LANG_NOW}" in step:
                step = {**step, "run": step[f"run_{LANG_NOW}"]}
            step = {k: filled(v, kept) if k in ("run", "read", "until", "start") else v for k, v in step.items()}
            if "run" in step or "read" in step:
                plain_step(number, step, kept)
            elif "start" in step:
                started = step["start"]
                side = subprocess.Popen(["fm", *shlex.split(step["start"])], stdin=subprocess.DEVNULL, stdout=LOG or subprocess.DEVNULL, stderr=subprocess.STDOUT)
            elif "finish" in step:
                side.wait(timeout=COMMAND_LIMIT)
                # The match command also ends where the match stops for something (a card, an injury, a question):
                # the stop is answered and the match carried on, until the game is out of the match.
                restarts = 0
                while restarts < 12 and read_json("status", 120).get("DuringMatch") == "True":
                    restarts += 1
                    answer_stop(step.get("again"), kept)
                    side = subprocess.Popen(["fm", *shlex.split(started)], stdin=subprocess.DEVNULL, stdout=LOG or subprocess.DEVNULL, stderr=subprocess.STDOUT)
                    side.wait(timeout=COMMAND_LIMIT)
                side = None
            elif "stop" in step:
                side.terminate()
                side.wait(timeout=60)
                side = None
            elif "until" in step:
                limit = time.time() + step.get("seconds", 300)
                restarts = 0
                wrong = "never asked"
                turn = 0
                while time.time() < limit:
                    # The reading first: what the match stopped for (a question) may be what is waited for.
                    try:
                        wrong = holds(step, dig(read_json(step["until"], 120), step["path"]), kept)
                    except (Failed, KeyError, IndexError, ValueError, TypeError, AttributeError) as error:
                        wrong = str(error)
                    if wrong is None:
                        break
                    if side is not None and side.poll() is not None and restarts < 8 \
                            and read_json("status", 120).get("DuringMatch") == "True":
                        # The match stopped for the game's own question (staff advice before kick-off, a question
                        # after a goal, an injury): answer it, play on.
                        restarts += 1
                        answer_stop(step.get("again"), kept)
                        side = subprocess.Popen(["fm", *shlex.split(started)], stdin=subprocess.DEVNULL, stdout=LOG or subprocess.DEVNULL, stderr=subprocess.STDOUT)
                        continue
                    if side is not None and side.poll() is not None:
                        # The match is over and the reading above was the last one.
                        if step.get("moment"):
                            raise Missed(f"step {number} `{step['until']}` {step['path']}: the match ended before it ({wrong})")
                        raise Failed(f"step {number} `{step['until']}` {step['path']} {wrong}, and the match beside it ended")
                    if step.get("steer") and side is not None:
                        try:
                            steer(steering)
                        except Failed:
                            pass  # a reading lost between two moments of the match: the next round asks again
                    if "doing" in step:
                        # A step of our own each round (a conversation of any length: `talk 1` until it is over);
                        # a list is taken in turn, one entry a round, at the step's pace.
                        doing = step["doing"] if isinstance(step["doing"], list) else [step["doing"]]
                        fm(filled(doing[turn % len(doing)], kept), 120)
                        turn += 1
                        if not isinstance(step["doing"], list):
                            continue
                    time.sleep(step.get("every", 1))  # only the pace of asking: what ends the wait is the value
                if wrong is not None:
                    raise Failed(f"step {number} `{step['until']}` {step['path']} {wrong} after {step.get('seconds', 300)} s")
    finally:
        if side is not None and side.poll() is None:
            side.terminate()


def run(args):
    tests = load_list(args.list)
    if args.only:
        wanted = set(args.only.split(","))
        tests = [t for t in tests if t["task"] in wanted or any(w in t["name"] for w in wanted)]
    if problems := problems_in(tests):
        for problem in problems:
            print(f"  ! {problem}")
        print("The list is broken: nothing was run.")
        return 1
    RESULTS.mkdir(parents=True, exist_ok=True)
    stem = pathlib.Path(args.list).stem
    failed = 0
    global LOG
    restore = {}
    global LANG_NOW
    for lang in args.lang.split(","):
        LANG_NOW = lang
        rows = []
        loaded = None
        # The run before is kept beside it: an injury stop once seen only there was lost when a second run wrote over it (T156).
        log_file = RESULTS / f"{stem}-{lang}.log"
        if log_file.exists():
            log_file.replace(log_file.with_suffix(".prev.log"))
        LOG = open(log_file, "w")
        stop_watch = threading.Event()
        threading.Thread(target=watch_memory, args=(RESULTS / f"{stem}-{lang}.memory.tsv", stop_watch), daemon=True).start()
        for test in sorted(tests, key=lambda t: t["at"]):  # one load per scenario where the tests allow it
            passes = []
            missed = []
            deaths = 0
            SEEN_DIALOGS.clear()
            # `tries` bounds the attempts a test with a moment to wait for may take to reach `repeat` passes.
            # A game that died (or never reached the menu) costs no try: DEATHS bounds those on their own.
            tries_left = test.get("tries", test.get("repeat", 1))
            while tries_left > 0:
                tries_left -= 1
                if len([p for p in passes if p is None]) >= test.get("repeat", 1):
                    break
                try:
                    if loaded != test["at"] or test.get("fresh"):
                        print(f"[{lang}] entering {test['at']}: {json.dumps(enter(test['at'], lang))}", flush=True)
                        loaded = test["at"]
                    was = fast_matches() if test.get("fast") else {}
                    try:
                        play(test)
                    finally:
                        restore.update(was)
                        put_back(restore)
                    passes.append(None)
                except Fatal as error:
                    # The game's own fatal error (T148): closed at once and started again by the next entry.
                    fm("stop --no-save", 120)
                    deaths += 1
                    error = f"{error}{keep_log(f'{stem}-{lang}-death{deaths}.Player.log')}"
                    missed.append(f"the game hit a fatal error: {error}")
                    loaded = None
                    print(f"[{lang}] the game hit a fatal error in {test['task']} ({deaths}): {error}", flush=True)
                    if deaths >= DEATHS:
                        break
                    tries_left += 1
                    continue
                except Missed as error:
                    missed.append(str(error))
                    loaded = None
                    print(f"[{lang}] missed {test['task']}: {error}", flush=True)
                except Failed as error:
                    if fm("status --json", 120)[0] == NOT_RUNNING and deaths < DEATHS:
                        # The game died under the try (T92): not the test's failure. Started again by the next entry.
                        deaths += 1
                        error = f"{error}{keep_log(f'{stem}-{lang}-death{deaths}.Player.log')}"
                        missed.append(f"the game died: {error}")
                        loaded = None
                        print(f"[{lang}] the game died in {test['task']} ({deaths}): {error}", flush=True)
                        tries_left += 1
                        continue
                    passes.append(str(error))
                    # Whatever it left behind must not fail the next test: that one starts fresh.
                    loaded = None
                    break  # a task that fails moves to the next round: no more of its time this one
            bad = [p for p in passes if p]
            failed += bool(bad)
            mark = "FAIL" if bad else "pass" if passes else "MISSED"
            print(f"[{lang}] {mark} {test['task']} {test['name']} ({len(passes) - len(bad)}/{len(passes)}"
                  + (f", {len(missed)} without the moment" if missed else "") + ")", flush=True)
            for problem in bad[:2]:
                print(f"       {problem}", flush=True)
            extra = (f"\n  - the game died {deaths} ×" if deaths else "") \
                + (f"\n  - questions answered in the match: {', '.join(SEEN_DIALOGS)}" if SEEN_DIALOGS else "")
            if extra:
                print(extra.replace("\n  - ", "\n       "), flush=True)
            rows.append(f"- {mark} {test['task']} {test['name']} ({len(passes) - len(bad)}/{len(passes)}"
                        + (f", {len(missed)} without the moment" if missed else "") + ")" + "".join(f"\n  - {p}" for p in bad) + extra)
        report = RESULTS / f"{stem}-{lang}.md"
        report.write_text(f"# {stem}, {lang}, {datetime.datetime.now():%Y-%m-%d %H:%M}\n\n" + "\n".join(rows) + "\n")
        stop_watch.set()
        print(f"[{lang}] written: {report}", flush=True)
    if restore:
        print(f"NOT PUT BACK, a match is still on: {restore}. When it ends: fm settings \"<name>\" <value>")
    print(f"{len(tests)} tests, {failed} failed somewhere.")
    return 1 if failed else 0


# ---- the hunt: matches played and watched for every rare moment at once


HUNT_KEYS = {"task", "name", "probe", "seen", "is", "not", "quota", "checks", "steps"}


def hunt_problems(spec):
    """The hunt list's own shape, told without the game."""
    found = []
    tasks = {m.get("task") for m in spec.get("moment", [])}
    for scenario in spec.get("scenario", []):
        if not (STORE / f"{scenario.get('at')}.fm").exists():
            found.append(f"no scenario '{scenario.get('at')}'")
        for task in scenario.get("serves", []):
            if task not in tasks:
                found.append(f"scenario {scenario.get('at')} serves {task}, which no moment names")
    for moment in spec.get("moment", []):
        where = f"moment {moment.get('task')}"
        if set(moment) - HUNT_KEYS:
            found.append(f"{where}: unknown {sorted(set(moment) - HUNT_KEYS)}")
        if moment.get("probe") not in ("dugout apply", "status", "injury"):
            found.append(f"{where}: probe is `dugout apply`, `status` or `injury`")
        if moment.get("probe") != "injury" and ("seen" not in moment or not {"is", "not"} & set(moment)):
            found.append(f"{where}: seen = <path> with is / not")
        if not any(moment.get("task") in s.get("serves", []) for s in spec.get("scenario", [])):
            found.append(f"{where}: no scenario serves it")
        kept = set()
        for check in moment.get("checks", []):
            for key in ("same_as", "differs_from"):
                if key in check and check[key] not in kept:
                    found.append(f"{where}: check {key} '{check[key]}' was not kept before")
            if "keep" in check:
                kept.add(check["keep"])
        for step in moment.get("steps", []):
            if not {"run", "read"} & set(step):
                found.append(f"{where}: a moment's steps are run / read only")
    return found


def hunt_check(moment, data):
    """The moment's checks on the reading that showed it. None when all hold, else what was wrong."""
    kept = {}
    for check in moment.get("checks", []):
        try:
            value = dig(data, check["path"])
        except (KeyError, IndexError, ValueError, TypeError, AttributeError) as error:
            return f"{check['path']}: not there ({error})"
        if "keep" in check:
            kept[check["keep"]] = value
        if wrong := holds(check, value, kept):
            return f"{check['path']} {wrong}"
    return None


class Hunt:
    def __init__(self, spec, lang, stem):
        self.moments = spec["moment"]
        self.lang = lang
        self.passes = {m["task"]: 0 for m in self.moments}
        self.tries = {m["task"]: [] for m in self.moments}
        self.evidence = open(RESULTS / f"{stem}-{lang}.evidence.jsonl", "a")
        self.cards = 0
        self.unread = []
        self.refused = {}

    def need(self, task):
        moment = next(m for m in self.moments if m["task"] == task)
        return self.passes[task] < moment.get("quota", {}).get(self.lang, 0)

    def open_tasks(self):
        return [m["task"] for m in self.moments if self.need(m["task"])]

    def note(self, task, wrong, data, minute):
        entry = {"at": datetime.datetime.now().isoformat(timespec="seconds"), "lang": self.lang, "task": task,
                 "pass": wrong is None, "wrong": wrong, "minute": minute, "data": data}
        self.evidence.write(json.dumps(entry, ensure_ascii=False) + "\n")
        self.evidence.flush()
        if wrong is None:
            self.passes[task] += 1
        self.tries[task].append(wrong)
        quota = next(m for m in self.moments if m["task"] == task).get("quota", {}).get(self.lang, 0)
        print(f"[{self.lang}] CAUGHT {task} {'pass' if wrong is None else 'FAIL'} ({self.passes[task]}/{quota}) at {minute}'"
              + (f": {wrong}" if wrong else ""), flush=True)

    def seen(self, probe, data, minute):
        """Each moment of this probe the reading shows, checked; a moment met is checked even when its quota is full."""
        for moment in self.moments:
            if moment["probe"] != probe or moment.get("quota", {}).get(self.lang, 0) == 0:
                continue
            try:
                value = dig(data, moment["seen"])
            except (KeyError, IndexError, ValueError, TypeError, AttributeError):
                continue
            if holds(moment, value, {}) is not None:
                continue
            wrong = hunt_check(moment, data)
            if wrong is None and moment.get("steps"):
                kept = {}
                try:
                    for number, step in enumerate(moment["steps"], 1):
                        step = {k: filled(v, kept) if k in ("run", "read") else v for k, v in step.items()}
                        plain_step(number, step, kept)
                except Failed as error:
                    wrong = str(error)
                    fm("resume", 120)  # a step that failed between a pause and its resume must not leave the match held
            self.note(moment["task"], wrong, data, minute)

    def injuries(self, taken, minute):
        for man in taken:
            wrong = None if not man["still_on"] and man["rc"] == 0 else \
                "no substitute fitting him" if man.get("no_substitute") else f"`sub` rc={man['rc']}, still in the eleven: {man['still_on']}"
            for moment in self.moments:
                if moment["probe"] == "injury" and moment.get("quota", {}).get(self.lang, 0):
                    self.note(moment["task"], wrong, man, minute)

    def report(self):
        rows = []
        for moment in self.moments:
            task = moment["task"]
            quota = moment.get("quota", {}).get(self.lang, 0)
            if not quota:
                continue
            bad = [t for t in self.tries[task] if t]
            mark = "FAIL" if bad else "pass" if self.passes[task] >= quota else "MISSED" if not self.tries[task] else "part"
            rows.append(f"- {mark} {task} {moment['name']} ({self.passes[task]}/{quota}, met {len(self.tries[task])} ×)"
                        + "".join(f"\n  - {b}" for b in bad[:3]))
        return rows


def take_card(hunt, minute):
    """Every card is taken: what it changed says which moment it was, by the game's ids."""
    rc, out, _, _ = fm("dugout apply --json --play-on", 180)
    if not out.lstrip().startswith("{"):
        return
    data = json.loads(out)
    if data.get("Before"):
        return  # the staff's advice before kick-off: answered, no card of the match
    if not data.get("Done"):
        # A card the game would not take stays open, and the match stops at it again and again: twice is enough,
        # then it is put away by its place on the list (the list's own Minute and CanDo, no words).
        key = (data.get("Minute"), data.get("Text"))
        hunt.refused[key] = hunt.refused.get(key, 0) + 1
        if hunt.refused[key] >= 2:
            try:
                listed = read_json("dugout", 120)
                at = next(i for i, e in enumerate(listed, 1) if e.get("CanDo") and (e.get("Minute"), e.get("Text")) == key)
                fm(f"dugout {at} dismiss", 180)
            except (Failed, StopIteration):
                pass
    hunt.cards += 1
    if not data.get("Changes"):
        hunt.unread.append({"minute": minute, "rc": rc, "data": data})
        print(f"[{hunt.lang}] a card taken without a change read back (rc={rc}) at {minute}': {out[:300]}", flush=True)
    hunt.seen("dugout apply", data, minute)


def watch_match(scenario, hunt, limit):
    """One match played to its end, every stop answered, every card applied and each moment checked as it comes."""
    # The match stops itself at a card that can be applied (paused), so a card is taken at any speed.
    started = "match --play --no-talk --stop-at-advice"
    side = subprocess.Popen(["fm", *shlex.split(started)], stdin=subprocess.DEVNULL, stdout=LOG or subprocess.DEVNULL, stderr=subprocess.STDOUT)
    steering = {}
    once = set()
    restarts = 0
    end = time.time() + limit
    minute = "?"
    try:
        while time.time() < end:
            if side.poll() is not None:
                now = read_json("status", 120)
                if now.get("DuringMatch") != "True":
                    return "ended"
                if restarts >= 120:
                    return "stuck at its stops"
                restarts += 1
                hunt.injuries(answer_stop(None, {}), minute)
                # The card taken while the match is held (played on first, it closed within seconds at full speed:
                # 14 Nov); --play-on plays the match on once it is pressed, for a team instruction reaches the
                # match's tactic only at a dead ball. The staff's advice before kick-off is taken here too.
                take_card(hunt, minute)
                side = subprocess.Popen(["fm", *shlex.split(started)], stdin=subprocess.DEVNULL, stdout=LOG or subprocess.DEVNULL, stderr=subprocess.STDOUT)
                continue
            try:
                now = read_json("status", 120)
            except Failed:
                time.sleep(5)
                continue
            minute = (now.get("MatchTime") or "?").split(":")[0]
            if now.get("Period") in PLAYING:
                # A moment read off the status (extra time) is taken once a match.
                period = now.get("Period")
                if period not in once:
                    before = sum(hunt.tries[t].__len__() for t in hunt.tries)
                    hunt.seen("status", now, minute)
                    if sum(hunt.tries[t].__len__() for t in hunt.tries) != before:
                        once.add(period)
                if scenario.get("steer"):
                    try:
                        steer(steering)
                    except Failed:
                        pass
                take_card(hunt, minute)
            time.sleep(scenario.get("every", 8))  # only the pace of looking: what is seen decides
        return "too long"
    finally:
        if side.poll() is None:
            side.terminate()
            side.wait(timeout=60)


def hunt_run(args):
    with open(args.list, "rb") as file:
        spec = tomllib.load(file)
    if problems := hunt_problems(spec):
        for problem in problems:
            print(f"  ! {problem}")
        print("The hunt list is broken: nothing was run.")
        return 1
    RESULTS.mkdir(parents=True, exist_ok=True)
    stem = pathlib.Path(args.list).stem
    global LOG, LANG_NOW
    for lang in args.lang.split(","):
        LANG_NOW = lang
        log_file = RESULTS / f"{stem}-{lang}.log"
        if log_file.exists():
            log_file.replace(log_file.with_suffix(f".{int(time.time())}.log"))
        LOG = open(log_file, "w")
        stop_watch = threading.Event()
        threading.Thread(target=watch_memory, args=(RESULTS / f"{stem}-{lang}.memory.tsv", stop_watch), daemon=True).start()
        hunt = Hunt(spec, lang, stem)
        SEEN_DIALOGS.clear()
        deadline = time.time() + args.hours * 3600
        matches = deaths = 0
        turn = 0
        ends = []
        while time.time() < deadline and hunt.open_tasks() and deaths < DEATHS * 2:
            serving = [sc for sc in spec["scenario"] if set(sc.get("serves", [])) & set(hunt.open_tasks())]
            scenario = serving[turn % len(serving)]
            turn += 1
            restore = {}
            try:
                print(f"[{lang}] match {matches + 1} in {scenario['at']} for {sorted(set(scenario['serves']) & set(hunt.open_tasks()))}: "
                      f"{json.dumps(enter(scenario['at'], lang))}", flush=True)
                if scenario.get("fast"):
                    restore = fast_matches()
                for name, value in scenario.get("speeds", {}).items():
                    restore.setdefault(name, read_json(f'settings "{name}"')["Value"])
                    must(f'settings "{name}" "{value}"')
                matches += 1
                how = watch_match(scenario, hunt, scenario.get("minutes", 100) * 60)
                ends.append(how)
                print(f"[{lang}] match {matches} {how}; open: {hunt.open_tasks()}; cards {hunt.cards}", flush=True)
            except Fatal as error:
                deaths += 1
                fm("stop --no-save", 120)
                print(f"[{lang}] the game hit a fatal error ({deaths}): {error}{keep_log(f'{stem}-{lang}-death{deaths}.Player.log')}", flush=True)
            except Failed as error:
                if fm("status --json", 120)[0] == NOT_RUNNING:
                    deaths += 1
                    print(f"[{lang}] the game died ({deaths}): {error}{keep_log(f'{stem}-{lang}-death{deaths}.Player.log')}", flush=True)
                else:
                    print(f"[{lang}] the match could not be played: {error}", flush=True)
                    deaths += 1  # bounded the same way: a hunt that cannot start a match must not spin all night
            finally:
                if restore:
                    put_back(restore)
        rows = hunt.report()
        extra = [f"- {matches} matches ({', '.join(ends)}), {hunt.cards} cards taken, the game died {deaths} ×",
                 f"- questions answered in the matches: {', '.join(sorted(set(SEEN_DIALOGS))) or 'none'}"]
        extra += [f"- a card taken without a change read back at {u['minute']}': {json.dumps(u['data'], ensure_ascii=False)[:300]}" for u in hunt.unread]
        report = RESULTS / f"{stem}-{lang}.md"
        report.write_text(f"# {stem}, {lang}, {datetime.datetime.now():%Y-%m-%d %H:%M}\n\n" + "\n".join(rows + extra) + "\n")
        stop_watch.set()
        print("\n".join(rows + extra), flush=True)
        print(f"[{lang}] written: {report}", flush=True)
    return 0


def hunt_dry(args):
    with open(args.list, "rb") as file:
        spec = tomllib.load(file)
    found = hunt_problems(spec)
    for moment in spec.get("moment", []):
        for step in moment.get("steps", []):
            command = step.get("run") or step.get("read")
            word = shlex.split(command)[0]
            rc, out, _, _ = fm(f"{word} --help", 30)
            if rc == 64 and not out.strip():
                found.append(f"{moment['task']}: `{word}` has no help; is it a command?")
    for problem in found:
        print(f"  ! {problem}")
    print(f"{len(spec.get('moment', []))} moments, {len(spec.get('scenario', []))} scenarios, {len(found)} problems.")
    return 1 if found else 0


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="what", required=True)
    sub.add_parser("scenarios").set_defaults(go=scenarios)
    one = sub.add_parser("make")
    one.add_argument("name")
    one.add_argument("moment")
    one.set_defaults(go=make)
    one = sub.add_parser("forward")
    one.add_argument("date")
    one.add_argument("--answer", action="store_true", help="to reach a moment: opens what needs no choice, stops at a choice")
    one.add_argument("--past", action="append", help="a message id (inbox --open --json) the walk has been stopped at and goes past now")
    one.add_argument("--catch", action="store_true", help="keep a scenario at each kind of moment not kept before (caught.json)")
    one.add_argument("--pool", type=int, default=0, help="keep the last N saves (each week, each match day) in the pool, and a log of the walk")
    one.add_argument("--hold", action="append", help="a player whose bids are not refused (a promise made to him); repeatable")
    one.set_defaults(go=forward)
    one = sub.add_parser("keep")
    one.add_argument("copy")
    one.add_argument("name")
    one.add_argument("moment")
    one.set_defaults(go=keep)
    one = sub.add_parser("enter")
    one.add_argument("name")
    one.add_argument("--lang", choices=LANGUAGES)
    one.set_defaults(go=enter_command)
    one = sub.add_parser("dry")
    one.add_argument("list")
    one.set_defaults(go=dry)
    one = sub.add_parser("run")
    one.add_argument("list")
    one.add_argument("--lang", default="en,tr")
    one.add_argument("--only")
    one.set_defaults(go=run)
    one = sub.add_parser("hunt", help="matches watched for every rare moment at once, until each moment's quota is met")
    one.add_argument("list")
    one.add_argument("--lang", default="en,tr")
    one.add_argument("--hours", type=float, default=6, help="upper bound for each language")
    one.add_argument("--dry", action="store_true", help="check the list only")
    one.set_defaults(go=lambda a: hunt_dry(a) if a.dry else hunt_run(a))
    one = sub.add_parser("home")
    one.add_argument("save")
    one.set_defaults(go=home)
    args = parser.parse_args()
    try:
        return args.go(args)
    except Failed as error:
        print(f"error: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
