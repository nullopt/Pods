using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppLifecycle;

namespace Pods.App;

public static partial class Program
{
    const string InstanceKey = "main";

    [STAThread]
    static int Main()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        var instance = AppInstance.FindOrRegisterForKey(InstanceKey);
        if (!instance.IsCurrent)
        {
            // Hand this launch to the running copy and let it take the foreground.
            AllowSetForegroundWindow(instance.ProcessId);
            var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            Task.Run(() => instance.RedirectActivationToAsync(activation).AsTask()).Wait();
            return 0;
        }

        instance.Activated += (_, _) => App.Instance?.OnActivatedAgain();
        Microsoft.UI.Xaml.Application.Start(p =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            new App();
        });
        return 0;
    }

    /// <summary>Lets a restarted copy register the key before this process has exited.</summary>
    internal static void ReleaseInstanceKey() => AppInstance.GetCurrent().UnregisterKey();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(uint processId);
}
