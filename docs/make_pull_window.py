"""Builds the "pull a window to this monitor" animation for WindowMover, as a light and a dark SVG.

Run: python make_pull_window.py   (writes pull-window-light.svg and pull-window-dark.svg beside it)
The SVGs are generated; edit this file, not them. Times below are in seconds.

The story, in two passes that run straight into each other:
  Pass 1. Two monitors, both on. The blue window on the left is unfocused and stacked behind a red
          one; a different window on the right monitor has focus. You click the blue window's icon on
          the right monitor's taskbar: focus moves to it, and you can see that on the left monitor.
          Mouse4 is held, the middle button is clicked, and the blue window slides over.
  Pass 2. The left monitor goes dark, leaving a grey dotted outline where the red window is. You click
          the red window's icon: the blue window loses focus, the red one gains it. Same buttons, and
          the red window comes out of the dark monitor.
"""
from pathlib import Path
import xml.dom.minidom as m

T = 28.5
DX = 254                     # Monitor 2's offset from Monitor 1; the gap between the bezels is DX - 246
SH = DX - 320                # how far Monitor 2's elements moved from the old 320 spacing
MARGIN = 12                  # empty space left and right of the monitors
W = (283 + DX) - 37 + 2 * MARGIN   # picture width: from Monitor 1's left bezel to Monitor 2's right bezel, plus the margins
CENTER = MARGIN - 37         # puts Monitor 1's bezel MARGIN px from the left edge
EASE = 'cubic-bezier(0.2, 0, 0.38, 0.9)'
SHOWY = 'cubic-bezier(0.4, 0.14, 0.3, 1)'
ID = 'translate(0, 0) scale(1)'
MEDIUM = f'translate({-1.6 * (66 + CENTER):g}px, -99.2px) scale(1.6)'   # shows the left window and the right monitor's taskbar icons
TS = f"{T:g}s infinite"     # loops; the "loop" track fades the picture out and back in at the seam


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


def tx(name, stops):
    """Like op(), for translateX values. A stop may carry an easing as its third item."""
    return track(name, [(s[0], f"transform: {s[1]}") + s[2:] for s in stops])


X0, X320 = 'translateX(0)', f'translateX({DX}px)'
RING_OFF, RING_ON, RING_END = "opacity: 0; transform: scale(1)", "opacity: 1; transform: scale(1)", "opacity: 0; transform: scale(3.5)"

# --- the events (seconds) ----------------------------------------------------------------
# Pass 1: the first look at the setup and the captions, so it is the slower pass
CUR1 = (1.5, 2.1)           # the cursor travels to the blue window's icon
ZIN1, ZOUT1 = (2.6, 3.0), (14.6, 15.0)
UI1 = (2.7, 3.0, 14.6, 15.0)  # caption bar in, in, out, out
S1A, S2A, S3A = 3.3, 8.0, 11.0
CLICK1, HOLD1, MIDDLE1 = 5.6, 8.8, 13.0
MOVE1, HIT1_OFF = (15.2, 15.8), 16.2
DWELL1 = {"left": 1.4, "wheel": 0.8}     # how long a lit button stays lit
# Between the passes
AGAIN = (17.2, 18.6)         # caption in the full view: "Again, with the left monitor off"
OFF = (17.6, 17.9)           # the left monitor goes dark
CUR2 = (18.2, 18.7)         # the cursor moves from the blue icon to the red one
# Pass 2: the same steps, but the viewer has seen them, so it is the quicker pass
ZIN2, ZOUT2 = (18.9, 19.3), (24.6, 25.0)
UI2 = (19.0, 19.3, 24.6, 25.0)
S1B, S2B, S3B = 19.5, 21.6, 22.9
CLICK2, HOLD2, MIDDLE2 = 20.6, 22.0, 23.8
MOVE2, HIT2_OFF = (25.2, 25.7), 26.0
DWELL2 = {"left": 0.9, "wheel": 0.5}

