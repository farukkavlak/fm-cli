#!/usr/bin/env python3
"""The one live test tool: scenario saves and rounds of tests.

  fm-round.py scenarios                       the scenario saves kept
  fm-round.py make <scenario> "<moment>"      keep the game's present moment as a scenario
  fm-round.py forward <yyyy-mm-dd> [--answer] days forward, matches simulated, until then or until the game asks;
                                              --answer: the first answer to whatever it asks, to reach a moment
  fm-round.py enter <scenario> [--lang en]    a fresh copy of it, loaded, in that language
  fm-round.py dry <list.toml>                 check a test list; changes nothing in the game
  fm-round.py run <list.toml> [--lang en,tr] [--only T106]
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
  run = "<command>"        rc = 0 (default), a number, "fail" (any non-zero) or "any"
  read = "<command>"       run with --json; path = "A.B.0.C", "#" is a list's length, "?Field" keeps a list's entries with it set. Then is / not / same_as /
                           differs_from / present = true, and keep = "<name>" remembers the value
  until = "<command>"      a read asked again until it holds; seconds = the upper bound (default 300), every = the pace of asking (default 1)
  start = "<command>"      runs beside the next steps (a match); `finish = true` waits for it to end
  stop = true              ends what `start` began, the match staying where it is (the next test starts fresh)
  live = true              on a read: it can only be read at that moment, so a dry run does not ask it
  {name} in a command      the value kept under that name

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
import tomllib

ROOT = pathlib.Path(__file__).resolve().parent.parent
STORE = pathlib.Path(os.environ.get("FM_SCENARIOS", pathlib.Path.home() / "fm27_bep" / "scenarios"))
RESULTS = ROOT / "tests" / "live" / "results"
PREFIX = "scn-"
LANGUAGES = {"en": ("english", "English"), "tr": ("turkish", "Türkçe")}
COMMAND_LIMIT = 1800


class Failed(Exception):
    pass


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
        LOG.write(f"{datetime.datetime.now():%H:%M:%S} fm {command} rc={result[0]} {result[3]:.1f}s\n{result[1][:1500]}{result[2][:500]}\n")
        LOG.flush()
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
    that value. Raises KeyError when the path is not there."""
    for part in path.split(".") if path else []:
        if part == "#":
            data = len(data)
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
    """Only to reach a moment: the first answer to whatever is open, each one printed.
    True only when the game's state moved, so an answer the game ignores cannot loop forever."""
    _, shown, _, _ = fm("status")
    moved = lambda: fm("status")[1] != shown
    rc, out, _, _ = fm("talk --json")
    if rc == 0 and out.lstrip().startswith("{") and json.loads(out).get("Answers"):
        print(f"  talk: {json.loads(out)['Says'][:1]}", flush=True)
        if fm("talk 1")[0] == 0 and moved():
            return True
    names = list(dict.fromkeys(re.findall(r'reply "([^"]+)"', shown) + [line.strip() for line in shown.splitlines() if line.startswith("  ")]))
    for name in names:
        print(f"  reply: {name}", flush=True)
        if fm(f'reply "{name}"')[0] == 0:
            fm(f'reply "{name}" 1')  # a message with no choices is answered by opening it
            if moved():
                return True
    # Screens a message cannot answer: squad numbers at the season's start, the set-piece wizard of a new job.
    for step in ("numbers --auto", "setpieces new", "next --skip"):
        if fm(step)[0] == 0 and moved():
            print(f"  {step}", flush=True)
            return True
    return False


def forward(args):
    """Days forward through the tool, matches simulated, up to a date. Stops where the game asks something.
    With --answer the first answers can lose the job (a board meeting answered blindly, 1 Mar 2030): while
    employed it saves once a week, and stops the moment the job is gone, so the week's save is the way back."""
    employed = fm("squad")[0] == 0
    saved_week = None
    answered = False
    while True:
        now = state()
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
        if now.get("Date", "") >= args.date:
            print(f"Reached {now['Date']} ({now['State']}).")
            return 0
        before = now.get("Date"), now.get("Clock")
        match_day = now["State"] in ("Tactics", "SubmitTeam", "SquadNumbers")
        rc, out, err, _ = fm("match --instant" if match_day else "next")
        stopped = rc != 0 or (not match_day and (state().get("Date"), state().get("Clock")) == before)
        if stopped and not match_day and state()["State"] in ("Tactics", "SubmitTeam", "SquadNumbers"):
            continue  # opening the last message led on to the match: not a stop
        if stopped:
            if args.answer and match_day and fm("tactic pick")[0] == 0:
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
READ_KEYS = {"read", "until", "path", "keep", "is", "not", "same_as", "differs_from", "present", "seconds", "every", "live", "note"}


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
            if kind == "run" and set(step) - {"run", "rc", "note"}:
                found.append(f"{where} step {number}: unknown {sorted(set(step) - {'run', 'rc', 'note'})}")
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
                    except (Failed, KeyError, IndexError, ValueError, TypeError) as error:
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


