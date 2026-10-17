"""Builds the "resize the focused window" animation for WindowMover, as a light and a dark SVG.

Run: python make_resize_window.py   (writes resize-window-light.svg and resize-window-dark.svg beside it)
The SVGs are generated; edit this file, not them. Times below are in seconds.

It is drawn like the pull-window animation (make_pull_window.py, whose pieces it borrows): the mouse, the
steps, then one monitor. Ctrl and Shift keys sit above the mouse. The blue window has focus. Ctrl+Shift
go down, the wheel turns up three notches and the window grows a step at a time, kept on its screen, then
down six and it shrinks. A dashed outline keeps the size it started at, to measure by. Each step holds
for a few seconds once its result shows, and a progress bar under the steps shows where the picture is.
"""
from pathlib import Path
import xml.dom.minidom as m

from make_pull_window import (EASE, ORANGE, DESK, TASKBAR, TASK_SEL, FOCUS, BEZEL, BAND, POWER_LIT,
                              ICONS_M1, M1_OTHER, BLUE_LIT, MOX, MOY, MS, at, BULLET_X, TEXT_X, ROWS,
                              row, LEAD3, MX, THEMES, win, HOLD, progress)

W, H = MX + 283 + 16, 262


def q(x):
    v = x / T * 100
    return (f"{v:.2f}".rstrip('0').rstrip('.')) + '%'


def track(name, stops):
    """One @keyframes block. stops: (time, css) or (time, css, easing), in time order. Time 0 and T are added."""
    stops = [s if len(s) == 3 else (s[0], s[1], None) for s in stops]
    if stops[0][0] != 0:
        stops.insert(0, (0, stops[0][1], None))
    if stops[-1][0] != T:
        stops.append((T, stops[-1][1], None))
    times = [s[0] for s in stops]
    assert times == sorted(times) and len(set(times)) == len(times), (name, times)
    body = "\n".join(f"      {q(t)} {{ {css};{(' animation-timing-function: ' + e + ';') if e else ''} }}" for t, css, e in stops)
    return f"    @keyframes {name} {{\n{body}\n    }}\n"


def op(name, stops):
    return track(name, [(s[0], f"opacity: {s[1]}") for s in stops])


def shown(name, *spans, fade=0.2):
    stops = []
    for a, b in spans:
        stops += [(a, 0), (a + fade, 1), (b, 1), (b + fade, 0)]
    return op(name, stops)


# --- the events (seconds) ----------------------------------------------------------------
# As in the pull animation, each step holds for HOLD seconds once its result shows, the keys and the
# wheel still lit, so the chain keys/mouse → step → monitor can be read before the next step.
S1 = 1.0                     # each step lights up 0.2 s after the one before it goes out
KEYS_DOWN = S1 + 0.5
E1 = KEYS_DOWN + 0.1 + HOLD
S2 = E1 + 0.2
UP = (S2 + 0.5, S2 + 0.9, S2 + 1.3)   # the wheel's notches, scrolling up
E2 = UP[-1] + 0.15 + HOLD
S3 = E2 + 0.2
DOWN = tuple(S3 + 0.5 + 0.4 * i for i in range(6))
E3 = DOWN[-1] + 0.15 + HOLD
S4 = E3 + 0.2
KEYS_UP = S4 + 0.5
E4 = KEYS_UP + 0.1 + HOLD
KEYS = (KEYS_DOWN, KEYS_UP)
END = E4
T = E4 + 1.5
TS = f"{T:g}s infinite"
SEGMENTS = ((S1, E1), (S2, E2), (S3, E3), (S4, E4))

BLUE = (100, 70, 120, 80)    # the blue window as drawn (BLUE_LIT)


def scaled(r, notches):
    """What WindowScaling does to a window (x, y, w, h) on the monitor: 10% a notch around its centre, kept on the screen."""
    x, y, w, h = r
    f = 1.1 ** notches
    cx, cy = x + w / 2, y + h / 2
    w, h = min(w * f, 240), min(h * f, 130)
    return min(max(cx - w / 2, 40), 280 - w), min(max(cy - h / 2, 30), 160 - h), w, h


def to_rect(r):
    """The transform that takes the blue window as drawn to r."""
    sx, sy = r[2] / BLUE[2], r[3] / BLUE[3]
    return f"transform: translate({r[0] - BLUE[0] * sx:.2f}px, {r[1] - BLUE[1] * sy:.2f}px) scale({sx:.4f}, {sy:.4f})"


GROW = [(0, to_rect(BLUE), "ease-out")]
_r = BLUE
for _t, _n in [(t, 1) for t in UP] + [(t, -1) for t in DOWN]:
    GROW.append((_t, GROW[-1][1], "ease-out"))
    _r = scaled(_r, _n)
    GROW.append((_t + 0.15, to_rect(_r)))

STEPS = ("Hold Ctrl+Shift and…", "…scroll up to make the window bigger", "…scroll down to make it smaller", "Now you can release Ctrl+Shift")

