"""
Replay viewer: step through a battle in the terminal, from its logs alone.

It never runs the battle core. It reads the event log (one row per event) and the round
snapshots (the whole board at the start of each round), and rebuilds the board by applying
events. Change it freely; nothing here can affect the rules.

Usage:
    python viewer/replay.py logs/first-goblins-1.events.csv
    python viewer/replay.py logs/first-goblins-1            (the shared start of the file names)

Keys (with the readchar package installed):
    right / left    next / previous event        up / down   next / previous turn
    r               jump to a round               h           heights on or off
    d               danger zone on or off         space       play the current turn
    p               play to the end               q           quit
Without readchar, type the same letters and press Enter ("n" and "b" for next and back).
"""

from __future__ import annotations

import csv
import json
import sys
import time
from copy import deepcopy
from pathlib import Path

from rich.console import Console, Group
from rich.panel import Panel
from rich.table import Table
from rich.text import Text

try:
    import readchar
except ImportError:  # the viewer still works, with typed commands
    readchar = None

# Background shades from no ground (black) to very high ground (light grey), as in the C# render.
HEIGHT_BG = ["grey3", "grey23", "grey35", "grey50", "grey70"]
PARTY_STYLE = "bold bright_cyan"
ENEMY_STYLE = "bold bright_red"


# ---------------------------------------------------------------------------
# Loading
# ---------------------------------------------------------------------------

def load(path_arg: str) -> tuple[list[dict], list[dict]]:
    base = path_arg
    for suffix in (".events.csv", ".snapshots.jsonl", ".inputs.jsonl"):
        if base.endswith(suffix):
            base = base[: -len(suffix)]
    events_path = Path(base + ".events.csv")
    snapshots_path = Path(base + ".snapshots.jsonl")
    if not events_path.exists():
        sys.exit(f"no event log at {events_path}")
    if not snapshots_path.exists():
        sys.exit(f"no snapshots at {snapshots_path}")
    with events_path.open(newline="") as f:
        events = list(csv.DictReader(f))
    for e in events:
        e["seq"] = int(e["seq"])
        e["round"] = int(e["round"])
    snapshots = [json.loads(line) for line in snapshots_path.read_text().splitlines() if line.strip()]
    if not snapshots:
        sys.exit("the snapshot file is empty")
    return events, snapshots


def num(value: str) -> int | None:
    return int(value) if value not in ("", None) else None


def tile(e: dict, which: str) -> tuple[int, int] | None:
    x, y = num(e[f"{which}_x"]), num(e[f"{which}_y"])
    return (x, y) if x is not None and y is not None else None


def at(p: tuple[int, int] | None) -> str:
    """A tile as "(x,y)", matching the C# tools."""
    return f"({p[0]},{p[1]})" if p else "?"


# ---------------------------------------------------------------------------
# The board, rebuilt from a snapshot plus events
# ---------------------------------------------------------------------------

