using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Notka.Desktop
{
    public static class Program
    {
        internal static Action? ActivationHandler { get; set; }

        [STAThread]
        private static int Main(string[] args)
        {
            PerfLog.MarkMain();
            WinRT.ComWrappersSupport.InitializeComWrappers();
            if (!IsFirstInstance())
            {
                return 0;
            }

            Application.Start(callback =>
            {
                var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                _ = new App();
            });
            return 0;
        }

        private static bool IsFirstInstance()
        {
            var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            var instance = AppInstance.FindOrRegisterForKey("Notka");
            if (instance.IsCurrent)
            {
                instance.Activated += (_, _) => ActivationHandler?.Invoke();
                return true;
            }

            using var redirected = new ManualResetEvent(false);
            Task.Run(() =>
            {
                try
                {
                    instance.RedirectActivationToAsync(activation).AsTask().Wait();
                }
                finally
                {
                    redirected.Set();
                }
            });
            redirected.WaitOne(TimeSpan.FromSeconds(10));
            return false;
        }
    }
}