TRACKS = [
    track("cam", [(0, f"transform: {ID}", SHOWY), (ZIN1[0], f"transform: {ID}", SHOWY), (ZIN1[1], f"transform: {MEDIUM}", SHOWY),
                  (ZOUT1[0], f"transform: {MEDIUM}", SHOWY), (ZOUT1[1], f"transform: {ID}", SHOWY),
                  (ZIN2[0], f"transform: {ID}", SHOWY), (ZIN2[1], f"transform: {MEDIUM}", SHOWY),
                  (ZOUT2[0], f"transform: {MEDIUM}", SHOWY), (ZOUT2[1], f"transform: {ID}")]),
    track("cur", [(0, "transform: translate(146px, -89px)", EASE), (CUR1[0], "transform: translate(146px, -89px)", EASE),
                  (CUR1[1], "transform: translate(0, 0)", EASE), (CUR2[0], "transform: translate(0, 0)", EASE),
                  (CUR2[1], "transform: translate(22px, 0)")]),
    # labels and stands are not needed in the close-ups, and would show through the caption bar as it fades
    op("hidez", [(0, 1), (ZIN1[0], 1), (ZIN1[1], 0), (ZOUT1[0], 0), (ZOUT1[1], 1), (ZIN2[0], 1), (ZIN2[1], 0), (ZOUT2[0], 0), (ZOUT2[1], 1)]),
    # the captions and the mouse go first, while the black bar is still solid; the bar then dissolves with the zoom-out
    op("zoomtxt", [(0, 0), (UI1[0], 0), (UI1[1], 1), (UI1[2] - 0.3, 1), (UI1[2], 0), (UI2[0], 0), (UI2[1], 1), (UI2[2] - 0.3, 1), (UI2[2], 0)]),
    op("zoomui", [(0, 0), (UI1[0], 0), (UI1[1], 1), (UI1[2], 1), (UI1[3], 0), (UI2[0], 0), (UI2[1], 1), (UI2[2], 1), (UI2[3], 0)]),
    op("step1", [(0, 0), (S1A, 0), (S1A + 0.3, 1), (16.8, 1), (16.9, 0), (S1B, 0), (S1B + 0.3, 1)]),
    op("step2", [(0, 0), (S2A, 0), (S2A + 0.3, 1), (16.8, 1), (16.9, 0), (S2B, 0), (S2B + 0.3, 1)]),
    op("step3", [(0, 0), (S3A, 0), (S3A + 0.3, 1), (16.8, 1), (16.9, 0), (S3B, 0), (S3B + 0.3, 1)]),
    # Mouse4 held, left click, middle click: the mouse in the caption bar
    op("side", [(0, 0), (HOLD1 - 0.1, 0), (HOLD1 + 0.1, 1), (UI1[2] - 0.1, 1), (UI1[2], 0),
                (HOLD2 - 0.1, 0), (HOLD2 + 0.1, 1), (UI2[2] - 0.1, 1), (UI2[2], 0)]),
    op("wheel", [(0, 0), (MIDDLE1, 0), (MIDDLE1 + 0.05, 1), (MIDDLE1 + DWELL1["wheel"], 1), (MIDDLE1 + DWELL1["wheel"] + 0.15, 0),
                 (MIDDLE2, 0), (MIDDLE2 + 0.05, 1), (MIDDLE2 + DWELL2["wheel"], 1), (MIDDLE2 + DWELL2["wheel"] + 0.15, 0)]),
    op("left", [(0, 0), (CLICK1, 0), (CLICK1 + 0.1, 1), (CLICK1 + DWELL1["left"], 1), (CLICK1 + DWELL1["left"] + 0.2, 0),
                (CLICK2, 0), (CLICK2 + 0.1, 1), (CLICK2 + DWELL2["left"], 1), (CLICK2 + DWELL2["left"] + 0.2, 0)]),
    # click rings: pass 1 on the blue icon, pass 2 on the red icon
    track("ring1", [(0, RING_OFF), (CLICK1, RING_OFF), (CLICK1 + 0.05, RING_ON), (CLICK1 + DWELL1["left"] + 0.2, RING_END)]),
    track("ring", [(0, RING_OFF), (MIDDLE1, RING_OFF), (MIDDLE1 + 0.05, RING_ON), (MIDDLE1 + DWELL1["wheel"] + 0.3, RING_END)]),
    track("ring1b", [(0, RING_OFF), (CLICK2, RING_OFF), (CLICK2 + 0.05, RING_ON), (CLICK2 + DWELL2["left"] + 0.2, RING_END)]),
    track("ringb", [(0, RING_OFF), (MIDDLE2, RING_OFF), (MIDDLE2 + 0.05, RING_ON), (MIDDLE2 + DWELL2["wheel"] + 0.3, RING_END)]),
    # focus: the blue icon's active state (box, bar, outline), the red icon's, and the other window's on monitor 2
    op("sel", [(0, 0), (CLICK1, 0), (CLICK1 + 0.2, 1), (CLICK2, 1), (CLICK2 + 0.3, 0)]),
    op("selr", [(0, 0), (CLICK2, 0), (CLICK2 + 0.2, 1)]),
    op("xact", [(0, 1), (CLICK1, 1), (CLICK1 + 0.3, 0)]),
    # the blue window: unfocused, then focused at the first click, unfocused again at the second
    op("windim", [(0, 1), (CLICK1, 1), (CLICK1 + 0.3, 0), (CLICK2, 0), (CLICK2 + 0.3, 1)]),
    op("winvis", [(0, 0), (CLICK1, 0), (CLICK1 + 0.3, 1), (CLICK2, 1), (CLICK2 + 0.3, 0)]),
    tx("hop", [(0, X0, EASE), (MOVE1[0], X0, EASE), (MOVE1[1], X320)]),
    op("landed", [(0, 0), (MIDDLE1, 0), (MIDDLE1 + 0.05, 1), (HIT1_OFF, 1), (HIT1_OFF + 0.25, 0)]),
    # the left monitor goes dark
    op("off1", [(0, 0), (OFF[0], 0), (OFF[1], 1)]),
    op("led1on", [(0, 1), (OFF[0], 1), (OFF[1], 0)]),
    op("again", [(0, 0), (AGAIN[0], 0), (AGAIN[0] + 0.3, 1), (AGAIN[1], 1), (AGAIN[1] + 0.15, 0)]),
    # the red window: a grey dotted outline on the dark monitor, an orange highlight at the middle click, the real window fading in as it slides
    op("ghost2", [(0, 0), (OFF[1], 0), (OFF[1] + 0.3, 1), (CLICK2, 1), (CLICK2 + 0.3, 0)]),
    op("ghostf", [(0, 0), (CLICK2, 0), (CLICK2 + 0.3, 1), (MOVE2[0], 1), (MOVE2[0] + 0.6, 0)]),
    op("redvis", [(0, 0), (MOVE2[0], 0), (MOVE2[0] + 0.5, 1)]),
    tx("hop2", [(0, X0, EASE), (MOVE2[0], X0, EASE), (MOVE2[1], X320)]),
    op("landed2", [(0, 0), (MIDDLE2, 0), (MIDDLE2 + 0.05, 1), (HIT2_OFF, 1), (HIT2_OFF + 0.25, 0)]),
    # fades the whole picture in at the start and out at the end, so the loop restarts without a jump
    op("loop", [(0, 0), (0.5, 1), (T - 0.8, 1), (T, 0)]),
]
KF = "".join(TRACKS)


