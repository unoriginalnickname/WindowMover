"""Builds the "fill half the screen" animation for WindowMover, as a light and a dark SVG.

Run: python make_snap_window.py   (writes snap-window-light.svg and snap-window-dark.svg beside it)
The SVGs are generated; edit this file, not them. Times below are in seconds.

Drawn like the resize animation (make_resize_window.py, whose pieces it borrows): the mouse with Ctrl and
Shift above it, the steps, then one monitor. The blue window has focus, in the middle of the screen.
Ctrl+Shift go down, Mouse4 sends the window to the left half, Mouse5 to the right half, and Ctrl+Shift
come up. Each step's text appears only when it happens and dims once done; each holds for a few seconds
once its result shows, and a progress bar under the steps shows where the picture is.
"""
from pathlib import Path
import xml.dom.minidom as m

from make_pull_window import (EASE, ORANGE, DESK, TASKBAR, TASK_SEL, FOCUS, BEZEL, BAND, POWER_LIT,
                              ICONS_M1, M1_OTHER, BLUE_LIT, MOX, MOY, MS, at, BULLET_X, TEXT_X, ROWS,
                              row, MX, THEMES, win, HOLD, progress)
from make_resize_window import BLUE, KEY_Y, to_keys, to_rect

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
# As in the other two, each step holds for HOLD seconds once its result shows, with the keys and the
# button it used still lit, so the chain keys/mouse → step → monitor can be read before the next step.
S1 = 1.0                     # each step lights up 0.2 s after the one before it goes out
KEYS_DOWN = S1 + 0.5
E1 = KEYS_DOWN + 0.1 + HOLD
S2 = E1 + 0.2
PRESS4 = S2 + 0.4
SNAP4 = (PRESS4 + 0.1, PRESS4 + 0.6)
E2 = SNAP4[1] + HOLD
S3 = E2 + 0.2
PRESS5 = S3 + 0.4
SNAP5 = (PRESS5 + 0.1, PRESS5 + 0.6)
E3 = SNAP5[1] + HOLD
S4 = E3 + 0.2
KEYS_UP = S4 + 0.5
E4 = KEYS_UP + 0.1 + HOLD
T = E4 + 1.5
TS = f"{T:g}s infinite"      # loops; the "loop" fade hides the window's jump back to the middle
SEGMENTS = ((S1, E1), (S2, E2), (S3, E3), (S4, E4))

# The screen above the taskbar, and its halves: what WindowPlacement.Half gives
SCREEN = (40, 30, 240, 130)
LEFT = (40, 30, 120, 130)
RIGHT = (160, 30, 120, 130)

STEPS = ("Hold Ctrl+Shift", "Mouse4: left half", "Mouse5: right half", "Release Ctrl+Shift")

# Leaders to the side buttons: up over the mouse at y=94, below the keys' leader, then down its left
# side to the button. Mouse5 is the upper side button, Mouse4 the lower.
MOUSE4, MOUSE5 = at(-8, 67), at(-8, 47)


def to_side(i, button):
    return f"{BULLET_X - 6},{row(i)} 132,{row(i)} 132,94 12,94 12,{button.split(',')[1]} {button}"


TRACKS = [
    shown("step1", (S1, E1)),
    shown("step2", (S2, E2)),
    shown("step3", (S3, E3)),
    shown("step4", (S4, E4)),
    shown("done1", (E1, E4)),
    shown("done2", (E2, E4)),
    shown("done3", (E3, E4)),
    shown("keys1", (S1, E3)),
    shown("keys4", (S4, E4)),
    shown("lead4", (S2, E2)),
    shown("lead5", (S3, E3)),
    shown("keyon", (KEYS_DOWN, KEYS_UP), fade=0.1),
    shown("m4", (PRESS4, E2), fade=0.1),
    shown("m5", (PRESS5, E3), fade=0.1),
    track("snap", [(0, to_rect(BLUE), EASE), (SNAP4[0], to_rect(BLUE), EASE), (SNAP4[1], to_rect(LEFT), EASE),
                   (SNAP5[0], to_rect(LEFT), EASE), (SNAP5[1], to_rect(RIGHT))]),
    # the half the window is going to, outlined as it lands
    shown("half4", (SNAP4[0], E2)),
    shown("half5", (SNAP5[0], E3)),
    *[op(f"pg{i}", [(0, 0), (a, 0), (a + 0.2, 1), (b, 1), (b + 0.2, 0.4)]) for i, (a, b) in enumerate(SEGMENTS)],
    op("loop", [(0, 0), (0.5, 1), (T - 0.8, 1), (T, 0)]),
]
KF = "".join(TRACKS)

