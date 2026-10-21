#!/usr/bin/env python3
"""Kalan bağımlılıkların canlı envanteri: kodda kaç tane var, tam olarak nerede.

Kullanım:
  python3 tools/remaining.py            # ekrana yaz
  python3 tools/remaining.py --write    # docs/BAGIMLILIKLAR.md içindeki
                                        # <!-- auto:remaining --> ... <!-- /auto:remaining -->
                                        # bloğunu koddan yeniden üret
  python3 tools/remaining.py --count    # yalnız üç sayı

Ölçüt koddur, iddia değil: bir `Task.Delay` ya da yazıyla basış koddan kalkınca
kutu kendiliğinden düşer. Kutu başına bir çağrı yeri vardır; `[~]` ölçülüp
"borç değil" bulunmuş yerdir (yoklama aralığı gibi).
"""
from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
FM = ROOT / "cli" / "fm"
DOC = ROOT / "docs" / "BAGIMLILIKLAR.md"
START = "<!-- auto:remaining -->"
END = "<!-- /auto:remaining -->"

DELAY = re.compile(r"Task\.Delay\(")
TEXT_PRESS = re.compile(r'\("text",\s*"([^"]+)"')
UNTIL_SECONDS = re.compile(r"Until\(.*?seconds:\s*([0-9.]+)")

# Ölçüldü ve borç olmadığı kanıtlanan yerler: sayıya girer, açık kutuya girmez.
SETTLED = {
    ("Bridge.cs", 103): "döngüde yoklama aralığı (250 ms), §8.179",
    ("Bridge.cs", 109): "döngüde yoklama aralığı (250 ms), §8.179",
    ("Bridge.cs", 127): "döngüde yoklama aralığı (250 ms), §8.179",
    ("Bridge.cs", 147): "döngüde yoklama aralığı (250 ms), §8.179",
    ("Clock.cs", 148): "`next`in Settled beklemesi ölçüldü, değişiklik gerekmedi §8.181",
    ("Clock.cs", 157): "`next`in Settled beklemesi ölçüldü, değişiklik gerekmedi §8.181",
    ("Clock.cs", 187): "işsizlik yolu oyunun cevabına bağlandı §8.289",
    ("League.cs", 253): "tur arasında tempo (250 ms); ilk okuma beklemesiz, §8.320",
    ("StatusFlags.cs", 54): "döngüde yoklama aralığı (250 ms), §8.317",
}

# Kısa `Until`ler için aynısı: işaret oyunun kendi cevabı, süre yalnız üst sınır.
SETTLED_SHORT = {
    ("Coaches.cs", 56): "yardımcının ataması oyunun işaretiyle bekleniyor; 3 sn bilinçli kısa, §8.318",
    ("League.cs", 223): "yarışma bağı ilk `pathset`te tutuyor (30 ms); 3 sn yalnız üst sınır, §8.319",
}

# Denendi, ölçülemedi / karar bekliyor: açık kutuya girmez, tekrar denenmez.
PARKED = {
    ("Guided.cs", 161): "bedel sözü penceresi bu kayıtta açılamıyor; Faruk'a soru (autopilot-quest, Tur 21)",
    ("Guided.cs", 174): "bedel sözü penceresi bu kayıtta açılamıyor; Faruk'a soru (autopilot-quest, Tur 21)",
}


def scan_delays() -> list[tuple[str, int, str]]:
    out = []
    for path in sorted(FM.glob("*.cs")):
        for i, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            if DELAY.search(line):
                out.append((path.name, i, " ".join(line.split())[:90]))
    return out


def scan_text_presses() -> list[tuple[str, int, str]]:
    out = []
    for path in sorted(FM.glob("*.cs")):
        for i, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            m = TEXT_PRESS.search(line)
            if m:
                out.append((path.name, i, m.group(1)))
    return out


def scan_short_until(limit: float = 3.0) -> list[tuple[str, int, float]]:
    out = []
    for path in sorted(FM.glob("*.cs")):
        for i, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            m = UNTIL_SECONDS.search(line)
            if m and float(m.group(1)) <= limit:
                out.append((path.name, i, float(m.group(1))))
    return out


def render() -> str:
    delays = scan_delays()
    texts = scan_text_presses()
    short = scan_short_until()

    open_delays = [d for d in delays if (d[0], d[1]) not in SETTLED and (d[0], d[1]) not in PARKED]
    open_short = [u for u in short if (u[0], u[1]) not in SETTLED_SHORT]
    lines: list[str] = []
    lines.append(
        f"**Canlı sayaç:** açık kutu **{len(open_delays) + len(texts) + len(open_short)}** · "
        f"`Task.Delay` {len(delays)} çağrı ({len(open_delays)} borç, {len(SETTLED)} borç değil) · "
        f"yazıyla basış {len(texts)} · kısa `Until` (≤3 sn) {len(open_short)}"
    )
    lines.append("")
    lines.append(
        "Bu blok `python3 tools/remaining.py --write` ile **koddan** üretilir, elle düzenlenmez. "
        "Bir kutu = koddaki bir çağrı yeri; yer kalkınca kutu kendiliğinden düşer. `[~]` ölçülüp borç olmadığı "
        "kanıtlanmış yerdir."
    )
    lines.append("")

    lines.append(f"#### A1. `Task.Delay` — {len(open_delays)} borç / {len(delays)} çağrı")
    lines.append("")
    for name, lineno, code in delays:
        if (name, lineno) in SETTLED:
            lines.append(f"- [~] `{name}:{lineno}` — {SETTLED[(name, lineno)]}")
        elif (name, lineno) in PARKED:
            lines.append(f"- [?] `{name}:{lineno}` — {PARKED[(name, lineno)]}")
        else:
            lines.append(f"- [ ] `{name}:{lineno}` `{code}`")
    lines.append("")

    lines.append(f"#### B1. Yazıyla basma/tıklama — {len(texts)} yer")
    lines.append("")
    for name, lineno, label in texts:
        lines.append(f'- [ ] `{name}:{lineno}` "{label}"')
    lines.append("")

    lines.append(f"#### A2. Kısa `Until` (≤3 sn) — {len(open_short)} yer")
    lines.append("")
    for name, lineno, secs in short:
        if (name, lineno) in SETTLED_SHORT:
            lines.append(f"- [~] `{name}:{lineno}` — {SETTLED_SHORT[(name, lineno)]}")
        else:
            lines.append(f"- [ ] `{name}:{lineno}` {secs:g} sn")
    lines.append("")
    return "\n".join(lines)


def write_into_doc(block: str) -> None:
    text = DOC.read_text(encoding="utf-8")
    if START not in text or END not in text:
        sys.exit(f"işaret bulunamadı: {START} / {END} — docs/BAGIMLILIKLAR.md'ye ekle")
    head, rest = text.split(START, 1)
    _, tail = rest.split(END, 1)
    DOC.write_text(f"{head}{START}\n{block}{END}{tail}", encoding="utf-8")


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--write", action="store_true")
    ap.add_argument("--count", action="store_true")
    args = ap.parse_args()

    if args.count:
        delays, texts, short = scan_delays(), scan_text_presses(), scan_short_until()
        open_delays = [
            d for d in delays if (d[0], d[1]) not in SETTLED and (d[0], d[1]) not in PARKED
        ]
        open_short = [u for u in short if (u[0], u[1]) not in SETTLED_SHORT]
        print(f"{len(open_delays)} {len(texts)} {len(open_short)}")
        return

    block = render()
    if args.write:
        write_into_doc(block)
        print("yazıldı:", DOC)
    else:
        print(block)


if __name__ == "__main__":
    main()