class Board:
    def __init__(self, snapshot: dict):
        s = deepcopy(snapshot)
        self.width, self.height = s["width"], s["height"]
        self.arena = s["arena"]
        self.round = s["round"]
        self.phase = s["phase"]
        self.actor = s.get("actor", "")
        self.heights = [[int(c) for c in row] for row in s["heights"]]
        self.terrain = [list(row) for row in s["terrain"]]
        self.features = [list(row) for row in s["features"]]
        self.dead_features = {(src["x"], src["y"]) for src in s["sources"] if src["dead"]}
        self.traps = {(t["x"], t["y"]): t["kind"] for t in s["traps"]}
        self.rumbles = {(r["x"], r["y"]): r["unit"] for r in s["rumbles"]}
        self.units = {u["label"]: u for u in s["units"]}
        self.tethers = list(s["tethers"])
        self.danger = {tuple(p) for p in s.get("danger", [])}
        self.outcome = ""

    def unit_at(self, x: int, y: int) -> dict | None:
        found = None
        for u in self.units.values():
            if u["on_board"] and u["alive"] and (u["x"], u["y"]) == (x, y):
                # Show the rider rather than the mount when two share a tile.
                if found is None or "riding" in u:
                    found = u
        return found

    def apply(self, e: dict) -> None:
        kind = e["event"]
        target = self.units.get(e["target"])
        actor = self.units.get(e["actor"])
        to = tile(e, "to")

        if kind == "round_start":
            self.round = int(e["amount"])
        elif kind == "phase_start":
            self.phase = e["detail"]
        elif kind == "turn_start":
            self.actor = e["actor"]
        elif kind in ("move", "charge", "push", "pull", "drag", "swap") and target and to:
            self.place(target, to)
        elif kind in ("damage", "heal") and target:
            target["hp"] = int(e["hp_after"])
        elif kind in ("death", "fled", "knocked_out") and target:
            target["alive"] = False
            target["on_board"] = False
            if "carrying" in target:
                rider = self.units.get(target.pop("carrying"))
                if rider:
                    rider.pop("riding", None)
            if "riding" in target:
                mount = self.units.get(target.pop("riding"))
                if mount:
                    mount.pop("carrying", None)
        elif kind == "status" and target:
            target["statuses"][e["detail"]] = int(e["amount"]) or 1
        elif kind == "status_end" and target:
            target["statuses"].pop(e["detail"], None)
        elif kind == "trap_set" and to:
            self.traps[to] = e["detail"]
        elif kind in ("trap_triggered", "trap_end") and to:
            self.traps.pop(to, None)
        elif kind == "burrow" and target:
            target["on_board"] = False
            target["statuses"]["burrowed"] = 1
            if to:
                self.rumbles[to] = e["target"]
        elif kind == "surface" and target and to:
            self.rumbles = {p: u for p, u in self.rumbles.items() if u != e["target"]}
            target["statuses"].pop("burrowed", None)
            target["on_board"] = True
            target["x"], target["y"] = to
        elif kind == "crater" and to:
            self.heights[to[1]][to[0]] = int(e["amount"])
        elif kind == "source_death" and to:
            self.dead_features.add(to)
        elif kind == "creature_move":
            frm = tile(e, "from")
            if frm and to:
                glyph = self.features[frm[1]][frm[0]]
                self.features[frm[1]][frm[0]] = "."
                self.features[to[1]][to[0]] = glyph
        elif kind == "mount" and actor and target:
            actor["riding"] = e["target"]
            target["carrying"] = e["actor"]
            actor["x"], actor["y"] = target["x"], target["y"]
        elif kind == "dismount" and actor and to:
            mount = self.units.get(actor.pop("riding", ""))
            if mount:
                mount.pop("carrying", None)
            actor["x"], actor["y"] = to
        elif kind == "tether_set":
            self.tethers.append({"label": e["detail"]})
        elif kind == "tether_end":
            self.tethers = [t for t in self.tethers if t.get("label") != e["detail"]]
        elif kind == "battle_end":
            self.outcome = e["detail"]

    def place(self, unit: dict, to: tuple[int, int]) -> None:
        unit["x"], unit["y"] = to
        if "carrying" in unit:  # a mount carries its rider along
            rider = self.units.get(unit["carrying"])
            if rider:
                rider["x"], rider["y"] = to
        if "riding" in unit:  # a rider moves its mount along
            mount = self.units.get(unit["riding"])
            if mount:
                mount["x"], mount["y"] = to


# ---------------------------------------------------------------------------
# Describing events
# ---------------------------------------------------------------------------

def name(board: Board, label: str) -> str:
    u = board.units.get(label)
    if not u:
        return label
    return u["name"] if u["side"] == "party" else f"{u['name']} {label}"