def play(test):
    """One pass of a test's steps. Raises Failed with what was wrong; nothing printed is compared."""
    kept = {}
    side = None
    try:
        for number, step in enumerate(test["steps"], 1):
            step = {k: filled(v, kept) if k in ("run", "read", "until", "start") else v for k, v in step.items()}
            if "run" in step:
                rc, out, err, _ = fm(step["run"])
                wanted = step.get("rc", 0)
                if wanted != "any" and ((wanted == "fail" and rc == 0) or (wanted != "fail" and rc != wanted)):
                    raise Failed(f"step {number} `{step['run']}`: rc={rc}, wanted {wanted}: {(err or out).strip()[:200]}")
            elif "start" in step:
                side = subprocess.Popen(["fm", *shlex.split(step["start"])], stdin=subprocess.DEVNULL, stdout=LOG or subprocess.DEVNULL, stderr=subprocess.STDOUT)
            elif "finish" in step:
                side.wait(timeout=COMMAND_LIMIT)
                side = None
            elif "stop" in step:
                side.terminate()
                side.wait(timeout=60)
                side = None
            elif "read" in step:
                try:
                    value = dig(read_json(step["read"]), step["path"])
                except (KeyError, IndexError, ValueError, TypeError) as error:
                    raise Failed(f"step {number} `{step['read']}` {step['path']}: not there ({error})") from error
                if "keep" in step:
                    kept[step["keep"]] = value
                if wrong := holds(step, value, kept):
                    raise Failed(f"step {number} `{step['read']}` {step['path']} {wrong}")
            elif "until" in step:
                limit = time.time() + step.get("seconds", 300)
                wrong = "never asked"
                while time.time() < limit:
                    if side is not None and side.poll() is not None:
                        raise Failed(f"step {number} `{step['until']}` {step['path']} {wrong}, and the match beside it ended")
                    try:
                        wrong = holds(step, dig(read_json(step["until"], 120), step["path"]), kept)
                    except (Failed, KeyError, IndexError, ValueError, TypeError) as error:
                        wrong = str(error)
                    if wrong is None:
                        break
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
        tests = [t for t in tests if t["task"] in wanted]
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
    for lang in args.lang.split(","):
        rows = []
        loaded = None
        LOG = open(RESULTS / f"{stem}-{lang}.log", "w")
        for test in sorted(tests, key=lambda t: t["at"]):  # one load per scenario where the tests allow it
            passes = []
            for _ in range(test.get("repeat", 1)):
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
                except Failed as error:
                    passes.append(str(error))
                    # Whatever it left behind must not fail the next test: that one starts fresh.
                    loaded = None
                    break  # a task that fails moves to the next round: no more of its time this one
            bad = [p for p in passes if p]
            failed += bool(bad)
            mark = "pass" if not bad else "FAIL"
            print(f"[{lang}] {mark} {test['task']} {test['name']} ({len(passes) - len(bad)}/{len(passes)})", flush=True)
            for problem in bad[:2]:
                print(f"       {problem}", flush=True)
            rows.append(f"- {mark} {test['task']} {test['name']} ({len(passes) - len(bad)}/{len(passes)})" + "".join(f"\n  - {p}" for p in bad))
        report = RESULTS / f"{stem}-{lang}.md"
        report.write_text(f"# {stem}, {lang}, {datetime.datetime.now():%Y-%m-%d %H:%M}\n\n" + "\n".join(rows) + "\n")
        print(f"[{lang}] written: {report}", flush=True)
    if restore:
        print(f"NOT PUT BACK, a match is still on: {restore}. When it ends: fm settings \"<name>\" <value>")
    print(f"{len(tests)} tests, {failed} failed somewhere.")
    return 1 if failed else 0


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
    one.add_argument("--answer", action="store_true", help="to reach a moment: the first answer to whatever the game asks, printed")
    one.set_defaults(go=forward)
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
