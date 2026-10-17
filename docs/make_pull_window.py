"""Builds the "pull a window to this monitor" animation for WindowMover, as a light and a dark SVG.

Run: python make_pull_window.py   (writes pull-window-light.svg and pull-window-dark.svg beside it)
The SVGs are generated; edit this file, not them. Times below are in seconds.

The picture reads left to right: the mouse, the steps, the two monitors. The cursor is on the left
monitor, so that is where windows are pulled to. Each step is a few words with an orange leader to the
mouse button it uses. A step's text appears only when that step happens, dims once it is done and is
cleared at the end of its pass, so there is never a list to read ahead; nothing zooms and nothing is numbered.
Each step holds for a few seconds once its result shows (HOLD), with the button it used still lit and, for
a click or press, a decal where it landed (the cursor turns into a pointing hand, rays at its fingertip), so the chain mouse → step → monitors can be read before the next
step. A segmented progress bar under the steps shows where in the flow the picture is.

The story, in two passes under their own heading:
  Pass 1. "Both monitors on". The blue window is on the right monitor, unfocused, behind a red one; a
          window on the left monitor has focus. Click the blue window's icon on the left monitor's
          taskbar (focus moves to it), hold Mouse4, middle-click: the blue window comes over. Release.
  Pass 2. "Right monitor off". A finger presses the right monitor's power button and its
          screen goes black, leaving a grey dotted outline where the red window is. The same four
          steps bring the red window out of the dark monitor. Then the cursor leaves, ending the loop.
"""
from math import cos, radians, sin
from pathlib import Path
import xml.dom.minidom as m

DX = 254                    # Monitor 2's offset from Monitor 1, in the monitors' own coordinates
MX = 407                     # where the monitors start: Monitor 1's bezel (x=37 in its own coordinates) lands at 444
W, H = 283 + DX + MX + 16, 262
EASE = 'cubic-bezier(0.2, 0, 0.38, 0.9)'
ORANGE = "#f0883e"


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
    """Opacity 0, except 1 during each (start, end) span, fading in and out over `fade`."""
    stops = []
    for a, b in spans:
        stops += [(a, 0), (a + fade, 1), (b, 1), (b + fade, 0)]
    return op(name, stops)


def tx(name, stops):
    """Like op(), for transform values. A stop may carry an easing as its third item."""
    return track(name, [(s[0], f"transform: {s[1]}") + s[2:] for s in stops])


X0, XDX = 'translateX(0)', f'translateX({DX}px)'
RING_OFF, RING_ON, RING_END = "opacity: 0; transform: scale(1)", "opacity: 1; transform: scale(1)", "opacity: 0; transform: scale(3.5)"

# --- the events (seconds) ----------------------------------------------------------------
# Every step holds still for HOLD seconds once its result is on the screen, so the whole chain can be
# read in turn: the mouse button, the lit step, then what the monitors show. Clicks and presses leave a
# decal where they landed for the same time. The 4.5 + 1 + 4 segments of the progress bar follow along.
HOLD = 3.5
# Pass 1
S1A = 1.0                    # each step lights up 0.2 s after the one before it goes out
CUR1 = (S1A, S1A + 0.6)      # the cursor travels to the blue window's icon
CLICK1 = S1A + 0.8
E1A = CLICK1 + 0.3 + HOLD    # when step 1 goes out
S2A = E1A + 0.2
HOLD1 = S2A + 0.4
E2A = HOLD1 + 0.1 + HOLD
S3A = E2A + 0.2
MIDDLE1 = S3A + 0.4
MOVE1 = (MIDDLE1 + 0.5, MIDDLE1 + 1.1)
E3A = MOVE1[1] + HOLD
S4A = E3A + 0.2
RELEASE1 = S4A + 0.4
E4A = RELEASE1 + 0.1 + HOLD
# Between the passes: the heading changes, then a finger turns the right monitor off
HEAD = E4A + 0.2
SP = HEAD + 0.2
FINGER = (SP + 0.2, SP + 0.8, SP + 1.4, SP + 2.0)   # in, pressing, lifting, gone
PRESS = FINGER[1]
OFF = (PRESS + 0.1, PRESS + 0.4)
EP = OFF[1] + HOLD
# Pass 2, the same steps
S1B = EP + 0.2
CUR2 = (S1B, S1B + 0.5)      # the cursor moves from the blue icon to the red one
CLICK2 = S1B + 0.7
E1B = CLICK2 + 0.3 + HOLD
S2B = E1B + 0.2
HOLD2 = S2B + 0.4
E2B = HOLD2 + 0.1 + HOLD
S3B = E2B + 0.2
MIDDLE2 = S3B + 0.4
MOVE2 = (MIDDLE2 + 0.5, MIDDLE2 + 1.1)
E3B = MOVE2[1] + HOLD
S4B = E3B + 0.2
RELEASE2 = S4B + 0.4
E4B = RELEASE2 + 0.1 + HOLD
CUR_OUT = (E4B, E4B + 0.8)   # the cursor leaves the screen: that was the end
T = E4B + 1.8
TS = f"{T:g}s infinite"      # loops; the "loop" track fades the picture out and back in at the seam

