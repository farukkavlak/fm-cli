#!/usr/bin/env python3
"""Turn a terminal capture (ANSI, 256 colors) into a standalone HTML page that looks like a terminal."""
import html, re, sys

BASE16 = ["#1d1f21", "#cc6666", "#8fbf6a", "#e0c070", "#81a2be", "#b294bb", "#8abeb7", "#c5c8c6",
          "#6b6f73", "#ff6b6b", "#3ddc84", "#ffd75f", "#6cb6ff", "#d7a6ff", "#5fd7d7", "#ffffff"]

def color(n):
    if n < 16:
        return BASE16[n]
    if n < 232:
        n -= 16
        steps = [0, 95, 135, 175, 215, 255]
        return "#%02x%02x%02x" % (steps[n // 36], steps[n // 6 % 6], steps[n % 6])
    v = 8 + (n - 232) * 10
    return "#%02x%02x%02x" % (v, v, v)

def convert(text):
    text = re.sub(r"\x1b\[\?[0-9;]*[a-zA-Z]|\x1b[=>]|\^D\x08\x08|[\x04\x08]", "", text)
    text = text.replace("\r\n", "\n").replace("\r", "")
    out, st = [], {}
    for part in re.split(r"(\x1b\[[0-9;]*m)", text):
        m = re.fullmatch(r"\x1b\[([0-9;]*)m", part)
        if not m:
            if part:
                css = ";".join(f"{k}:{v}" for k, v in st.items())
                out.append(f'<span style="{css}">{html.escape(part)}</span>' if css else html.escape(part))
            continue
        codes = [int(c) for c in m.group(1).split(";") if c] or [0]
        i = 0
        while i < len(codes):
            c = codes[i]
            if c == 0: st = {}
            elif c == 1: st["font-weight"] = "bold"
            elif c == 2: st["opacity"] = "0.6"
            elif c == 3: st["font-style"] = "italic"
            elif c == 4: st["text-decoration"] = "underline"
            elif c == 7: st["filter"] = "invert(1)"
            elif c in (22,): st.pop("font-weight", None); st.pop("opacity", None)
            elif c == 39: st.pop("color", None)
            elif c == 49: st.pop("background", None)
            elif 30 <= c <= 37: st["color"] = BASE16[c - 30]
            elif 90 <= c <= 97: st["color"] = BASE16[c - 82]
            elif 40 <= c <= 47: st["background"] = BASE16[c - 40]
            elif 100 <= c <= 107: st["background"] = BASE16[c - 92]
            elif c in (38, 48) and i + 2 < len(codes) and codes[i + 1] == 5:
                st["color" if c == 38 else "background"] = color(codes[i + 2]); i += 2
            elif c in (38, 48) and i + 4 < len(codes) and codes[i + 1] == 2:
                st["color" if c == 38 else "background"] = "#%02x%02x%02x" % tuple(codes[i + 2:i + 5]); i += 4
            i += 1
    return "".join(out).rstrip("\n")

def page(body, title):
    return f"""<!doctype html><meta charset="utf-8"><style>
body{{margin:0;background:#0e1013;display:inline-block}}
.win{{margin:0;background:#16181c;border-radius:10px;overflow:hidden;display:inline-block}}
.bar{{height:28px;background:#24272d;display:flex;align-items:center;gap:7px;padding:0 12px}}
.bar i{{width:12px;height:12px;border-radius:50%;display:inline-block}}
.bar span{{color:#8a8f98;font:12px Menlo,monospace;margin-left:10px}}
pre{{margin:0;padding:14px 18px 16px;color:#d6d8dc;font:13px/1.35 Menlo,'SF Mono',monospace;white-space:pre}}
</style><div class="win"><div class="bar"><i style="background:#ff5f57"></i><i style="background:#febc2e"></i><i style="background:#28c840"></i><span>{html.escape(title)}</span></div><pre>{body}</pre></div>"""

if __name__ == "__main__":
    src, dst, title = sys.argv[1], sys.argv[2], sys.argv[3]
    with open(src, encoding="utf-8", errors="replace") as f:
        body = convert(f.read())
    with open(dst, "w", encoding="utf-8") as f:
        f.write(page(body, title))
    plain = re.sub(r"<[^>]+>", "", body)
    plain = html.unescape(plain).split("\n")
    print(max(len(l) for l in plain), len(plain))