# --- Background life: muted windows and taskbar icons that stay out of the way. -----------
# Only the two targets (blue, then red) use saturated colour and the white outline.
TEAL, MAUVE, OLIVE, SLATE, DUSTY_RED = "#6d8794", "#8c7d99", "#95916c", "#7b8794", "#a8686c"
INK = "#1c2128"


def glyph(kind, x):
    """A small mark inside a 16x12 taskbar icon whose left edge is x (top at y=163)."""
    if kind == "bars":
        return f'<rect x="{x+3}" y="166" width="10" height="1.5" fill="{INK}" opacity="0.55"/><rect x="{x+3}" y="169.5" width="6" height="1.5" fill="{INK}" opacity="0.55"/>'
    if kind == "dot":
        return f'<circle cx="{x+8}" cy="169" r="3" fill="none" stroke="{INK}" stroke-width="1.5" opacity="0.55"/>'
    if kind == "tri":
        return f'<polygon points="{x+5},172.5 {x+8},165.5 {x+11},172.5" fill="{INK}" opacity="0.55"/>'
    return "".join(f'<rect x="{x+4+6*i}" y="{166+4*j}" width="3" height="3" fill="{INK}" opacity="0.55"/>' for i in range(2) for j in range(2))


def icons(xs, spec, running):
    out = []
    for i, (x, (colour, kind)) in enumerate(zip(xs, spec)):
        out.append(f'<rect x="{x}" y="163" width="16" height="12" rx="2" fill="{colour}"/>{glyph(kind, x)}')
        if i in running:
            out.append(f'<rect x="{x+4}" y="177.2" width="8" height="1.2" rx="0.6" fill="#8b949e" opacity="0.7"/>')
    return "\n      ".join(out)


def win(x, y, w, h, fill, bar):
    """A background window: muted fill, darker title bar, faint content bars, a faint edge."""
    return (f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="3" fill="{fill}"/>'
            f'<rect x="{x}" y="{y}" width="{w}" height="10" rx="3" fill="{bar}"/><rect x="{x}" y="{y+6}" width="{w}" height="4" fill="{bar}"/>'
            f'<rect x="{x+8}" y="{y+17}" width="{w*6//10}" height="4" rx="2" fill="#ffffff" opacity="0.22"/>'
            f'<rect x="{x+8}" y="{y+26}" width="{w*8//10-8}" height="4" rx="2" fill="#ffffff" opacity="0.22"/>'
            f'<rect x="{x+8}" y="{y+35}" width="{w*4//10}" height="4" rx="2" fill="#ffffff" opacity="0.22"/>'
            f'<rect x="{x+0.5}" y="{y+0.5}" width="{w-1}" height="{h-1}" rx="2.5" fill="none" stroke="#9aa5b1" stroke-opacity="0.35"/>')


