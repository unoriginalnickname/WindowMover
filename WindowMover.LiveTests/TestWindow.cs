using System.Runtime.InteropServices;

namespace WindowMover.LiveTests;

// An ordinary top-level window the tests own, on its own STA thread with its own message
// loop - a stand-in for "some app's window", and the one kind of window these tests can
// create and destroy without touching anything the user is working in.
internal sealed class TestWindow : IDisposable
{
    private const int SW_MAXIMIZE = 3;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hWnd);

    private readonly Thread thread;
    private readonly Form form;

    public IntPtr Handle { get; }

    private TestWindow(string title, Rectangle bounds)
    {
        using var ready = new ManualResetEventSlim();
        Form? created = null;

        thread = new Thread(() =>
        {
            created = new Form
            {
                Text = title,
                StartPosition = FormStartPosition.Manual,
                Bounds = bounds,
                // Must stay a window WindowMoveFilter agrees to move: visible, not a tool
                // window, comfortably larger than its minimum size.
                MinimumSize = new Size(400, 300)
            };
            created.Shown += (_, _) => ready.Set();
            Application.Run(created);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        if (!ready.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Test window never appeared");
        form = created!;
        Handle = form.Invoke(() => form.Handle);
    }

    // A restored window filling a reasonable part of the given monitor.
    public static TestWindow Restored(Screen monitor)
    {
        Rectangle work = monitor.WorkingArea;
        var bounds = new Rectangle(work.X + work.Width / 8, work.Y + work.Height / 8, work.Width / 2, work.Height / 2);
        return new TestWindow($"WindowMover live test {Guid.NewGuid():N}", bounds);
    }

    // A window maximized on the given monitor - maximizing is done through Win32 rather than
    // WindowState so it goes through the same path a real app's window would.
    public static TestWindow Maximized(Screen monitor)
    {
        var window = Restored(monitor);
        Rectangle work = monitor.WorkingArea;
        SetWindowPos(window.Handle, IntPtr.Zero, work.X, work.Y, work.Width, work.Height, SWP_NOZORDER | SWP_NOACTIVATE);
        ShowWindow(window.Handle, SW_MAXIMIZE);
        Thread.Sleep(300);

        // A window that quietly failed to maximize would send every test using it down the
        // restored path instead, passing while proving nothing about the case they name.
        if (!IsZoomed(window.Handle))
        {
            window.Dispose();
            throw new InvalidOperationException("Test window did not maximize");
        }

        return window;
    }

    public void Dispose()
    {
        try
        {
            form.Invoke(() => form.Close());
            thread.Join(TimeSpan.FromSeconds(5));
            form.Dispose();
        }
        catch (InvalidOperationException) { /* already closed */ }
    }
}
