#!/usr/bin/env python3
"""Plays an interactive fm command in a pseudo-terminal and photographs the screen along the way.

Arrow-key menus redraw the screen in place, so a plain capture of the bytes is not what the user
sees; pyte keeps a terminal screen and we photograph that.

  uv run --with pyte tools/manual/drive.py <out-dir> <scene> [<scene> …]

A scene is a list of steps in SCENES below:
  ("wait", "<regex>")    wait until the screen shows it (60 s, 600 s for matches)
  ("keys", "<keys>")     type; "\\r" is Enter, DOWN / UP move in menus
  ("pick", "<regex>")    move through a menu until the highlighted line matches
  ("exit",)              wait for the command to finish
  ("snap", "<name>")     photograph the screen as <out-dir>/<name>.png
"""
import html, os, pty, re, select, subprocess, sys, tempfile, time

import pyte

sys.path.insert(0, os.path.dirname(__file__))
from ansi2html import BASE16, page

COLS, ROWS = 110, 400
CHROME = "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
DOWN, UP = "\x1b[B", "\x1b[A"
NAMES = ["black", "red", "green", "brown", "blue", "magenta", "cyan", "white"]

# name: (command, steps)
SCENES = {
	"reply": ("fm reply 19", [("wait", r"Respond"), ("snap", "reply"), ("keys", "\r"), ("exit",), ("snap", "reply-done")]),
	"register": ("fm register", [("wait", r"Yardımcı seçsin"), ("snap", "register"), ("pick", r"Emre Mor"), ("snap", "register-pick"),
		("keys", "\r"), ("wait", r"> Listeyi gönder"), ("snap", "register-ready"), ("keys", "\r"), ("exit",), ("snap", "register-done")]),
	"offer": ("fm offer 'Mauro Icardi' 5M", [("wait", r"değiştirmek istediğini seç"), ("snap", "offer"),
		("pick", r"Sonraki satıştan pay"), ("keys", "\r"), ("wait", r"Seçimi değiştir"), ("pick", r"Seçimi değiştir"), ("keys", "\r"),
		("wait", r"35%"), ("pick", r"35%"), ("snap", "offer-choice"), ("keys", "\r"),
		("wait", r"Sonraki satıştan pay: 35%"), ("snap", "offer-edited"),
		("pick", r"Gönderme, ekranı"), ("keys", "\r"), ("exit",)]),
	"offer-lock": ("fm offer 'Robin Hack'", [("wait", r"değiştirmek istediğini seç"),
		("pick", r"Bonservis:"), ("keys", "\r"), ("wait", r"Pazarlık durumu"), ("pick", r"Pazarlık durumu"), ("keys", "\r"),
		("wait", r"ne kadar değiştirebilsin"), ("pick", r"Yarı pazarlıklı"), ("snap", "offer-lock"), ("keys", "\r"),
		("wait", r"Yarı pazarlıklı"), ("snap", "offer-locked"),
		("pick", r"Gönderme, ekranı"), ("keys", "\r"), ("exit",)]),
	# Changes the game: opens renewal talks, puts a wage to the agent, then calls them off.
	"contract": ("fm contract 'Kerem Aktürkoğlu'", [("wait", r"Yine de gir"), ("keys", "\r"),
		("wait", r"Vaatler — değiştirmek"), ("snap", "contract-promises"),
		("pick", r"Vaatleri onayla"), ("keys", "\r"), ("wait", r"Sözleşme — değiştirmek"), ("snap", "contract-terms"),
		("pick", r"Haftalık ücret"), ("keys", "\r"), ("wait", r"Tutarı değiştir"), ("pick", r"Tutarı"), ("keys", "\r"),
		("wait", r"tutarı"), ("keys", "130000\r"), ("wait", r"Haftalık ücret: £130,000"), ("snap", "contract-invalid"),
		("pick", r"Menajer ücreti"), ("keys", "\r"), ("wait", r"Tutarı değiştir"), ("pick", r"Tutarı"), ("keys", "\r"),
		("wait", r"tutarı"), ("keys", "800000\r"), ("wait", r"Menajer ücreti: £800K"),
		("pick", r"Şartları menajere öner"), ("keys", "\r"), ("wait", r"Menajerin istediği|kabul ediyor"), ("snap", "contract-answer"),
		("pick", r"Görüşmeyi iptal et"), ("keys", "\r"), ("wait", r"İptal et"), ("pick", r"İptal et"), ("keys", "\r"), ("exit",)]),
	# The same talks carried on once open: the answer to a proposal.
	"contract-answer": ("fm contract", [("wait", r"Şartları menajere öner"),
		("pick", r"Şartları menajere öner"), ("keys", "\r"), ("wait", r"Menajerin istediği|kabul ediyor"), ("snap", "contract-answer"),
		("pick", r"Şimdilik bırak"), ("keys", "\r"), ("exit",)]),
}