def focused(x, y, w, h):
    """Drawn over a background window while it has focus: a brighter title bar and a white outline."""
    return (f'<rect x="{x}" y="{y}" width="{w}" height="10" rx="3" fill="#6fa3c7"/><rect x="{x}" y="{y+6}" width="{w}" height="4" fill="#6fa3c7"/>'
            f'<rect x="{x+0.75}" y="{y+0.75}" width="{w-1.5}" height="{h-1.5}" rx="2.5" fill="none" stroke="#ffffff" stroke-width="1.5"/>')


# The story needs "show taskbar buttons on all taskbars" (the blue window's icon is clicked on the other
# monitor), so both taskbars carry the same buttons in the same order, and light up together.
TASKBAR_SPEC = ((DUSTY_RED, "bars"), (TEAL, "grid"), (MAUVE, "dot"), (SLATE, "tri"))
ICONS_M1 = icons((78, 100, 122, 144), TASKBAR_SPEC, {1, 3})
ICONS_M2 = icons((398 + SH, 420 + SH, 442 + SH, 464 + SH), TASKBAR_SPEC, {1, 3})

M1_BEHIND = win(52, 42, 96, 62, "#3f5260", "#364856")        # monitor 1: behind the blue window
M1_RED = win(176, 98, 92, 58, "#6b454a", "#5b3a3e")          # monitor 1: the red window, stacked in front of the blue one
M2_FOCUSED = win(368 + SH, 100, 80, 56, "#3f5260", "#364856")     # monitor 2: has focus at the start
M2_FOCUSED_ON = focused(368 + SH, 100, 80, 56)
M2_OTHER = win(462 + SH, 38, 100, 50, "#54495f", "#493f54")

BLUE_DIM = '''<rect x="100" y="70" width="120" height="80" rx="5" fill="#5b779f"/>
        <rect x="100" y="70" width="120" height="18" rx="5" fill="#4d6890"/>
        <rect x="100" y="80" width="120" height="8" fill="#4d6890"/>
        <g fill="#ffffff" opacity="0.55"><circle cx="112" cy="79" r="3"/><circle cx="123" cy="79" r="3"/><circle cx="134" cy="79" r="3"/></g>
        <rect x="110" y="100" width="70" height="6" rx="3" fill="#ffffff" opacity="0.4"/>
        <rect x="110" y="114" width="90" height="6" rx="3" fill="#ffffff" opacity="0.3"/>
        <rect x="110" y="128" width="55" height="6" rx="3" fill="#ffffff" opacity="0.3"/>
        <rect x="100.5" y="70.5" width="119" height="79" rx="4.5" fill="none" stroke="#9aa5b1" stroke-opacity="0.35"/>'''
BLUE_LIT = '''<rect x="100" y="70" width="120" height="80" rx="5" fill="#2f81f7"/>
        <rect x="100" y="70" width="120" height="18" rx="5" fill="#1158c7"/>
        <rect x="100" y="80" width="120" height="8" fill="#1158c7"/>
        <rect x="100" y="88" width="120" height="1" fill="#a5d6ff" opacity="0.7"/>
        <g fill="#ffffff"><circle cx="112" cy="79" r="3"/><circle cx="123" cy="79" r="3"/><circle cx="134" cy="79" r="3"/></g>
        <rect x="110" y="100" width="70" height="6" rx="3" fill="#ffffff" opacity="0.85"/>
        <rect x="110" y="114" width="90" height="6" rx="3" fill="#ffffff" opacity="0.6"/>
        <rect x="110" y="128" width="55" height="6" rx="3" fill="#ffffff" opacity="0.6"/>
        <rect x="100.75" y="70.75" width="118.5" height="78.5" rx="4.5" fill="none" stroke="#ffffff" stroke-width="1.5"/>'''
