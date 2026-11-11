using System.Windows.Interop;

namespace PopBar;

internal static class WindowPresentation
{
    // Only for an explicit user action, never for streaming updates or toolbar display.
    internal static void ShowForeground(Window window)
    {
        var previous=Native.GetForegroundWindow();
        var focusLeft=false;
        void Deactivated(object? sender,EventArgs args) => focusLeft=true;
        void Cleanup(object? sender,EventArgs args)
        {
            window.ContentRendered-=Rendered;
            window.Closed-=Cleanup;
            window.Deactivated-=Deactivated;
        }
        void Rendered(object? sender,EventArgs args)
        {
            Cleanup(sender,args);
            if(focusLeft || !window.IsVisible || window.WindowState==WindowState.Minimized) return;
            var handle=new WindowInteropHelper(window).Handle;
            var foreground=Native.GetForegroundWindow();
            // Do not take focus back if the user switched to another window while loading.
            if(foreground==previous || foreground==handle || foreground==0) BringForward(window);
        }
        window.ContentRendered+=Rendered;
        window.Closed+=Cleanup;
        window.Deactivated+=Deactivated;
        try {
            window.ShowActivated=true;
            if(window.WindowState==WindowState.Minimized) window.WindowState=WindowState.Normal;
            window.Show();
            BringForward(window);
        } catch { Cleanup(null,EventArgs.Empty); throw; }
    }

    static void BringForward(Window window)
    {
        var handle=new WindowInteropHelper(window).Handle;
        if(handle==0 || !window.IsVisible) return;
        Native.SetForegroundWindow(handle);
        window.Activate();
        if(Native.GetForegroundWindow()!=handle) {
            // A no-activate toolbar click can leave the originating app foreground.
            // Briefly raise the result, then restore its ordinary non-topmost behavior.
            var topmost=window.Topmost;
            try { window.Topmost=true; Native.SetForegroundWindow(handle); window.Activate(); }
            finally { window.Topmost=topmost; }
        }
    }
}
