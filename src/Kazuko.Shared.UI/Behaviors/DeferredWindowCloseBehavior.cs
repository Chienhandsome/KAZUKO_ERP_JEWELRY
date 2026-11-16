using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace Kazuko.Shared.UI.Behaviors;

/// <summary>Cancels the first close and prepares it on a new dispatcher turn, even when all awaits complete inline.</summary>
public static class DeferredWindowCloseBehavior
{
    public static void Attach(Window window, Func<Task> prepareClose, Action beforeFinalClose)
    {
        var preparing = false;
        var allowClose = false;
        var closed = false;
        window.Closed += (_, _) => closed = true;
        window.Closing += async (_, args) =>
        {
            if (allowClose) return;
            args.Cancel = true;
            if (preparing) return;
            preparing = true;
            window.IsEnabled = false;

            // A completed FlushAsync does not yield. Close must never run inside
            // the original Window.Closing stack (WPF rejects reentrant Close).
            await Dispatcher.Yield(DispatcherPriority.Background);
            try
            {
                if (closed || window.Dispatcher.HasShutdownStarted) return;
                await prepareClose();
                if (closed || window.Dispatcher.HasShutdownStarted) return;
                beforeFinalClose();
                allowClose = true;
                window.Close();
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Could not finish closing the window: {0}", ex.Message);
            }
            finally
            {
                if (!closed)
                {
                    preparing = false;
                    allowClose = false;
                    window.IsEnabled = true;
                }
            }
        };
    }
}