M1_BACK = win(48, 100, 80, 56, "#f6f8fa", "#c3d2dc")


def build(PAGE, LABEL, BRIGHT, FAINT, STAND, OUTLINE, OUTLINE_W, MOUSE, MOUSE_LINE, MOUSE_BTN):
    lead = f'fill="none" stroke="{ORANGE}" stroke-width="1.6" stroke-linejoin="round"'
    pg_css = "\n".join(f"      .pg{i} {{ animation: pg{i} {TS}; }}" for i in range(len(SEGMENTS)))
    steps = "\n    ".join(
        (f'<g class="past{i + 1}"><circle cx="{BULLET_X}" cy="{row(i)}" r="3" fill="{FAINT}"/><text x="{TEXT_X}" y="{ROWS[i]}" fill="{FAINT}">{s}</text></g>' if i < 3 else '') +
        f'<g class="st{i + 1}"><circle cx="{BULLET_X}" cy="{row(i)}" r="3.5" fill="{ORANGE}"/><text x="{TEXT_X}" y="{ROWS[i]}" fill="{BRIGHT}">{s}</text></g>'
        for i, s in enumerate(STEPS))

    def key(x, w, fill, line, ink, word):
        return (f'<rect x="{x}" y="{KEY_Y}" width="{w}" height="21" rx="4" fill="{fill}" stroke="{line}" stroke-width="1.6"/>'
                f'<text x="{x + w / 2:g}" y="{KEY_Y + 15}" fill="{ink}">{word}</text>')

    def outline(cls, r):
        return (f'<rect class="{cls}" x="{r[0] + 2}" y="{r[1] + 2}" width="{r[2] - 4}" height="{r[3] - 4}" rx="4" '
                f'fill="none" stroke="{ORANGE}" stroke-width="2" stroke-dasharray="5 4"/>')

    return f"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {W} {H}" width="{W}" height="{H}" role="img" aria-labelledby="t d">
  <title id="t">Filling half the screen with the focused window</title>
  <desc id="d">A mouse with Ctrl and Shift keys above it, four steps, and a monitor whose blue window has focus, in the middle of the screen. Ctrl and Shift are held; Mouse4 is pressed and the window fills the left half of the screen, then Mouse5 is pressed and it fills the right half. Then Ctrl and Shift are released.</desc>
  <style>
    text {{ font-family: "Segoe UI", Helvetica, Arial, sans-serif; }}
    .st1, .st2, .st3, .st4, .keys1, .keys4, .lead4, .lead5, .keyon, .m4, .m5, .half4, .half5, .pgbar {{ opacity: 0; }}
    @media (prefers-reduced-motion: no-preference) {{
      .pgbar {{ opacity: 1; }}
{pg_css}
      .past1 {{ animation: done1 {TS}; }}
      .past2 {{ animation: done2 {TS}; }}
      .past3 {{ animation: done3 {TS}; }}
      .st1 {{ animation: step1 {TS}; }}
      .st2 {{ animation: step2 {TS}; }}
      .st3 {{ animation: step3 {TS}; }}
      .st4 {{ animation: step4 {TS}; }}
      .keys1 {{ animation: keys1 {TS}; }}
      .keys4 {{ animation: keys4 {TS}; }}
      .lead4 {{ animation: lead4 {TS}; }}
      .lead5 {{ animation: lead5 {TS}; }}
      .keyon {{ animation: keyon {TS}; }}
      .m4 {{ animation: m4 {TS}; }}
      .m5 {{ animation: m5 {TS}; }}
      .snap {{ animation: snap {TS}; }}
      .half4 {{ animation: half4 {TS}; }}
      .half5 {{ animation: half5 {TS}; }}
      .loop {{ animation: loop {TS}; }}
    }}
{KF}  </style>

  <rect width="{W}" height="{H}" rx="12" fill="{PAGE}"/>
  <g class="loop">

  <!-- the mouse, seen from above: Mouse5 is the upper side button, Mouse4 the lower one -->
  <g transform="translate({MOX} {MOY}) scale({MS})">
    <rect x="0" y="0" width="70" height="96" rx="32" fill="{MOUSE}" stroke="{MOUSE_LINE}" stroke-width="2.4"/>
    <line x1="0" y1="42" x2="70" y2="42" stroke="{MOUSE_LINE}" stroke-width="2.4"/>
    <line x1="35" y1="0" x2="35" y2="42" stroke="{MOUSE_LINE}" stroke-width="2.4"/>
    <rect x="29" y="12" width="12" height="22" rx="6" fill="{MOUSE_BTN}" stroke="{MOUSE_LINE}" stroke-width="1.8"/>
    <rect x="-8" y="40" width="10" height="14" rx="3" fill="{MOUSE_BTN}" stroke="{MOUSE_LINE}" stroke-width="1.8"/>
    <rect x="-8" y="60" width="10" height="14" rx="3" fill="{MOUSE_BTN}" stroke="{MOUSE_LINE}" stroke-width="1.8"/>
    <rect class="m5" x="-10" y="38" width="14" height="18" rx="4" fill="{ORANGE}"/>
    <rect class="m4" x="-10" y="58" width="14" height="18" rx="4" fill="{ORANGE}"/>
  </g>

  <!-- Ctrl and Shift, lit while held -->
  <g font-size="12" text-anchor="middle">
    {key(26, 44, MOUSE_BTN, MOUSE_LINE, LABEL, "Ctrl")}{key(78, 56, MOUSE_BTN, MOUSE_LINE, LABEL, "Shift")}
    <g class="keyon">{key(26, 44, ORANGE, ORANGE, "#ffffff", "Ctrl")}{key(78, 56, ORANGE, ORANGE, "#ffffff", "Shift")}</g>
  </g>

  <!-- the steps, and the leaders from each step to its key or button -->
  <g font-size="15">
    <text x="{BULLET_X - 4}" y="48" font-size="16" font-weight="600" fill="{BRIGHT}">Half the screen</text>
    {steps}
  </g>
  {progress(FAINT, len(SEGMENTS))}
  <g {lead}>
    <path class="keys1" d="{to_keys(0)}"/>
    <path class="keys4" d="{to_keys(3)}"/>
    <polyline class="lead4" points="{to_side(1, MOUSE4)}"/>
    <polyline class="lead5" points="{to_side(2, MOUSE5)}"/>
  </g>

  <g transform="translate({MX} 0)">
    <!-- the monitor: stand, bezel with the power button's band, screen -->
    <g fill="{STAND}"><rect x="146" y="200" width="28" height="14"/><rect x="122" y="214" width="76" height="6" rx="3"/></g>
    <path d="{BEZEL}" fill="#000000"/><path d="{BAND}" fill="#000000"/>
    <path d="{BEZEL}" fill="none" stroke="{OUTLINE}" stroke-width="{OUTLINE_W}"/>
    <rect x="{SCREEN[0]}" y="{SCREEN[1]}" width="{SCREEN[2]}" height="150" fill="{DESK}"/>
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

    <!-- the blue window, focused, and the half it lands on -->
    <g class="snap">
      {BLUE_LIT}
    </g>
    {outline("half4", LEFT)}
    {outline("half5", RIGHT)}
  </g>
  </g>
</svg>
"""


if __name__ == "__main__":
    here = Path(__file__).resolve().parent
    for name, args in THEMES.items():
        out = here / f"snap-window-{name}.svg"
        out.write_text(build(*args), encoding="utf-8", newline="")
        m.parse(str(out))
        print(out.name, "ok")
