#!/usr/bin/env python3
"""
Deterministic mock match data for the visualization catalog.

The shape is the same as the plugin's `/radar` + `/state` capture, with a little
extra metadata for the broadcast/analytics panels.  No FM, no game, no network:
this is what lets the catalog be rendered and polished quickly.

    frame()          a still at 67:12 (ARS 2-1 CHE)
    goal_frame()     the same match a second before the third goal
    goal_sequence()  a short sequence for replay / particle experiments
"""

from __future__ import annotations

import math
from typing import Any

HOME = {
    "code": "ARS",
    "name": "Arsenal",
    "outfield": "e30613",
    "text": "ffffff",
    "keeper": "f5c518",
}
AWAY = {
    "code": "CHE",
    "name": "Chelsea",
    "outfield": "034694",
    "text": "ffffff",
    "keeper": "101010",
}

# number, name, x, y, outfield/keeper, rotation
HOME_XI = [
    (22, "Raya", 0.06, 0.50, "keeper", 0),
    (4, "White", 0.22, 0.15, "outfield", 82),
    (6, "Gabriel", 0.20, 0.38, "outfield", 95),
    (2, "Saliba", 0.20, 0.62, "outfield", 110),
    (35, "Zinchenko", 0.24, 0.85, "outfield", 70),
    (5, "Partey", 0.40, 0.45, "outfield", 120),
    (8, "Ødegaard", 0.55, 0.35, "outfield", 135),
    (41, "Rice", 0.48, 0.62, "outfield", 100),
    (7, "Saka", 0.76, 0.22, "outfield", 150),
    (9, "Jesus", 0.80, 0.46, "outfield", 170),
    (11, "Martinelli", 0.70, 0.78, "outfield", 190),
]

AWAY_XI = [
    (1, "Sánchez", 0.94, 0.50, "keeper", 180),
    (24, "James", 0.88, 0.20, "outfield", 230),
    (6, "Silva", 0.90, 0.40, "outfield", 250),
    (26, "Colwill", 0.90, 0.60, "outfield", 260),
    (3, "Cucurella", 0.86, 0.80, "outfield", 300),
    (25, "Caicedo", 0.78, 0.48, "outfield", 280),
    (8, "Enzo", 0.72, 0.30, "outfield", 300),
    (10, "Mudryk", 0.70, 0.68, "outfield", 330),
    (11, "Madueke", 0.60, 0.20, "outfield", 20),
    (15, "Jackson", 0.58, 0.50, "outfield", 30),
    (20, "Palmer", 0.62, 0.78, "outfield", 40),
]

HOME_SUBS = [
    (30, "Turner", "keeper"),
    (18, "Tomiyasu", "outfield"),
    (20, "Jorginho", "outfield"),
    (19, "Trossard", "outfield"),
    (24, "Nelson", "outfield"),
    (14, "Nketiah", "outfield"),
    (21, "Vieira", "outfield"),
]
AWAY_SUBS = [
    (13, "Bettinelli", "keeper"),
    (27, "Gusto", "outfield"),
    (5, "Badiashile", "outfield"),
    (23, "Gallagher", "outfield"),
    (7, "Sterling", "outfield"),
    (19, "Broja", "outfield"),
    (17, "Chukwuemeka", "outfield"),
]

STATS = {
    "possession": (62, 38),
    "shots": (12, 8),
    "on_target": (6, 4),
    "xg": (1.84, 0.92),
    "corners": (5, 3),
    "fouls": (7, 9),
}

EVENTS = [
    ("23'", "GOAL", "Saka", "Ødegaard assists; Arsenal lead."),
    ("41'", "CARD", "Caicedo", "Yellow card for a late challenge."),
    ("58'", "GOAL", "Palmer", "Chelsea level with a low finish."),
    ("62'", "CARD", "Rice", "Yellow card."),
    ("66'", "SUB", "Trossard", "On for Martinelli."),
    ("67'", "GOAL", "Ødegaard", "Saka cuts it back; Arsenal lead again."),
]

XG = [
    (0, 0.00, 0.00),
    (10, 0.08, 0.04),
    (23, 0.62, 0.10),
    (35, 0.95, 0.22),
    (45, 1.10, 0.30),
    (58, 1.25, 0.78),
    (62, 1.35, 0.82),
    (67, 1.84, 0.92),
]

# from player index to player index, count
PASSES = [
    (6, 7, 8), (7, 6, 7), (6, 8, 5), (8, 9, 4), (5, 6, 6),
    (5, 7, 4), (4, 6, 3), (9, 10, 3), (2, 5, 4), (3, 5, 3),
    (16, 24, 5), (18, 16, 4), (19, 18, 3), (20, 18, 4),
]


def _player(number, name, x, y, kit, text, rot=0.0, sub=False, team=0):
    return {
        "n": number, "name": name, "x": round(x, 4), "y": round(y, 4),
        "rot": rot, "h": 0, "kit": kit, "txt": text, "sub": sub, "team": team,
    }