# The progress bar's segments: pass 1's four steps, the power button, pass 2's four steps
SEGMENTS = ((S1A, E1A), (S2A, E2A), (S3A, E3A), (S4A, E4A), (SP, EP), (S1B, E1B), (S2B, E2B), (S3B, E3B), (S4B, E4B))

# The clicks, each held to the end of its step: the left clicks on the icons, the middle clicks
TAPS = ((CLICK1, E1A), (MIDDLE1, E3A), (CLICK2, E1B), (MIDDLE2, E3B))

FINGER_AWAY = "opacity: 0; transform: translate(34px, 70px)"
TRACKS = [
    track("cur", [(0, "opacity: 1; transform: translate(96px, -84px)", EASE), (CUR1[0], "opacity: 1; transform: translate(96px, -84px)", EASE),
                  (CUR1[1], "opacity: 1; transform: translate(0, 0)", EASE), (CUR2[0], "opacity: 1; transform: translate(0, 0)", EASE),
                  (CUR2[1], "opacity: 1; transform: translate(22px, 0)", EASE), (CUR_OUT[0], "opacity: 1; transform: translate(22px, 0)", EASE),
                  (CUR_OUT[1], "opacity: 0; transform: translate(-60px, 70px)")]),
    op("head1", [(0, 1), (HEAD, 1), (HEAD + 0.2, 0)]),
    op("head2", [(0, 0), (HEAD + 0.2, 0), (HEAD + 0.4, 1)]),
    # the steps: each appears lit as it happens, stays faint once done, and the pass's end clears them all
    shown("step1", (S1A, E1A), (S1B, E1B)),
    shown("step2", (S2A, E2A), (S2B, E2B)),
    shown("step3", (S3A, E3A), (S3B, E3B)),
    shown("step4", (S4A, E4A), (S4B, E4B)),
    shown("done1", (E1A, E4A), (E1B, E4B)),
    shown("done2", (E2A, E4A), (E2B, E4B)),
    shown("done3", (E3A, E4A), (E3B, E4B)),
    # leaders to the mouse: Mouse4's stays up while it is held, through the middle click
    shown("lead1", (S1A, E1A), (S1B, E1B)),
    shown("lead2", (S2A, E3A), (S2B, E3B)),
    shown("lead3", (S3A, E3A), (S3B, E3B)),
    shown("lead4", (S4A, E4A), (S4B, E4B)),
    # leaders to the screen: step 1 to the icon clicked, step 3 the path the window took
    shown("icon1", (S1A, E1A)),
    shown("icon2", (S1B, E1B)),
    shown("path1", (MIDDLE1, E3A)),
    shown("path2", (MIDDLE2, E3B)),
    # the mouse's buttons stay lit to the end of their step: Mouse4 while held, the left and middle clicks
    shown("side", (HOLD1, RELEASE1), (HOLD2, RELEASE2), fade=0.1),
    shown("left", (CLICK1, E1A), (CLICK2, E1B), fade=0.1),
    shown("wheel", (MIDDLE1, E3A), (MIDDLE2, E3B), fade=0.1),
    # click rings on the icons as they are clicked, then a decal held where each click or press landed
    track("ring1", [(0, RING_OFF), (CLICK1, RING_OFF), (CLICK1 + 0.05, RING_ON), (CLICK1 + 1.1, RING_END)]),
    track("ring1b", [(0, RING_OFF), (CLICK2, RING_OFF), (CLICK2 + 0.05, RING_ON), (CLICK2 + 0.9, RING_END)]),
    shown("tap1", *TAPS[:2], fade=0.1),
    shown("tap2", *TAPS[2:], fade=0.1),
    shown("tapp", (PRESS, EP), fade=0.1),
    # the cursor is a pointing hand while a click's decal is up, an arrow otherwise
    shown("hand", *TAPS, fade=0.1),
    op("arrow", [(0, 1)] + [s for a, b in TAPS for s in ((a, 1), (a + 0.1, 0), (b, 0), (b + 0.1, 1))]),
    # focus: the blue icon's active state, the red icon's, and the window that has focus at the start
    op("sel", [(0, 0), (CLICK1, 0), (CLICK1 + 0.2, 1), (CLICK2, 1), (CLICK2 + 0.3, 0)]),
    op("selr", [(0, 0), (CLICK2, 0), (CLICK2 + 0.2, 1)]),
    op("xact", [(0, 1), (CLICK1, 1), (CLICK1 + 0.3, 0)]),
    # the blue window: unfocused, then focused at the first click, unfocused again at the second
    op("windim", [(0, 1), (CLICK1, 1), (CLICK1 + 0.3, 0), (CLICK2, 0), (CLICK2 + 0.3, 1)]),
    op("winvis", [(0, 0), (CLICK1, 0), (CLICK1 + 0.3, 1), (CLICK2, 1), (CLICK2 + 0.3, 0)]),
    tx("hop", [(0, XDX, EASE), (MOVE1[0], XDX, EASE), (MOVE1[1], X0)]),
    shown("landed", (MIDDLE1, E3A), fade=0.05),
    # the right monitor goes dark, pressed off by a finger
    op("off2", [(0, 0), (OFF[0], 0), (OFF[1], 1)]),
    op("led2on", [(0, 1), (OFF[0], 1), (OFF[1], 0)]),
    track("finger", [(0, FINGER_AWAY, EASE), (FINGER[0], FINGER_AWAY, EASE), (FINGER[1], "opacity: 1; transform: translate(0, 0)", EASE),
                     (FINGER[2], "opacity: 1; transform: translate(0, 0)", EASE), (FINGER[3], FINGER_AWAY)]),
    # the red window: a grey dotted outline on the dark monitor, focused at the click, the real window fading in as it slides
    op("ghost2", [(0, 0), (OFF[1], 0), (OFF[1] + 0.3, 1), (CLICK2, 1), (CLICK2 + 0.3, 0)]),
    op("ghostf", [(0, 0), (CLICK2, 0), (CLICK2 + 0.3, 1), (MOVE2[0], 1), (MOVE2[0] + 0.6, 0)]),
    op("redvis", [(0, 0), (MOVE2[0], 0), (MOVE2[0] + 0.5, 1)]),
    tx("hop2", [(0, XDX, EASE), (MOVE2[0], XDX, EASE), (MOVE2[1], X0)]),
    shown("landed2", (MIDDLE2, E3B), fade=0.05),
    # the progress bar: a segment is bright while its step is on, half lit once it is done
    *[op(f"pg{i}", [(0, 0), (a, 0), (a + 0.2, 1), (b, 1), (b + 0.2, 0.4)]) for i, (a, b) in enumerate(SEGMENTS)],
    # fades the whole picture in at the start and out at the end, so the loop restarts without a jump
    op("loop", [(0, 0), (0.5, 1), (T - 0.8, 1), (T, 0)]),
]
KF = "".join(TRACKS)