def describe(board: Board, e: dict) -> str | None:
    k = e["event"]
    a, t = name(board, e["actor"]), name(board, e["target"])
    move = e["move"].replace("_", " ").title()
    timing = f" ({e['timing']} timing)" if e["timing"] else ""
    amount = e["amount"]
    frm, to = at(tile(e, "from")), at(tile(e, "to"))
    lines = {
        "move": f"{t} moves {frm} → {to}",
        "charge": f"{t} charges {frm} → {to}",
        "push": f"{t} is pushed {frm} → {to}",
        "pull": f"{t} is pulled {frm} → {to}",
        "drag": f"{t} is dragged {frm} → {to}",
        "swap": f"{t} swaps to {to}",
        "collide": f"{t} slams into {e['detail']}",
        "fall": f"{t} falls {amount} level(s)",
        "damage": f"{a} hits {t} with {move}: {amount} damage{timing}" if move else f"{t} takes {amount} damage",
        "miss": f"{a}'s {move} misses {t}{timing}",
        "heal": f"{t} recovers {amount} HP",
        "death": f"{t} is defeated",
        "knocked_out": f"{t} is knocked out",
        "fled": f"{t} flees the battle",
        "flee_start": f"{t} loses its nerve!",
        "status": f"{t} is {e['detail'].replace('_', ' ')}",
        "status_end": f"{t} is no longer {e['detail'].replace('_', ' ')}",
        "parry": f"{t} parries {a}",
        "counter": f"{a} strikes back at {t}{timing}",
        "trap_set": f"{a} sets a {e['detail']} trap at {to}",
        "trap_triggered": f"{t} is caught by a {e['detail']} trap",
        "trap_end": f"a {e['detail']} trap crumbles",
        "tether_set": f"{a} tethers {e['detail']}",
        "tether_end": f"a tether breaks ({e['detail']})",
        "source_use": f"{a} draws on the {e['detail']} at {to}",
        "source_drain": f"{a} drains the {e['detail']} at {to}",
        "source_death": f"the {e['detail']} at {to} dies",
        "corruption": "dark magic stirs",
        "burrow": f"{t} burrows; the ground rumbles at {to}",
        "surface": f"{t} bursts out at {to}",
        "crater": f"a crater opens at {to}",
        "order": f"{a} points the beast at {t if e['target'] else to}",
        "buff": f"{a} whips {t} (+{amount})",
        "mount": f"{a} mounts {t}",
        "dismount": f"{a} dismounts to {to}",
        "scamper": f"{a} scampers",
        "swap_order": "the party swaps its order",
        "creature_move": f"the {e['detail']} move {frm} → {to}",
        "battle_end": {"party_won": "Victory!", "party_lost": "Defeat.", "draw": "Draw."}.get(e["detail"], e["detail"]),
    }
    return lines.get(k)


# ---------------------------------------------------------------------------
# Drawing
# ---------------------------------------------------------------------------

def draw_board(board: Board, show_heights: bool, show_danger: bool) -> Text:
    text = Text()
    text.append("    " + " ".join(str(x % 10) for x in range(board.width)) + "\n", style="grey62")
    for y in range(board.height):
        text.append(f"{y:>2}  ", style="grey62")
        for x in range(board.width):
            h = board.heights[y][x]
            bg = HEIGHT_BG[min(max(h, 0), 4)]
            if show_danger and (x, y) in board.danger:
                bg = "dark_red"
            fg = "white" if h < 3 else "black"
            unit = board.unit_at(x, y)
            if show_heights:
                glyph, style = str(h), fg
            elif unit:
                glyph = "H" if unit["label"] == "hero" else "L" if unit["label"] == "loper" else unit["label"]
                style = PARTY_STYLE if unit["side"] == "party" else ENEMY_STYLE
            elif (x, y) in board.rumbles:
                glyph, style = "!", "bold yellow"
            elif (x, y) in board.traps:
                glyph, style = "^", "bold yellow"
            elif board.features[y][x] != ".":
                glyph = board.features[y][x]
                style = "grey50" if (x, y) in board.dead_features else "green3"
            else:
                glyph, style = board.terrain[y][x], fg
            text.append(f"{glyph} ", style=f"{style} on {bg}")
        text.append("\n")
    return text


def draw_units(board: Board) -> Table:
    table = Table(show_header=False, box=None, padding=(0, 1))
    for label, u in board.units.items():
        shown = name(board, label)
        if not u["alive"]:
            status = "fled" if "fled" in u["statuses"] else "out"
            table.add_row(Text(shown, style="grey50"), Text(status, style="grey50"), "")
            continue
        filled = round(10 * u["hp"] / u["max_hp"]) if u["max_hp"] else 0
        bar = Text("█" * filled + "░" * (10 - filled), style="green3" if u["side"] == "party" else "red3")
        statuses = " ".join(k.replace("_", " ") for k in u["statuses"])
        if "riding" in u:
            statuses = (statuses + " riding").strip()
        table.add_row(Text(shown, style=PARTY_STYLE if u["side"] == "party" else ENEMY_STYLE),
                      Text.assemble(bar, f" {u['hp']}/{u['max_hp']}"), Text(statuses, style="yellow"))
    return table


# ---------------------------------------------------------------------------
# Navigation
# ---------------------------------------------------------------------------