def _xi(rows, team, team_info):
    out = []
    for i, (number, name, x, y, kind, rot) in enumerate(rows):
        kit = team_info["keeper"] if kind == "keeper" else team_info["outfield"]
        out.append(_player(number, name, x, y, kit, team_info["text"], rot, False, team))
    return out


def _subs(rows, team, team_info, first_index):
    out = []
    for i, (number, name, kind) in enumerate(rows):
        kit = team_info["keeper"] if kind == "keeper" else team_info["outfield"]
        x = 0.36 + (i % 4) * 0.06
        y = 1.04 + (i // 4) * 0.03
        out.append(_player(number, name, x, y, kit, team_info["text"], 0, True, team))
    return out


def _players(goal=False):
    home = _xi(HOME_XI, 0, HOME)
    away = _xi(AWAY_XI, 1, AWAY)
    if goal:
        # The move has reached the box: Saka wide, Jesus and Ødegaard arriving.
        by_name = {p["name"]: p for p in home + away}
        moves = {
            "Saka": (0.91, 0.30), "Jesus": (0.93, 0.48), "Ødegaard": (0.80, 0.40),
            "Martinelli": (0.84, 0.72), "Rice": (0.62, 0.60), "Partey": (0.54, 0.44),
            "Sánchez": (0.95, 0.50), "Silva": (0.94, 0.42), "Colwill": (0.94, 0.60),
            "James": (0.90, 0.24), "Caicedo": (0.86, 0.50), "Enzo": (0.78, 0.34),
        }
        for name, (x, y) in moves.items():
            if name in by_name:
                by_name[name]["x"], by_name[name]["y"] = x, y
    return home + _subs(HOME_SUBS, 0, HOME, 11) + away + _subs(AWAY_SUBS, 1, AWAY, 11)


def frame(goal: bool = False) -> dict[str, Any]:
    home_xi = _xi(HOME_XI, 0, HOME)
    away_xi = _xi(AWAY_XI, 1, AWAY)
    players = _players(goal=goal)
    ball = {
        "x": 0.82 if not goal else 0.965,
        "y": 0.42 if not goal else 0.49,
        "rot": 168.0 if not goal else 182.0,
        "h": 0.02 if not goal else 0.85,
    }
    state = {
        "MatchTime": "67:12" if not goal else "67:14",
        "HomeScore": "2",
        "AwayScore": "1",
        "Period": "SecondHalf",
        "HumanIsHome": "True",
        "Playing": "True",
        "Commentary": (
            "Ødegaard slides it through to Saka on the right…"
            if not goal
            else "GOAL! Ødegaard finishes it off!"
        ),
    }
    return {
        "meta": {
            "home": HOME,
            "away": AWAY,
            "competition": "Premier League",
            "stats": STATS,
            "events": EVENTS,
            "xg": XG,
            "passes": PASSES,
            "player_cards": [
                {"name": "Ødegaard", "team": 0, "number": 8, "key_passes": 3, "xa": 0.4, "rating": 8.1},
                {"name": "Palmer", "team": 1, "number": 20, "shots": 2, "xg": 0.3, "rating": 7.4},
            ],
        },
        "radar": {
            "ball": ball,
            "players": players,
            "count": len(players),
        },
        "state": state,
    }


def goal_sequence(n: int = 24) -> list[dict[str, Any]]:
    """A short move from Ødegaard's pass to the finish; 10 Hz-ish data."""
    start = frame(goal=False)
    end = frame(goal=True)
    out = []
    for i in range(n):
        t = i / max(1, n - 1)
        ease = t * t * (3 - 2 * t)
        a = start["radar"]
        b = end["radar"]
        ball = {
            "x": a["ball"]["x"] + (b["ball"]["x"] - a["ball"]["x"]) * ease,
            "y": a["ball"]["y"] + (b["ball"]["y"] - a["ball"]["y"]) * ease
            + 0.04 * math.sin(t * math.pi * 3),
            "h": a["ball"]["h"] + (b["ball"]["h"] - a["ball"]["h"]) * max(0, (t - 0.7) / 0.3),
            "rot": 168 + 20 * t,
        }
        players = []
        for pa, pb in zip(a["players"], b["players"]):
            players.append(
                dict(
                    pa,
                    x=pa["x"] + (pb["x"] - pa["x"]) * ease,
                    y=pa["y"] + (pb["y"] - pa["y"]) * ease,
                )
            )
        frame_out = {
            "meta": start["meta"],
            "radar": {"ball": ball, "players": players, "count": len(players)},
            "state": dict(
                start["state"],
                MatchTime=f"66:{50 + i:02d}" if 50 + i < 60 else f"67:{50 + i - 60:02d}",
                Commentary="Ødegaard plays it into Saka…" if t < 0.75 else "GOAL! Ødegaard!",
            ),
        }
        out.append(frame_out)
    return out


if __name__ == "__main__":
    import json
    import sys

    data = frame(goal="--goal" in sys.argv)
    json.dump(data, sys.stdout, ensure_ascii=False, indent=2)
    print()