# --- The desktops are light, so a monitor that is off reads as off at a glance. ------------
# Only the two targets (blue, then red) use saturated colour; everything else stays pale.
DESK, TASKBAR, TASK_SEL, OFF_SCREEN = "#dde4ec", "#f3f5f7", "#d3dae2", "#05070a"
TEAL, MAUVE, OLIVE, SLATE, DUSTY_RED = "#6d8794", "#8c7d99", "#95916c", "#7b8794", "#a8686c"
INK = "#1c2128"
FOCUS = "#1f2328"            # a focused window's outline, and a focused icon's marks


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
            out.append(f'<rect x="{x+4}" y="177.2" width="8" height="1.2" rx="0.6" fill="#8b949e"/>')
    return "\n      ".join(out)


def win(x, y, w, h, fill, bar):
    """A background window: pale fill, a darker title bar, faint content bars, a faint edge."""
    return (f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="3" fill="{fill}"/>'
            f'<rect x="{x}" y="{y}" width="{w}" height="10" rx="3" fill="{bar}"/><rect x="{x}" y="{y+6}" width="{w}" height="4" fill="{bar}"/>'
            f'<rect x="{x+8}" y="{y+17}" width="{w*6//10}" height="4" rx="2" fill="{bar}" opacity="0.7"/>'
            f'<rect x="{x+8}" y="{y+26}" width="{w*8//10-8}" height="4" rx="2" fill="{bar}" opacity="0.5"/>'
            f'<rect x="{x+8}" y="{y+35}" width="{w*4//10}" height="4" rx="2" fill="{bar}" opacity="0.5"/>'
            f'<rect x="{x+0.5}" y="{y+0.5}" width="{w-1}" height="{h-1}" rx="2.5" fill="none" stroke="#8c959f" stroke-opacity="0.6"/>')


def focused(x, y, w, h):
    """Drawn over a background window while it has focus: a stronger title bar and a dark outline."""
    return (f'<rect x="{x}" y="{y}" width="{w}" height="10" rx="3" fill="#7fa7c9"/><rect x="{x}" y="{y+6}" width="{w}" height="4" fill="#7fa7c9"/>'
            f'<rect x="{x+0.75}" y="{y+0.75}" width="{w-1.5}" height="{h-1.5}" rx="2.5" fill="none" stroke="{FOCUS}" stroke-width="1.5"/>')


# "Show taskbar buttons on all taskbars": both taskbars carry the same buttons in the same order and
# light up together. The blue window's icon is first, the red window's second.
TASKBAR_SPEC = ((DUSTY_RED, "bars"), (TEAL, "grid"), (MAUVE, "dot"), (SLATE, "tri"))
ICONS_M1 = icons((78, 100, 122, 144), TASKBAR_SPEC, {1, 3})
ICONS_M2 = icons((78 + DX, 100 + DX, 122 + DX, 144 + DX), TASKBAR_SPEC, {1, 3})