def colour(name, default):
	if name == "default":
		return default
	if re.fullmatch(r"[0-9a-f]{6}", name):
		return "#" + name
	bright = name.startswith("bright")
	base = NAMES.index(name.removeprefix("bright").replace("yellow", "brown"))
	return BASE16[base + 8 * bright]


def render(screen):
	lines = []
	for y in range(screen.lines):
		row, out, last = screen.buffer[y], [], None
		for x in range(screen.columns):
			c = row[x]
			if c.data == "":
				continue
			fg, bg = colour(c.fg, None), colour(c.bg, None)
			if c.reverse:
				fg, bg = bg or "#16181c", fg or "#d6d8dc"
			css = ";".join(filter(None, [fg and f"color:{fg}", bg and f"background:{bg}",
				c.bold and "font-weight:bold", c.italics and "font-style:italic",
				c.underscore and "text-decoration:underline"]))
			if css != last:
				if last:
					out.append("</span>")
				if css:
					out.append(f'<span style="{css}">')
				last = css
			out.append(html.escape(c.data))
		if last:
			out.append("</span>")
		lines.append(("".join(out), "".join(row[x].data for x in range(screen.columns)).rstrip()))
	while lines and not lines[-1][1]:
		lines.pop()
	width = max((len(plain) for _, plain in lines), default=0)
	return "\n".join(h for h, _ in lines), width, len(lines)


def photograph(screen, cmd, path):
	body, width, lines = render(screen)
	body = f'<span style="opacity:.6">$</span> {html.escape(cmd)}\n' + body
	with tempfile.NamedTemporaryFile("w", suffix=".html", delete=False, encoding="utf-8") as f:
		f.write(page(body, cmd))
	subprocess.run([CHROME, "--headless=new", "--disable-gpu", "--hide-scrollbars",
		"--force-device-scale-factor=2", "--default-background-color=0e1013ff",
		f"--window-size={max(width, len(cmd) + 2) * 8 + 38},{(lines + 1) * 18 + 52}",
		f"--screenshot={path}", f"file://{f.name}"], capture_output=True)
	os.unlink(f.name)
	print("  " + os.path.basename(path))


def play(out, name):
	cmd, steps = SCENES[name]
	screen = pyte.Screen(COLS, ROWS)
	stream = pyte.ByteStream(screen)
	pid, fd = pty.fork()
	if pid == 0:
		os.environ.update(COLUMNS=str(COLS), LINES=str(ROWS), TERM="xterm-256color")
		os.execvp("sh", ["sh", "-c", f"stty cols {COLS} rows {ROWS}; exec {cmd}"])

	def pump(seconds):
		end = time.time() + seconds
		while time.time() < end:
			ready, _, _ = select.select([fd], [], [], 0.1)
			if ready:
				try:
					data = os.read(fd, 65536)
				except OSError:
					return False
				if not data:
					return False
				stream.feed(data)
		return True

	def text():
		return "\n".join(screen.display)

	alive = True
	for kind, *rest in steps:
		arg = rest[0] if rest else None
		if kind == "exit":
			while alive:
				alive = pump(1)
		elif kind == "wait":
			limit = time.time() + (600 if "match" in cmd else 60)
			while alive and not re.search(arg, text()) and time.time() < limit:
				alive = pump(0.3)
			if not re.search(arg, text()):
				print(f"  {name}: '{arg}' görünmedi", file=sys.stderr)
				print(text().rstrip(), file=sys.stderr)
				break
			pump(0.6)  # let the redraw settle
		elif kind == "pick":
			# Menus open on the current choice, so the one wanted can be above: down to the
			# bottom first, then back up.
			key, last = DOWN, None
			for _ in range(160):
				current = next((l for l in screen.display if l.lstrip().startswith(">")), "")
				if re.search(arg, current):
					break
				if current == last:
					key = UP
				last = current
				os.write(fd, key.encode())
				alive = pump(0.15)
			pump(0.4)
		elif kind == "keys":
			os.write(fd, arg.encode())
			alive = pump(0.4)
		elif kind == "snap":
			photograph(screen, cmd, os.path.join(out, arg + ".png"))
	if alive:
		os.kill(pid, 15)
	os.waitpid(pid, 0)


if __name__ == "__main__":
	out = sys.argv[1]
	os.makedirs(out, exist_ok=True)
	for scene in sys.argv[2:]:
		play(out, scene)
