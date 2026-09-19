using System.Drawing.Drawing2D;

// A brief outline drawn where a window just landed, with the monitor's number in it, fading
// out over a few hundred milliseconds.
//
// This exists because the obvious signal - putting the moved window in front - turned out to
// be something Windows will not always allow. A process that does not hold the foreground
// cannot raise a window above the foreground window, and cannot take the foreground either
// while the user is interacting with another app: measured live, every activation attempt was
// refused (ISSUES.md #6). So a move onto a monitor whose active window is already in the way
// could look exactly like a move that never happened.
//
// An overlay this app owns has none of those problems. It is always-on-top, which IS allowed
// for one's own window, so it shows above whatever is in the way; it never activates, never
// takes focus, and never touches the Z-order of anything the user owns. It is click-through,
// so it cannot swallow a click in the few hundred milliseconds it is up, and it is a tool
// window, so it never appears in the taskbar or in Alt-Tab - and WindowMoveFilter already
// refuses to move tool windows, so this app can never be asked to move its own indicator.
internal static class MoveIndicator
{
    // Tray-menu toggle. On by default: a move you cannot see is the complaint this answers.
    public static bool Enabled { get; set; } = true;

    private const int FadeDurationMs = 550;
    private const int FrameIntervalMs = 16;   // ~60fps; the whole thing is over in half a second

    private static IndicatorForm? current;

    // Shows the indicator over the given bounds. Called on the app's message-loop thread, the
    // same one the mouse hook runs on, so there is no cross-thread work to do here.
    public static void Show(Screen monitor, Rectangle landedBounds)
    {
        if (!Enabled) return;

        // Only ever one at a time: moving a window twice in quick succession should replace
        // the indicator, not stack a second one on top of the first.
        Hide();

        try
        {
            current = new IndicatorForm(MonitorNumber(monitor), landedBounds);
            current.FormClosed += (_, _) => { if (ReferenceEquals(current, null)) return; current = null; };
            current.ShowFade(FadeDurationMs, FrameIntervalMs);
        }
        catch (Exception ex)
        {
            // An indicator is a nicety; a move is not. Nothing here may take a move down with it.
            DebugLog.Write($"Move indicator: failed to show - {ex.GetType().Name}: {ex.Message}");
            current = null;
        }
    }

    // Called on shutdown so no overlay can outlive the message loop that animates it.
    public static void Hide()
    {
        var form = current;
        current = null;
        if (form is null) return;

        try { form.Close(); form.Dispose(); }
        catch (ObjectDisposedException) { /* already gone */ }
    }

    // What to actually print. Windows names monitors \\.\DISPLAY1, \\.\DISPLAY2 and so on, and
    // that trailing number is the one the user sees in Display Settings - worth more than this
    // app's own array index, which is arbitrary. Falls back to the index when a device name
    // does not follow that shape.
    private static string MonitorNumber(Screen monitor)
    {
        string name = monitor.DeviceName ?? string.Empty;
        string digits = new(name.Where(char.IsDigit).ToArray());
        if (digits.Length > 0) return digits;

        int index = Array.FindIndex(Screen.AllScreens, s => s.DeviceName == monitor.DeviceName);
        return (index < 0 ? 1 : index + 1).ToString();
    }

    private sealed class IndicatorForm : Form
    {
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_TRANSPARENT = 0x00000020;

        private readonly string number;
        private readonly System.Windows.Forms.Timer fadeTimer = new();
        private DateTime fadeStart;
        private int fadeDurationMs;

        public IndicatorForm(string number, Rectangle bounds)
        {
            this.number = number;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            // The form is positioned in real screen pixels; letting WinForms rescale it for the
            // monitor it opens on would move and resize it out from under those numbers.
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Black;
            // Black is the see-through colour, so only what is painted in other colours shows:
            // the outline and the number, with nothing but desktop in between.
            TransparencyKey = Color.Black;
            DoubleBuffered = true;
            Bounds = bounds;
        }

        // Keeps the overlay from stealing activation when it appears - the whole point is that
        // the user's focus is left exactly where it was.
        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams p = base.CreateParams;
                // NOACTIVATE: never becomes the active window. TOOLWINDOW: never in the taskbar
                // or Alt-Tab. TRANSPARENT: clicks land on whatever is underneath, so a click
                // aimed at the window that just arrived is not eaten by this.
                p.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT;
                return p;
            }
        }

        public void ShowFade(int durationMs, int frameIntervalMs)
        {
            fadeDurationMs = durationMs;
            Opacity = 1.0;
            Show();

            fadeStart = DateTime.UtcNow;
            fadeTimer.Interval = frameIntervalMs;
            fadeTimer.Tick += OnFadeTick;
            fadeTimer.Start();
        }

        private void OnFadeTick(object? sender, EventArgs e)
        {
            double elapsed = (DateTime.UtcNow - fadeStart).TotalMilliseconds;
            // Hold full opacity for the first third, then fade: a flash that starts fading
            // immediately reads as a flicker rather than as something showing you a place.
            double progress = Math.Clamp((elapsed - fadeDurationMs / 3.0) / (fadeDurationMs * 2.0 / 3.0), 0.0, 1.0);

            if (progress >= 1.0)
            {
                fadeTimer.Stop();
                Close();
                return;
            }

            Opacity = 1.0 - progress;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

            Rectangle outline = new(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            int thickness = Math.Clamp(Math.Min(Width, Height) / 90, 3, 10);

            // Drawn twice: a dark wide stroke under a bright narrow one, so the outline reads
            // against a pale wallpaper and a dark one alike without needing to know which.
            using (var shadow = new Pen(Color.FromArgb(120, 0, 0, 0), thickness + 3))
                g.DrawRectangle(shadow, Rectangle.Inflate(outline, -1, -1));
            using (var pen = new Pen(Color.FromArgb(255, 90, 190, 255), thickness))
                g.DrawRectangle(pen, Rectangle.Inflate(outline, -1, -1));

            // The number is sized from the window, not from a DPI, so it stays proportionate
            // on a small window and does not become a wall of pixels on a maximized one.
            float fontSize = Math.Clamp(Math.Min(Width, Height) / 4f, 40f, 180f);
            using var font = new Font(SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif, fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

            var textArea = new RectangleF(0, 0, Width, Height);
            using (var textShadow = new SolidBrush(Color.FromArgb(130, 0, 0, 0)))
                g.DrawString(number, font, textShadow, new RectangleF(3, 3, Width, Height), format);
            using (var textBrush = new SolidBrush(Color.FromArgb(255, 235, 245, 255)))
                g.DrawString(number, font, textBrush, textArea, format);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                fadeTimer.Stop();
                fadeTimer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