M1_FOCUSED = win(48, 100, 80, 56, "#f6f8fa", "#c3d2dc")      # monitor 1: has focus at the start (the teal icon)
M1_FOCUSED_ON = focused(48, 100, 80, 56)
M1_OTHER = win(142, 38, 100, 50, "#f6f8fa", "#d5cce0")
M2_BEHIND = win(52 + DX, 42, 96, 62, "#f6f8fa", "#c3d2dc")   # monitor 2: behind the blue window
M2_RED = win(176 + DX, 98, 92, 58, "#f6efef", "#ddb7b9")     # monitor 2: the red window, in front of the blue one

# The blue and red windows are drawn where they land on monitor 1, and start shifted over by DX.
BLUE_DIM = f'''<rect x="100" y="70" width="120" height="80" rx="5" fill="#b9c9de"/>
        <rect x="100" y="70" width="120" height="18" rx="5" fill="#9fb3cf"/>
        <rect x="100" y="80" width="120" height="8" fill="#9fb3cf"/>
        <g fill="#ffffff" opacity="0.8"><circle cx="112" cy="79" r="3"/><circle cx="123" cy="79" r="3"/><circle cx="134" cy="79" r="3"/></g>
        <rect x="110" y="100" width="70" height="6" rx="3" fill="#ffffff" opacity="0.7"/>
        <rect x="110" y="114" width="90" height="6" rx="3" fill="#ffffff" opacity="0.55"/>
        <rect x="110" y="128" width="55" height="6" rx="3" fill="#ffffff" opacity="0.55"/>
        <rect x="100.5" y="70.5" width="119" height="79" rx="4.5" fill="none" stroke="#8c959f" stroke-opacity="0.6"/>'''
BLUE_LIT = f'''<rect x="100" y="70" width="120" height="80" rx="5" fill="#2f81f7"/>
        <rect x="100" y="70" width="120" height="18" rx="5" fill="#1158c7"/>
        <rect x="100" y="80" width="120" height="8" fill="#1158c7"/>
        <rect x="100" y="88" width="120" height="1" fill="#a5d6ff" opacity="0.7"/>
        <g fill="#ffffff"><circle cx="112" cy="79" r="3"/><circle cx="123" cy="79" r="3"/><circle cx="134" cy="79" r="3"/></g>
        <rect x="110" y="100" width="70" height="6" rx="3" fill="#ffffff" opacity="0.85"/>
        <rect x="110" y="114" width="90" height="6" rx="3" fill="#ffffff" opacity="0.6"/>
        <rect x="110" y="128" width="55" height="6" rx="3" fill="#ffffff" opacity="0.6"/>
        <rect x="100.75" y="70.75" width="118.5" height="78.5" rx="4.5" fill="none" stroke="{FOCUS}" stroke-width="1.5"/>'''
RED_LIT = f'''<rect x="176" y="98" width="92" height="58" rx="5" fill="#d6454b"/>
        <rect x="176" y="98" width="92" height="14" rx="5" fill="#a8282f"/>
        <rect x="176" y="106" width="92" height="6" fill="#a8282f"/>
        <rect x="176" y="112" width="92" height="1" fill="#ffd0d2" opacity="0.7"/>
        <g fill="#ffffff"><circle cx="186" cy="105" r="2.5"/><circle cx="195" cy="105" r="2.5"/><circle cx="204" cy="105" r="2.5"/></g>
        <rect x="184" y="122" width="52" height="5" rx="2.5" fill="#ffffff" opacity="0.85"/>
        <rect x="184" y="132" width="68" height="5" rx="2.5" fill="#ffffff" opacity="0.6"/>
        <rect x="184" y="142" width="40" height="5" rx="2.5" fill="#ffffff" opacity="0.6"/>
        <rect x="176.75" y="98.75" width="90.5" height="56.5" rx="4.5" fill="none" stroke="{FOCUS}" stroke-width="1.5"/>'''

BEZEL = "M40 27 H280 A3 3 0 0 1 283 30 V197 A3 3 0 0 1 280 200 H40 A3 3 0 0 1 37 197 V30 A3 3 0 0 1 40 27 Z"
BAND = "M37 180 H283 V197 A3 3 0 0 1 280 200 H40 A3 3 0 0 1 37 197 Z"
POWER = '<path d="M-3.6 -3.6 A5.2 5.2 0 1 0 3.6 -3.6"/><line x1="0" y1="-6.2" x2="0" y2="-1"/>'
POWER_LIT = f'''<circle r="7.6" fill="none" stroke="#58a6ff" stroke-opacity="0.4" stroke-width="1.8"/>
      <circle r="5.2" fill="#12304f"/>
      <g transform="scale(0.66)" fill="none" stroke="#8fd0ff" stroke-width="2.6" stroke-linecap="round">{POWER}</g>'''

# --- The mouse and the steps, left of the monitors ---------------------------------------
MOX, MOY, MS = 36, 104, 1.15  # the mouse's top-left corner and scale; its drawing is 70 x 96


def at(x, y):
    """A point on the mouse drawing, in picture coordinates."""
    return f"{MOX + MS * x:g},{MOY + MS * y:g}"


BULLET_X, TEXT_X = 160, 170
ROWS = (82, 117, 152, 187)   # baselines of the four steps
STEPS = ("Click its taskbar icon", "Hold Mouse4", "Middle-click", "Release Mouse4")
STEP1_END = TEXT_X + 134     # step 1's width in Segoe UI 15px, measured; where its leader to the taskbar starts