RED_LIT = '''<rect x="176" y="98" width="92" height="58" rx="5" fill="#d6454b"/>
        <rect x="176" y="98" width="92" height="14" rx="5" fill="#a8282f"/>
        <rect x="176" y="106" width="92" height="6" fill="#a8282f"/>
        <rect x="176" y="112" width="92" height="1" fill="#ffd0d2" opacity="0.7"/>
        <g fill="#ffffff"><circle cx="186" cy="105" r="2.5"/><circle cx="195" cy="105" r="2.5"/><circle cx="204" cy="105" r="2.5"/></g>
        <rect x="184" y="122" width="52" height="5" rx="2.5" fill="#ffffff" opacity="0.85"/>
        <rect x="184" y="132" width="68" height="5" rx="2.5" fill="#ffffff" opacity="0.6"/>
        <rect x="184" y="142" width="40" height="5" rx="2.5" fill="#ffffff" opacity="0.6"/>
        <rect x="176.75" y="98.75" width="90.5" height="56.5" rx="4.5" fill="none" stroke="#ffffff" stroke-width="1.5"/>'''

BEZEL = "M40 27 H280 A3 3 0 0 1 283 30 V197 A3 3 0 0 1 280 200 H40 A3 3 0 0 1 37 197 V30 A3 3 0 0 1 40 27 Z"
BAND = "M37 180 H283 V197 A3 3 0 0 1 280 200 H40 A3 3 0 0 1 37 197 Z"
POWER = '<path d="M-3.6 -3.6 A5.2 5.2 0 1 0 3.6 -3.6"/><line x1="0" y1="-6.2" x2="0" y2="-1"/>'
POWER_LIT = f'''<circle r="7.6" fill="none" stroke="#58a6ff" stroke-opacity="0.4" stroke-width="1.8"/>
      <circle r="5.2" fill="#12304f"/>
      <g transform="scale(0.66)" fill="none" stroke="#8fd0ff" stroke-width="2.6" stroke-linecap="round">{POWER}</g>'''
# The caption bar covers the bottom of the picture while the camera is zoomed, with rounded bottom corners to match the page.
BAR = f"M0 190 H{W} V278 A12 12 0 0 1 {W - 12} 290 H12 A12 12 0 0 1 0 278 Z"