# The wheel's leaders are the pull animation's step-3 route, from rows 1 and 2. The keys' leader forks to
# both keys: Ctrl's centre is at x=48, Shift's at 106.
LEAD_UP = LEAD3.replace(f"{BULLET_X - 6},{row(2)} 124,{row(2)}", f"{BULLET_X - 6},{row(1)} 124,{row(1)}")
KEY_Y = 36


def to_keys(i):
    return f"M{BULLET_X - 6} {row(i)} H142 V66 H48 V{KEY_Y + 21} M106 66 V{KEY_Y + 21}"


TRACKS = [
    shown("step1", (S1, E1)),
    shown("step2", (S2, E2)),
    shown("step3", (S3, E3)),
    shown("step4", (S4, E4)),
    shown("keys1", (S1, E3)),
    shown("keys4", (S4, E4)),
    shown("leadup", (S2, E2)),
    shown("leaddn", (S3, E3)),
    shown("keyon", KEYS, fade=0.1),
    shown("up", (UP[0] - 0.2, E2), fade=0.1),
    shown("dn", (DOWN[0] - 0.2, E3), fade=0.1),
    # the wheel flashes at each notch, then stays lit through the hold after the last one
    shown("wheel", *[(t, t + 0.2) for t in UP[:-1]], (UP[-1], E2), *[(t, t + 0.2) for t in DOWN[:-1]], (DOWN[-1], E3), fade=0.05),
    shown("was", (UP[0], END)),
    track("grow", GROW),
    *[op(f"pg{i}", [(0, 0), (a, 0), (a + 0.2, 1), (b, 1), (b + 0.2, 0.4)]) for i, (a, b) in enumerate(SEGMENTS)],
    op("loop", [(0, 0), (0.5, 1), (T - 0.8, 1), (T, 0)]),
]
KF = "".join(TRACKS)

M1_BACK = win(48, 100, 80, 56, "#f6f8fa", "#c3d2dc")