def row(i):
    return ROWS[i] - 5       # the middle of a step's line, where its bullet and leader sit


# Orthogonal leaders from each step's bullet to its button. Mouse4 is on the mouse's left side, so its
# leaders go up and over the mouse; they never cross the wheel's, which is up at the same time.
LEAD1 = f"{BULLET_X - 6},{row(0)} {at(17, 0)[:at(17, 0).index(',')]},{row(0)} {at(17, 4)}"
LEAD3 = f"{BULLET_X - 6},{row(2)} 124,{row(2)} 124,92 {at(35, 0)[:at(35, 0).index(',')]},92 {at(35, 12)}"
MOUSE4 = at(-8, 67)
LEAD2 = f"{BULLET_X - 6},{row(1)} 132,{row(1)} 132,62 12,62 12,{MOUSE4.split(',')[1]} {MOUSE4}"
LEAD4 = f"{BULLET_X - 6},{row(3)} 132,{row(3)} 132,62 12,62 12,{MOUSE4.split(',')[1]} {MOUSE4}"


def to_icon(icon_x):
    """Step 1's leader to an icon on monitor 1's taskbar: down the gap left of the monitors, then along the taskbar's bottom edge."""
    x = MX + icon_x + 8
    return f"{STEP1_END + 6},{row(0)} {MX + 29},{row(0)} {MX + 29},179.3 {x},179.3 {x},176"


def rays(cls, cx, cy, angles, r0, r1):
    """A click decal: short orange rays out from the point clicked or pressed, at the given angles (degrees, 270 is up)."""
    d = " ".join(f"M{cx + r0 * cos(radians(a)):.2f} {cy + r0 * sin(radians(a)):.2f} L{cx + r1 * cos(radians(a)):.2f} {cy + r1 * sin(radians(a)):.2f}"
                 for a in angles)
    return f'<path class="{cls}" d="{d}" fill="none" stroke="{ORANGE}" stroke-width="2" stroke-linecap="round"/>'


CLICK_FAN = (195, 233, 270, 307, 345)   # the rays fanned over a pointing hand's fingertip
# A pointing hand, the cursor while it clicks: index finger up, its tip at (0, 0)
HAND = ('<path transform="translate(-4.9 0) scale(0.95)" d="M2.6 13 V2.6 A2.6 2.6 0 0 1 7.8 2.6 V9 A2.1 2.1 0 0 1 12 9.6 '
        'A2.1 2.1 0 0 1 16.2 10.4 A2 2 0 0 1 20.2 11.4 V17 Q20.2 24 13.6 24 H10.2 Q5.6 24 3.4 19.6 L0.4 14.6 '
        'A1.9 1.9 0 0 1 3.4 12.4 Z M7.8 9 V12.6 M12 9.6 V13 M16.2 10.4 V13.4" '
        'fill="#ffffff" stroke="#0d1117" stroke-width="1" stroke-linejoin="round"/>')


# The progress bar, under the steps: a segment per step, the power button's on its own between the passes
PG_X, PG_Y, PG_W, PG_GAP, PG_SPLIT = BULLET_X - 4, 226, 22, 4, 14


def progress(FAINT, n, splits=()):
    """n segments, classes pg0 to pg{n-1}, with a wider gap after each segment in splits."""
    xs, x = [], PG_X
    for i in range(n):
        xs.append(x)
        x += PG_W + (PG_SPLIT if i in splits else PG_GAP)
    base = "".join(f'<rect x="{x}" y="{PG_Y}" width="{PG_W}" height="5" rx="2.5" fill="{FAINT}" opacity="0.45"/>' for x in xs)
    lit = "".join(f'<rect class="pg{i}" x="{x}" y="{PG_Y}" width="{PG_W}" height="5" rx="2.5" fill="{ORANGE}"/>' for i, x in enumerate(xs))
    return f'<g class="pgbar">{base}{lit}</g>'


