namespace WindowMover.LiveTests;

// Runs an STA thread with a real Windows message loop, and marshals calls onto it.
//
// The app's move code always runs on a thread that has one: the mouse hook callback is
// delivered on the thread that installed the hook, which is the app's own message loop.
// Parts of the move rely on that - MoveSettleWatcher debounces on a WinForms timer and the
// indicator is a Form of this app's own, and neither ticks nor paints unless something is
// pumping messages. Calling the move straight from an xUnit thread would silently skip those
// parts and the tests would be asserting against a shape of the code that never runs.
internal sealed class MessageLoopThread : IDisposable
{
    private readonly Thread thread;
    private readonly Form pump;

    public MessageLoopThread()
    {
        using var ready = new ManualResetEventSlim();
        Form? created = null;

        thread = new Thread(() =>
        {
            // An invisible zero-size form purely as something to Invoke onto and to give
            // Application.Run a lifetime to manage.
            created = new Form { ShowInTaskbar = false, WindowState = FormWindowState.Minimized };
            _ = created.Handle; // force handle creation before anything tries to Invoke
            ready.Set();
            Application.Run(created);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        ready.Wait(TimeSpan.FromSeconds(10));
        pump = created!;
    }

    // Runs the action on the message loop thread and waits for it to finish - the call
    // itself, not whatever it scheduled to happen afterwards.
    public void Invoke(Action action) => pump.Invoke(action);

    public void Dispose()
    {
        try
        {
            pump.Invoke(() => Application.ExitThread());
            thread.Join(TimeSpan.FromSeconds(5));
            pump.Dispose();
        }
        catch (InvalidOperationException) { /* already gone */ }
    }
}