class Viewer:
    def __init__(self, events: list[dict], snapshots: list[dict]):
        self.events = events
        self.snapshots = snapshots
        self.index = 0  # the last event applied
        self.show_heights = False
        self.show_danger = False
        self.console = Console()

    def board_at(self, index: int) -> Board:
        seq = self.events[index]["seq"]
        snap = self.snapshots[0]
        for s in self.snapshots:
            if s["seq"] <= seq:
                snap = s
        board = Board(snap)
        for e in self.events:
            if snap["seq"] < e["seq"] <= seq:
                board.apply(e)
        return board

    def turn_bounds(self) -> tuple[int, int]:
        """The first and last event index of the turn containing the current event."""
        start = self.index
        while start > 0 and self.events[start]["event"] not in ("turn_start", "round_start"):
            start -= 1
        end = self.index
        while end < len(self.events) - 1 and self.events[end]["event"] != "turn_end":
            end += 1
        return start, end

    def render(self) -> None:
        board = self.board_at(self.index)
        start, _ = self.turn_bounds()
        recent = [describe(board, e) for e in self.events[start : self.index + 1]]
        lines = [line for line in recent if line][-12:]
        event = self.events[self.index]
        title = f"{board.arena} · round {board.round} · {board.phase} phase"
        if board.actor:
            title += f" · {name(board, board.actor)}'s turn"
        if board.outcome:
            title += f" · {board.outcome.replace('_', ' ')}"
        body = Table.grid(padding=(0, 4))
        body.add_row(draw_board(board, self.show_heights, self.show_danger),
                     Group(draw_units(board), Text(""), Text("\n".join(lines) or "-", style="white")))
        footer = Text(f"event {self.index + 1}/{len(self.events)} ({event['event']})  "
                      "←→ event ↑↓ turn  r round  h heights  d danger  space turn  p all  q quit",
                      style="grey62")
        self.console.clear()
        self.console.print(Panel(Group(body, footer), title=title, title_align="left"))

    def step(self, delta: int) -> None:
        self.index = max(0, min(len(self.events) - 1, self.index + delta))

    def next_turn(self) -> None:
        _, end = self.turn_bounds()
        self.index = min(len(self.events) - 1, end + 1 if self.index == end else end)

    def previous_turn(self) -> None:
        start, _ = self.turn_bounds()
        self.index = max(0, start - 1)
        start, _ = self.turn_bounds()
        self.index = start

    def jump_to_round(self, number: int) -> None:
        for i, e in enumerate(self.events):
            if e["event"] == "round_start" and int(e["amount"]) == number:
                self.index = i
                return

    def play(self, to_end: bool) -> None:
        _, end = self.turn_bounds()
        stop = len(self.events) - 1 if to_end else end
        while self.index < stop:
            self.step(1)
            if describe(self.board_at(self.index), self.events[self.index]):
                self.render()
                time.sleep(0.35)
        self.render()

    def run(self) -> None:
        while True:
            self.render()
            key = read_key()
            if key in ("q", "Q"):
                return
            if key in ("RIGHT", "n"):
                self.step(1)
            elif key in ("LEFT", "b"):
                self.step(-1)
            elif key in ("DOWN", "N"):
                self.next_turn()
            elif key in ("UP", "B"):
                self.previous_turn()
            elif key == "h":
                self.show_heights = not self.show_heights
            elif key == "d":
                self.show_danger = not self.show_danger
            elif key == " ":
                self.play(to_end=False)
            elif key == "p":
                self.play(to_end=True)
            elif key == "r":
                answer = input("jump to round: ").strip()
                if answer.isdigit():
                    self.jump_to_round(int(answer))


def read_key() -> str:
    if readchar is None:
        return input("> ").strip() or "n"
    key = readchar.readkey()
    return {readchar.key.RIGHT: "RIGHT", readchar.key.LEFT: "LEFT",
            readchar.key.UP: "UP", readchar.key.DOWN: "DOWN"}.get(key, key)


def main() -> None:
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    events, snapshots = load(sys.argv[1])
    viewer = Viewer(events, snapshots)
    if "--dump" in sys.argv:  # print every turn's final board once, for checking without a keyboard
        while True:
            _, end = viewer.turn_bounds()
            viewer.index = end
            viewer.render()
            if end >= len(events) - 1:
                return
            viewer.index = end + 1
        return
    viewer.run()


if __name__ == "__main__":
    main()