def build(PAGE, LABEL, BRIGHT, FAINT, STAND, OUTLINE, OUTLINE_W, MOUSE, MOUSE_LINE, MOUSE_BTN):
    lead = f'fill="none" stroke="{ORANGE}" stroke-width="1.6" stroke-linejoin="round"'
    pg_css = "\n".join(f"      .pg{i} {{ animation: pg{i} {TS}; }}" for i in range(len(SEGMENTS)))
    steps = "\n    ".join(
        (f'<g class="past{i + 1}"><circle cx="{BULLET_X}" cy="{row(i)}" r="3" fill="{FAINT}"/><text x="{TEXT_X}" y="{ROWS[i]}" fill="{FAINT}">{s}</text></g>' if i < 3 else '') +
        f'<g class="st{i + 1}"><circle cx="{BULLET_X}" cy="{row(i)}" r="3.5" fill="{ORANGE}"/><text x="{TEXT_X}" y="{ROWS[i]}" fill="{BRIGHT}">{s}</text></g>'
        for i, s in enumerate(STEPS))
    return f"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {W} {H}" width="{W}" height="{H}" role="img" aria-labelledby="t d">
  <title id="t">Pulling a window to the monitor you are on</title>
  <desc id="d">A mouse, four steps, and two monitors. The cursor is on the left monitor. With both monitors on: the blue window's icon is clicked on the left monitor's taskbar, Mouse4 is held, the middle button is clicked, and the blue window comes from the right monitor to the left one; then Mouse4 is released. Then a finger presses the right monitor's power button and its screen goes black, leaving a grey dotted outline where the red window is. The same steps bring the red window out of the dark monitor to the left one, and the cursor leaves the screen.</desc>
  <style>
    text {{ font-family: "Segoe UI", Helvetica, Arial, sans-serif; }}
    .win, .win2 {{ transform: translateX(0); }}
    .cur {{ transform: translate(22px, 0); }}
    .selr, .off2, .windim, .redvis, .head2 {{ opacity: 1; }}
    .hit, .hit2, .ring1, .ring1b, .left, .wheel, .side, .xact, .sel, .winvis, .ghost2, .ghostf, .led2on, .head1, .finger,
    .st1, .st2, .st3, .st4, .lead1, .lead2, .lead3, .lead4, .icon1, .icon2, .path1, .path2, .tap1, .tap2, .tapp, .hand, .pgbar {{ opacity: 0; }}
    .ring1, .ring1b {{ transform-box: fill-box; transform-origin: center; }}
    @media (prefers-reduced-motion: no-preference) {{
      .pgbar {{ opacity: 1; }}
{pg_css}
      .tap1  {{ animation: tap1 {TS}; }}
      .tap2  {{ animation: tap2 {TS}; }}
      .tapp  {{ animation: tapp {TS}; }}
      .hand  {{ animation: hand {TS}; }}
      .arrow {{ animation: arrow {TS}; }}
      .cur    {{ animation: cur {TS}; }}
      .head1  {{ animation: head1 {TS}; }}
      .head2  {{ animation: head2 {TS}; }}
      .past1 {{ animation: done1 {TS}; }}
      .past2 {{ animation: done2 {TS}; }}
      .past3 {{ animation: done3 {TS}; }}
      .st1 {{ animation: step1 {TS}; }}
      .st2 {{ animation: step2 {TS}; }}
      .st3 {{ animation: step3 {TS}; }}
      .st4 {{ animation: step4 {TS}; }}
      .lead1 {{ animation: lead1 {TS}; }}
      .lead2 {{ animation: lead2 {TS}; }}
      .lead3 {{ animation: lead3 {TS}; }}
      .lead4 {{ animation: lead4 {TS}; }}
      .icon1 {{ animation: icon1 {TS}; }}
      .icon2 {{ animation: icon2 {TS}; }}
      .path1 {{ animation: path1 {TS}; }}
      .path2 {{ animation: path2 {TS}; }}
      .side  {{ animation: side {TS}; }}
      .left  {{ animation: left {TS}; }}
      .wheel {{ animation: wheel {TS}; }}
      .ring1 {{ animation: ring1 {TS}; }}
      .ring1b {{ animation: ring1b {TS}; }}
      .sel    {{ animation: sel {TS}; }}
      .selr   {{ animation: selr {TS}; }}
      .xact   {{ animation: xact {TS}; }}
      .windim {{ animation: windim {TS}; }}
      .winvis {{ animation: winvis {TS}; }}
      .win    {{ animation: hop {TS}; }}
      .hit    {{ animation: landed {TS}; }}
      .off2   {{ animation: off2 {TS}; }}
      .led2on {{ animation: led2on {TS}; }}
      .finger {{ animation: finger {TS}; }}
      .ghost2 {{ animation: ghost2 {TS}; }}
      .ghostf {{ animation: ghostf {TS}; }}
      .redvis {{ animation: redvis {TS}; }}
      .win2   {{ animation: hop2 {TS}; }}
      .hit2   {{ animation: landed2 {TS}; }}
      .loop   {{ animation: loop {TS}; }}
    }}
{KF}  </style>

  <defs>
    <marker id="head" viewBox="0 0 10 10" refX="7" refY="5" markerWidth="5" markerHeight="5" orient="auto-start-reverse">
      <path d="M0 0 L10 5 L0 10 Z" fill="{ORANGE}"/>
    </marker>
  </defs>
  <rect width="{W}" height="{H}" rx="12" fill="{PAGE}"/>
  <g class="loop">

  <!-- the mouse, seen from above: Mouse5 is the upper side button, Mouse4 the lower one -->
  <g transform="translate({MOX} {MOY}) scale({MS})">
    <rect x="0" y="0" width="70" height="96" rx="32" fill="{MOUSE}" stroke="{MOUSE_LINE}" stroke-width="2.4"/>
    <line x1="0" y1="42" x2="70" y2="42" stroke="{MOUSE_LINE}" stroke-width="2.4"/>
    <line x1="35" y1="0" x2="35" y2="42" stroke="{MOUSE_LINE}" stroke-width="2.4"/>
    <path class="left" d="M35 0 H32 A32 32 0 0 0 0 32 V42 H35 Z" fill="{ORANGE}"/>
    <rect x="29" y="12" width="12" height="22" rx="6" fill="{MOUSE_BTN}" stroke="{MOUSE_LINE}" stroke-width="1.8"/>
    <rect class="wheel" x="27" y="10" width="16" height="26" rx="8" fill="{ORANGE}"/>
    <rect x="-8" y="40" width="10" height="14" rx="3" fill="{MOUSE_BTN}" stroke="{MOUSE_LINE}" stroke-width="1.8"/>
    <rect x="-8" y="60" width="10" height="14" rx="3" fill="{MOUSE_BTN}" stroke="{MOUSE_LINE}" stroke-width="1.8"/>
    <rect class="side" x="-10" y="58" width="14" height="18" rx="4" fill="{ORANGE}"/>
  </g>

  <!-- the steps, a heading per pass, and the leaders from each step to its button and to the screen -->
  <g font-size="15">
    <text class="head1" x="{BULLET_X - 4}" y="48" font-size="16" font-weight="600" fill="{BRIGHT}">Both monitors on</text>
    <text class="head2" x="{BULLET_X - 4}" y="48" font-size="16" font-weight="600" fill="{BRIGHT}">Right monitor off</text>
    {steps}
  </g>
  {progress(FAINT, len(SEGMENTS), (3, 4))}
  <g {lead}>
    <polyline class="lead1" points="{LEAD1}"/>
    <polyline class="lead2" points="{LEAD2}"/>
    <polyline class="lead3" points="{LEAD3}"/>
    <polyline class="lead4" points="{LEAD4}"/>
    <polyline class="icon1" points="{to_icon(56)}"/>
    <polyline class="icon2" points="{to_icon(78)}"/>
  </g>

  <g transform="translate({MX} 0)">
    <!-- monitors: stand, a thin black bezel with a taller bottom band that holds the power button, then the screen -->
    <g fill="{STAND}">
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
    <g fill="{DESK}">
      <rect x="40"  y="30" width="240" height="150"/>
      <rect x="{40 + DX}" y="30" width="240" height="150"/>
    </g>
    <g font-size="15" text-anchor="middle" fill="{LABEL}">
      <text x="160" y="244">Monitor 1</text>
      <text x="{160 + DX}" y="244">Monitor 2</text>
    </g>

    <!-- power buttons at each monitor's bottom-right corner. Monitor 2's goes dark when it is turned off. -->
    <g transform="translate(262 190)">
      <circle r="6" fill="#21262d" stroke="#c9d1d9" stroke-width="1.5"/>
      {POWER_LIT}
    </g>
    <g transform="translate({262 + DX} 190)">
      <circle r="6" fill="#21262d" stroke="#c9d1d9" stroke-width="1.5"/>
      <g transform="scale(0.66)" fill="none" stroke="#6e7681" stroke-width="2.2" stroke-linecap="round">{POWER}</g>
      <g class="led2on">
        {POWER_LIT}
      </g>
    </g>

    <!-- windows, bottom to top: monitor 1's, the one behind the blue window, the blue window unfocused, the red one in front -->
    {M1_FOCUSED}
    <g class="xact">{M1_FOCUSED_ON}</g>
    {M1_OTHER}
    {M2_BEHIND}
    <g class="win">
      <g class="windim">
        {BLUE_DIM}
      </g>
    </g>
    {M2_RED}

    <!-- taskbars, the same buttons on both (see TASKBAR_SPEC) -->
    <!-- half a unit wider than the screen on each side, out to the outer edge of the screen's 1-unit edge line -->
    <rect x="39.5" y="160" width="241" height="20" fill="{TASKBAR}"/>
    <rect x="{39.5 + DX}" y="160" width="241" height="20" fill="{TASKBAR}"/>
    <rect x="40" y="160" width="240" height="0.8" fill="#c9d1d9"/>
    <rect x="{40 + DX}" y="160" width="240" height="0.8" fill="#c9d1d9"/>
    <rect class="sel" x="52" y="161" width="24" height="15" rx="2" fill="{TASK_SEL}"/>
    <rect class="selr" x="74" y="161" width="24" height="15" rx="2" fill="{TASK_SEL}"/>
    <rect class="xact" x="96" y="161" width="24" height="15" rx="2" fill="{TASK_SEL}"/>
    <rect class="sel" x="{52 + DX}" y="161" width="24" height="15" rx="2" fill="{TASK_SEL}"/>
    <rect class="selr" x="{74 + DX}" y="161" width="24" height="15" rx="2" fill="{TASK_SEL}"/>
    <rect class="xact" x="{96 + DX}" y="161" width="24" height="15" rx="2" fill="{TASK_SEL}"/>
    <g>
      {ICONS_M1}
      {ICONS_M2}
    </g>
    <rect x="56" y="163" width="16" height="12" rx="2" fill="#2f81f7"/>
    <rect x="{56 + DX}" y="163" width="16" height="12" rx="2" fill="#2f81f7"/>
    <g fill="{FOCUS}">
      <rect class="sel" x="59" y="177.2" width="10" height="1.6" rx="0.8"/>
      <rect class="sel" x="{59 + DX}" y="177.2" width="10" height="1.6" rx="0.8"/>
      <rect class="selr" x="81" y="177.2" width="10" height="1.6" rx="0.8"/>
      <rect class="selr" x="{81 + DX}" y="177.2" width="10" height="1.6" rx="0.8"/>
      <rect class="xact" x="103" y="177.2" width="10" height="1.6" rx="0.8"/>
      <rect class="xact" x="{103 + DX}" y="177.2" width="10" height="1.6" rx="0.8"/>
    </g>
    <rect class="selr" x="78" y="163" width="16" height="12" rx="2" fill="#e5484d"/>
    <rect class="selr" x="{78 + DX}" y="163" width="16" height="12" rx="2" fill="#e5484d"/>

    <!-- monitor 2 turned off: a black screen over everything on it -->
    <rect class="off2" x="{40 + DX}" y="30" width="240" height="150" fill="{OFF_SCREEN}"/>

    <!-- thin grey edge line around each screen above the taskbar, so the taskbar runs straight into the bezel -->
    <g fill="none" stroke="#4b535d" stroke-width="1">
      <path d="M40 160 V30 H280 V160"/>
      <path d="M{40 + DX} 160 V30 H{280 + DX} V160"/>
      <path class="off2" d="M{40 + DX} 160 V180 H{280 + DX} V160"/>
    </g>

    <!-- the blue window, focused: it comes in front of the others when its icon is clicked, then carries the orange highlight and slides over -->
    <g class="win">
      <g class="winvis">
        {BLUE_LIT}
      </g>
      <rect class="hit" x="96" y="66" width="128" height="88" rx="8" fill="none" stroke="{ORANGE}" stroke-width="4"/>
    </g>

    <!-- the red window on the dark monitor: a grey dotted outline, focused at the click, the real window fading in as it slides -->
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
      <rect class="hit2" x="172" y="94" width="100" height="66" rx="8" fill="none" stroke="{ORANGE}" stroke-width="4"/>
    </g>

    <!-- step 3's leader on the screen: the path the window is about to take, from where it is to where it lands -->
    <g fill="none" stroke="{ORANGE}" stroke-width="2" stroke-dasharray="5 4" stroke-linecap="round" marker-end="url(#head)">
      <path class="path1" d="M{160 + DX} 64 Q{160 + DX / 2:g} -26 166 62"/>
      <path class="path2" d="M{222 + DX} 92 Q{222 + DX / 2:g} -44 228 90"/>
    </g>

    <!-- click rings (blue icon, then red icon) and the cursor that travels between them -->
    <circle class="ring1" cx="64" cy="169" r="3" fill="none" stroke="{ORANGE}" stroke-width="1"/>
    <circle class="ring1b" cx="86" cy="169" r="3" fill="none" stroke="{ORANGE}" stroke-width="1"/>
    {rays("tap1", 64, 168, CLICK_FAN, 4.5, 10)}
    {rays("tap2", 86, 168, CLICK_FAN, 4.5, 10)}
    {rays("tapp", 262 + DX, 190, range(0, 360, 45), 9, 13)}
    <g class="cur"><g transform="translate(64 169)">
      <polygon class="arrow" points="0,0 0,12 3,9 5.5,14 7.5,13 5,8 9,8" fill="#ffffff" stroke="#0d1117" stroke-width="0.6"/>
      <g class="hand">{HAND}</g>
    </g></g>

    <!-- the finger that turns monitor 2 off, its tip on the power button -->
    <g transform="translate({262 + DX} 190)">
      <g class="finger">
        <g transform="rotate(-28)">
          <rect x="-7.5" y="-3" width="15" height="90" rx="7.5" fill="#e8b896" stroke="#9a6b4f" stroke-width="1"/>
          <rect x="-4.6" y="-1" width="9.2" height="11" rx="4.2" fill="#f6dccb" stroke="#c99a7c" stroke-width="0.6"/>
          <path d="M-5 22 Q0 24.5 5 22" fill="none" stroke="#b7826a" stroke-width="0.8"/>
        </g>
      </g>
    </g>
  </g>
  </g>
</svg>
"""


THEMES = {
    # name: (page, labels, lit step, faint step, stand, bezel outline, outline width, mouse, mouse lines, mouse buttons)
    "light": ("#ffffff", "#1f2328", "#1f2328", "#8c959f", "#57606a", "#000000", 1, "#f6f8fa", "#57606a", "#eaeef2"),
    "dark":  ("#0d1117", "#c9d1d9", "#f0f6fc", "#6e7681", "#8b949e", "#c9d1d9", 1.5, "#161b22", "#c9d1d9", "#30363d"),
}

if __name__ == "__main__":
    here = Path(__file__).resolve().parent
    for name, args in THEMES.items():
        out = here / f"pull-window-{name}.svg"
        out.write_text(build(*args), encoding="utf-8", newline="")
        m.parse(str(out))
        print(out.name, "ok")