def build(PAGE, LABEL, STAND, OUTLINE, OUTLINE_W, TASKBAR, OFF_SCREEN):
    return f"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {W} 290" width="{W}" height="290" role="img" aria-labelledby="t d">
  <title id="t">Pulling a window to the monitor you are on</title>
  <desc id="d">Two monitors. On the left, a blue window is unfocused and stacked behind a red one; on the right, a different window has focus. The cursor clicks the blue window's icon on the right monitor's taskbar, and focus moves to the blue window on the left. The mouse's back side button (Mouse4) is held and the middle button is clicked, and the blue window slides to the right monitor. Then the left monitor is turned off, leaving a grey dotted outline where the red window is. The red window's icon is clicked, and with the same buttons the red window slides out of the dark monitor to the right one.</desc>
  <style>
    text {{ font-family: "Segoe UI", Helvetica, Arial, sans-serif; fill: {LABEL}; }}
    .step, .num {{ fill: #c9d1d9; }}
    .num {{ font-weight: 700; }}
    .numon {{ fill: #000000; }}
    .cam {{ transform-origin: 0 0; }}
    .win, .win2 {{ transform: translateX({DX}px); }}
    .cur {{ transform: translate(22px, 0); }}
    .selr, .off1, .windim, .redvis {{ opacity: 1; }}
    .hit, .hit2, .zoomui, .zoomtxt, .step, .ring, .ring1, .ringb, .ring1b, .left, .xact, .sel, .winvis, .ghost2, .ghostf, .led1on, .again {{ opacity: 0; }}
    .ring, .ring1, .ringb, .ring1b {{ transform-box: fill-box; transform-origin: center; }}
    @media (prefers-reduced-motion: no-preference) {{
      .cam    {{ animation: cam {TS}; }}
      .cur    {{ animation: cur {TS}; }}
      .win    {{ animation: hop {TS}; }}
      .win2   {{ animation: hop2 {TS}; }}
      .hit    {{ animation: landed {TS}; }}
      .hit2   {{ animation: landed2 {TS}; }}
      .winvis {{ animation: winvis {TS}; }}
      .windim {{ animation: windim {TS}; }}
      .redvis {{ animation: redvis {TS}; }}
      .ghost2 {{ animation: ghost2 {TS}; }}
      .ghostf {{ animation: ghostf {TS}; }}
      .xact   {{ animation: xact {TS}; }}
      .sel    {{ animation: sel {TS}; }}
      .selr   {{ animation: selr {TS}; }}
      .off1   {{ animation: off1 {TS}; }}
      .led1on {{ animation: led1on {TS}; }}
      .again  {{ animation: again {TS}; }}
      .zoomui {{ animation: zoomui {TS}; }}
      .zoomtxt {{ animation: zoomtxt {TS}; }}
      .hidez  {{ animation: hidez {TS}; }}
      .loop   {{ animation: loop {TS}; }}
      .s1 {{ animation: step1 {TS}; }}
      .s2 {{ animation: step2 {TS}; }}
      .s3 {{ animation: step3 {TS}; }}
      .side  {{ animation: side {TS}; }}
      .wheel {{ animation: wheel {TS}; }}
      .ring  {{ animation: ring {TS}; }}
      .ring1 {{ animation: ring1 {TS}; }}
      .ringb {{ animation: ringb {TS}; }}
      .ring1b {{ animation: ring1b {TS}; }}
      .left  {{ animation: left {TS}; }}
    }}
{KF}  </style>

  <defs>
    <clipPath id="s1"><rect x="40" y="30" width="240" height="150"/></clipPath>
    <clipPath id="s2"><rect x="{40 + DX}" y="30" width="240" height="150"/></clipPath>
  </defs>
  <rect width="{W}" height="290" rx="12" fill="{PAGE}"/>
  <g class="loop">

  <g class="cam"><g transform="translate({CENTER} 0)">
    <!-- monitors: stand, a thin black bezel with a taller bottom band that holds the power button, then the screen -->
    <g class="hidez" fill="{STAND}">
      <rect x="146" y="200" width="28" height="14"/><rect x="122" y="214" width="76" height="6" rx="3"/>
      <rect x="{146 + DX}" y="200" width="28" height="14"/><rect x="{122 + DX}" y="214" width="76" height="6" rx="3"/>
    </g>
    <g fill="#000000">
      <path d="{BEZEL}"/>
      <path transform="translate({DX} 0)" d="{BEZEL}"/>
      <path d="{BAND}"/>
      <path transform="translate({DX} 0)" d="{BAND}"/>
    </g>
    <g fill="none" stroke="{OUTLINE}" stroke-width="{OUTLINE_W}">
      <path d="{BEZEL}"/>
      <path transform="translate({DX} 0)" d="{BEZEL}"/>
    </g>
    <g fill="#4a5360">
      <rect x="40"  y="30" width="240" height="150"/>
      <rect x="{40 + DX}" y="30" width="240" height="150"/>
    </g>
    <g class="hidez" font-size="18" text-anchor="middle">
      <text x="160" y="244">Monitor 1</text>
      <text x="{160 + DX}" y="244">Monitor 2</text>
    </g>

    <!-- power buttons at each monitor's bottom-right corner. Monitor 1's lights down when it is turned off. -->
    <g transform="translate(262 190)">
      <circle r="6" fill="#21262d" stroke="#c9d1d9" stroke-width="1.5"/>
      <g transform="scale(0.66)" fill="none" stroke="#6e7681" stroke-width="2.2" stroke-linecap="round">{POWER}</g>
      <g class="led1on">
        {POWER_LIT}
      </g>
    </g>
    <g transform="translate({262 + DX} 190)">
      <circle r="6" fill="#21262d" stroke="#c9d1d9" stroke-width="1.5"/>
      {POWER_LIT}
    </g>

    <!-- windows. Monitor 2 first, then monitor 1 bottom to top: one behind the blue window, the blue window unfocused, the red one in front.
         The blue window is drawn after monitor 2's windows so it stays on top of them when it lands there. -->
    {M2_FOCUSED}
    <g class="xact">{M2_FOCUSED_ON}</g>
    {M2_OTHER}
    {M1_BEHIND}
    <g class="win">
      <g class="windim">
        {BLUE_DIM}
      </g>
    </g>
    {M1_RED}

    <!-- taskbars, the same buttons on both (see TASKBAR_SPEC). The blue icon is the blue window's, the red one the red window's. -->
    <rect x="40" y="160" width="240" height="20" fill="{TASKBAR}" clip-path="url(#s1)"/>
    <rect x="{40 + DX}" y="160" width="240" height="20" fill="{TASKBAR}" clip-path="url(#s2)"/>
    <rect class="sel" x="{372 + SH}" y="161" width="24" height="15" rx="2" fill="#4a5360"/>
    <rect class="selr" x="{394 + SH}" y="161" width="24" height="15" rx="2" fill="#4a5360"/>
    <rect class="xact" x="{416 + SH}" y="161" width="24" height="15" rx="2" fill="#4a5360"/>
    <rect class="sel" x="52" y="161" width="24" height="15" rx="2" fill="#4a5360"/>
    <rect class="selr" x="74" y="161" width="24" height="15" rx="2" fill="#4a5360"/>
    <rect class="xact" x="96" y="161" width="24" height="15" rx="2" fill="#4a5360"/>
    <g>
      {ICONS_M1}
      {ICONS_M2}
    </g>
    <rect x="56" y="163" width="16" height="12" rx="2" fill="#2f81f7"/>
    <rect x="{376 + SH}" y="163" width="16" height="12" rx="2" fill="#2f81f7"/>
    <rect class="sel" x="{376.5 + SH}" y="163.5" width="15" height="11" rx="1.5" fill="none" stroke="#ffffff" stroke-width="1"/>
    <rect class="sel" x="{379 + SH}" y="177.2" width="10" height="1.6" rx="0.8" fill="#ffffff"/>
    <rect class="selr" x="{398 + SH}" y="163" width="16" height="12" rx="2" fill="#e5484d"/>
    <rect class="selr" x="{398.5 + SH}" y="163.5" width="15" height="11" rx="1.5" fill="none" stroke="#ffffff" stroke-width="1"/>
    <rect class="selr" x="{401 + SH}" y="177.2" width="10" height="1.6" rx="0.8" fill="#ffffff"/>
    <rect class="xact" x="{420.5 + SH}" y="163.5" width="15" height="11" rx="1.5" fill="none" stroke="#ffffff" stroke-width="1"/>
    <rect class="xact" x="{423 + SH}" y="177.2" width="10" height="1.6" rx="0.8" fill="#ffffff"/>
    <rect class="sel" x="56.5" y="163.5" width="15" height="11" rx="1.5" fill="none" stroke="#ffffff" stroke-width="1"/>
    <rect class="sel" x="59" y="177.2" width="10" height="1.6" rx="0.8" fill="#ffffff"/>
    <rect class="selr" x="78" y="163" width="16" height="12" rx="2" fill="#e5484d"/>
    <rect class="selr" x="78.5" y="163.5" width="15" height="11" rx="1.5" fill="none" stroke="#ffffff" stroke-width="1"/>
    <rect class="selr" x="81" y="177.2" width="10" height="1.6" rx="0.8" fill="#ffffff"/>
    <rect class="xact" x="100.5" y="163.5" width="15" height="11" rx="1.5" fill="none" stroke="#ffffff" stroke-width="1"/>
    <rect class="xact" x="103" y="177.2" width="10" height="1.6" rx="0.8" fill="#ffffff"/>

    <!-- monitor 1 turned off: a dark screen over everything on it, and the rest of the screen's edge line -->
    <rect class="off1" x="40" y="30" width="240" height="150" fill="{OFF_SCREEN}"/>

    <!-- thin grey edge line around each screen above the taskbar, so the taskbar runs straight into the bezel -->
    <g fill="none" stroke="#4b535d" stroke-width="1">
      <path d="M40 160 V30 H280 V160"/>
      <path d="M{40 + DX} 160 V30 H{280 + DX} V160"/>
      <path class="off1" d="M40 160 V180 H280 V160"/>
    </g>

    <!-- the blue window, focused: it comes in front of the others when its icon is clicked, then carries the orange highlight and slides over -->
    <g class="win">
      <g class="winvis">
        {BLUE_LIT}
      </g>
      <rect class="hit" x="96" y="66" width="128" height="88" rx="8" fill="none" stroke="#f0883e" stroke-width="4"/>
    </g>

    <!-- the red window on the dark monitor: a grey dotted outline, an orange highlight at the middle click, the real window fading in as it slides -->
    <g class="win2">
      <rect class="ghost2" x="176" y="98" width="92" height="58" rx="5" fill="none" stroke="#8b949e" stroke-width="2" stroke-dasharray="2 5" stroke-linecap="round"/>
      <g class="ghostf">
        <rect x="174" y="96" width="96" height="62" rx="7" fill="none" stroke="#ffffff" stroke-opacity="0.25" stroke-width="3"/>
        <rect x="176.75" y="98.75" width="90.5" height="56.5" rx="4.5" fill="none" stroke="#ffffff" stroke-width="1.5"/>
        <rect x="177" y="112" width="90" height="1" fill="#ffffff" opacity="0.6"/>
      </g>
      <g class="redvis">
        {RED_LIT}
      </g>
      <rect class="hit2" x="172" y="94" width="100" height="66" rx="8" fill="none" stroke="#f0883e" stroke-width="4"/>
    </g>

    <!-- click rings (blue icon, then red icon) and the cursor that travels between them -->
    <circle class="ring1" cx="{384 + SH}" cy="169" r="3" fill="none" stroke="#f0883e" stroke-width="1"/>
    <circle class="ring" cx="{384 + SH}" cy="169" r="3" fill="none" stroke="#f0883e" stroke-width="1"/>
    <circle class="ring1b" cx="{406 + SH}" cy="169" r="3" fill="none" stroke="#f0883e" stroke-width="1"/>
    <circle class="ringb" cx="{406 + SH}" cy="169" r="3" fill="none" stroke="#f0883e" stroke-width="1"/>
    <g class="cur"><polygon points="0,0 0,12 3,9 5.5,14 7.5,13 5,8 9,8" transform="translate({384 + SH} 169)" fill="#ffffff" stroke="#0d1117" stroke-width="0.6"/></g>
  </g></g>

  <!-- said between the passes, in the full view -->
  <text class="again" x="{W // 2}" y="275" font-size="17" text-anchor="middle">Again, with the left monitor off</text>

  <!-- while the camera is zoomed: a caption bar with what to press -->
  <g class="zoomui">
    <path d="{BAR}" fill="#000000"/>
    <g class="zoomtxt">
    <g font-size="19">
      <text class="step s1" x="24" y="218">1. Click the icon: the window gets focus</text>
      <text class="step s2" x="24" y="245">2. Hold Mouse4: select the pull action</text>
      <text class="step s3" x="24" y="272">3. Middle-click: activate the action</text>
    </g>
    <g transform="translate(440 195) scale(0.92)">
      <rect x="0" y="0" width="70" height="96" rx="32" fill="#161b22" stroke="#c9d1d9" stroke-width="3"/>
      <line x1="0" y1="42" x2="70" y2="42" stroke="#c9d1d9" stroke-width="3"/>
      <line x1="35" y1="0" x2="35" y2="42" stroke="#c9d1d9" stroke-width="3"/>
      <rect x="29" y="12" width="12" height="22" rx="6" fill="#30363d" stroke="#c9d1d9" stroke-width="2"/>
      <path class="left" d="M35 0 H32 A32 32 0 0 0 0 32 V42 H35 Z" fill="#f0883e"/>
      <rect class="wheel" x="26" y="9" width="18" height="28" rx="9" fill="#f0883e"/>
      <rect x="-8" y="40" width="10" height="14" rx="3" fill="#30363d" stroke="#c9d1d9" stroke-width="2"/>
      <rect x="-8" y="60" width="10" height="14" rx="3" fill="#30363d" stroke="#c9d1d9" stroke-width="2"/>
      <rect class="side" x="-12" y="58" width="18" height="18" rx="4" fill="#f0883e"/>
      <circle cx="15" cy="27" r="7.5" fill="#000000" stroke="#c9d1d9" stroke-width="1.5"/><text class="num" x="15" y="30.6" font-size="10" text-anchor="middle">1</text><circle cx="-22" cy="67" r="7.5" fill="#000000" stroke="#c9d1d9" stroke-width="1.5"/><text class="num" x="-22" y="70.6" font-size="10" text-anchor="middle">2</text><circle cx="35" cy="22" r="7.5" fill="#000000" stroke="#c9d1d9" stroke-width="1.5"/><text class="num" x="35" y="25.6" font-size="10" text-anchor="middle">3</text>
      <g class="left"><circle cx="15" cy="27" r="7.5" fill="#f0883e" stroke="#ffffff" stroke-width="1.5"/><text class="num numon" x="15" y="30.6" font-size="10" text-anchor="middle">1</text></g><g class="side"><circle cx="-22" cy="67" r="7.5" fill="#f0883e" stroke="#ffffff" stroke-width="1.5"/><text class="num numon" x="-22" y="70.6" font-size="10" text-anchor="middle">2</text></g><g class="wheel"><circle cx="35" cy="22" r="7.5" fill="#f0883e" stroke="#ffffff" stroke-width="1.5"/><text class="num numon" x="35" y="25.6" font-size="10" text-anchor="middle">3</text></g>
    </g>
    </g>
  </g>
  </g>
</svg>
"""


THEMES = {
    # name: (page, labels, stand, bezel outline, outline width, taskbar, off screen)
    "light": ("#ffffff", "#1f2328", "#57606a", "#000000", 1, "#1c2128", "#05070a"),
    "dark":  ("#0d1117", "#c9d1d9", "#8b949e", "#c9d1d9", 1.5, "#262c34", "#10141a"),
}

if __name__ == "__main__":
    here = Path(__file__).resolve().parent
    for name, args in THEMES.items():
        out = here / f"pull-window-{name}.svg"
        out.write_text(build(*args), encoding="utf-8", newline="")
        m.parse(str(out))
        print(out.name, "ok")