def build(PAGE, LABEL, BRIGHT, FAINT, STAND, OUTLINE, OUTLINE_W, MOUSE, MOUSE_LINE, MOUSE_BTN):
    lead = f'fill="none" stroke="{ORANGE}" stroke-width="1.6" stroke-linejoin="round"'
    pg_css = "\n".join(f"      .pg{i} {{ animation: pg{i} {TS}; }}" for i in range(len(SEGMENTS)))
    steps = "\n    ".join(
        f'<circle cx="{BULLET_X}" cy="{row(i)}" r="3" fill="{FAINT}"/><text x="{TEXT_X}" y="{ROWS[i]}" fill="{FAINT}">{s}</text>'
        f'<g class="st{i + 1}"><circle cx="{BULLET_X}" cy="{row(i)}" r="3.5" fill="{ORANGE}"/><text x="{TEXT_X}" y="{ROWS[i]}" fill="{BRIGHT}">{s}</text></g>'
        for i, s in enumerate(STEPS))

    def key(x, w, fill, line, ink, word):
        return (f'<rect x="{x}" y="{KEY_Y}" width="{w}" height="21" rx="4" fill="{fill}" stroke="{line}" stroke-width="1.6"/>'
                f'<text x="{x + w / 2:g}" y="{KEY_Y + 15}" fill="{ink}">{word}</text>')

    return f"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {W} {H}" width="{W}" height="{H}" role="img" aria-labelledby="t d">
  <title id="t">Resizing the focused window</title>
  <desc id="d">A mouse with Ctrl and Shift keys above it, four steps, and a monitor whose blue window has focus. Ctrl and Shift are held; the wheel scrolls up and the window grows a notch at a time, then scrolls down and it shrinks below the size it started at, which a dashed outline marks. Then Ctrl and Shift are released.</desc>
  <style>
    text {{ font-family: "Segoe UI", Helvetica, Arial, sans-serif; }}
    .st1, .st2, .st3, .st4, .keys1, .keys4, .leadup, .leaddn, .keyon, .up, .dn, .wheel, .was, .pgbar {{ opacity: 0; }}
    @media (prefers-reduced-motion: no-preference) {{
      .pgbar {{ opacity: 1; }}
{pg_css}
      .st1 {{ animation: step1 {TS}; }}
      .st2 {{ animation: step2 {TS}; }}
      .st3 {{ animation: step3 {TS}; }}
      .st4 {{ animation: step4 {TS}; }}
      .keys1 {{ animation: keys1 {TS}; }}
      .keys4 {{ animation: keys4 {TS}; }}
      .leadup {{ animation: leadup {TS}; }}
      .leaddn {{ animation: leaddn {TS}; }}
      .keyon {{ animation: keyon {TS}; }}
      .up {{ animation: up {TS}; }}
      .dn {{ animation: dn {TS}; }}
      .wheel {{ animation: wheel {TS}; }}
      .was {{ animation: was {TS}; }}
      .grow {{ animation: grow {TS}; }}
      .loop {{ animation: loop {TS}; }}
    }}
{KF}  </style>

  <rect width="{W}" height="{H}" rx="12" fill="{PAGE}"/>
  <g class="loop">

  <!-- the mouse, seen from above, with the scroll direction beside the wheel -->
  <g transform="translate({MOX} {MOY}) scale({MS})">
    <rect x="0" y="0" width="70" height="96" rx="32" fill="{MOUSE}" stroke="{MOUSE_LINE}" stroke-width="2.4"/>
    <line x1="0" y1="42" x2="70" y2="42" stroke="{MOUSE_LINE}" stroke-width="2.4"/>
    <line x1="35" y1="0" x2="35" y2="42" stroke="{MOUSE_LINE}" stroke-width="2.4"/>
    <rect x="29" y="12" width="12" height="22" rx="6" fill="{MOUSE_BTN}" stroke="{MOUSE_LINE}" stroke-width="1.8"/>
    <rect class="wheel" x="27" y="10" width="16" height="26" rx="8" fill="{ORANGE}"/>
    <rect x="-8" y="40" width="10" height="14" rx="3" fill="{MOUSE_BTN}" stroke="{MOUSE_LINE}" stroke-width="1.8"/>
    <rect x="-8" y="60" width="10" height="14" rx="3" fill="{MOUSE_BTN}" stroke="{MOUSE_LINE}" stroke-width="1.8"/>
    <g fill="none" stroke="{ORANGE}" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round">
      <path class="up" d="M47 21 L52 15 L57 21"/>
      <path class="dn" d="M47 27 L52 33 L57 27"/>
    </g>
  </g>

  <!-- Ctrl and Shift, lit while held -->
  <g font-size="12" text-anchor="middle">
    {key(26, 44, MOUSE_BTN, MOUSE_LINE, LABEL, "Ctrl")}{key(78, 56, MOUSE_BTN, MOUSE_LINE, LABEL, "Shift")}
    <g class="keyon">{key(26, 44, ORANGE, ORANGE, "#ffffff", "Ctrl")}{key(78, 56, ORANGE, ORANGE, "#ffffff", "Shift")}</g>
  </g>

  <!-- the steps, and the leaders from each step to its key or button -->
  <g font-size="15">
    <text x="{BULLET_X - 4}" y="48" font-size="16" font-weight="600" fill="{BRIGHT}">Resize the focused window</text>
    {steps}
  </g>
  {progress(FAINT, len(SEGMENTS))}
  <g {lead}>
    <path class="keys1" d="{to_keys(0)}"/>
    <path class="keys4" d="{to_keys(3)}"/>
    <polyline class="leadup" points="{LEAD_UP}"/>
    <polyline class="leaddn" points="{LEAD3}"/>
  </g>

  <g transform="translate({MX} 0)">
    <!-- the monitor: stand, bezel with the power button's band, screen -->
    <g fill="{STAND}"><rect x="146" y="200" width="28" height="14"/><rect x="122" y="214" width="76" height="6" rx="3"/></g>
    <path d="{BEZEL}" fill="#000000"/><path d="{BAND}" fill="#000000"/>
    <path d="{BEZEL}" fill="none" stroke="{OUTLINE}" stroke-width="{OUTLINE_W}"/>
    <rect x="40" y="30" width="240" height="150" fill="{DESK}"/>
    <text x="160" y="244" font-size="15" text-anchor="middle" fill="{LABEL}">Monitor 1</text>
    <g transform="translate(262 190)">
      <circle r="6" fill="#21262d" stroke="#c9d1d9" stroke-width="1.5"/>
      {POWER_LIT}
    </g>

    <!-- windows behind, then the taskbar with the blue window's icon active -->
    {M1_BACK}
    {M1_OTHER}
    <rect x="39.5" y="160" width="241" height="20" fill="{TASKBAR}"/>
    <rect x="40" y="160" width="240" height="0.8" fill="#c9d1d9"/>
    <rect x="52" y="161" width="24" height="15" rx="2" fill="{TASK_SEL}"/>
    {ICONS_M1}
    <rect x="56" y="163" width="16" height="12" rx="2" fill="#2f81f7"/>
    <rect x="59" y="177.2" width="10" height="1.6" rx="0.8" fill="{FOCUS}"/>
    <path d="M40 160 V30 H280 V160" fill="none" stroke="#4b535d" stroke-width="1"/>

    <!-- the blue window, focused, and the size it started at -->
    <g class="grow">
      {BLUE_LIT}
    </g>
    <rect class="was" x="{BLUE[0]}" y="{BLUE[1]}" width="{BLUE[2]}" height="{BLUE[3]}" rx="5" fill="none" stroke="{ORANGE}" stroke-width="1.5" stroke-dasharray="4 3"/>
  </g>
  </g>
</svg>
"""


if __name__ == "__main__":
    here = Path(__file__).resolve().parent
    for name, args in THEMES.items():
        out = here / f"resize-window-{name}.svg"
        out.write_text(build(*args), encoding="utf-8", newline="")
        m.parse(str(out))
        print(out.name, "ok")
