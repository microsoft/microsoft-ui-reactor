using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace ChatSample.App;

static class Notifications
{
    static bool _registered;
    public static bool IsWindowFocused { get; set; }

    public static void Initialize()
    {
        if (_registered) return;
        try
        {
            var mgr = AppNotificationManager.Default;
            mgr.NotificationInvoked += OnNotificationInvoked;
            mgr.Register();
            _registered = true;
            System.Diagnostics.Trace.WriteLine("[notify] Registered");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[notify] Registration failed: {ex.Message}");
        }
    }

    public static void ShowPermissionRequest(string sessionId, string toolName, string detail)
    {
        if (!_registered || IsWindowFocused) return;
        try
        {
            var notification = new AppNotificationBuilder()
                .AddText($"🔒 Permission needed")
                .AddText($"{toolName}: {detail}")
                .AddArgument("action", "focus")
                .AddArgument("sessionId", sessionId)
                .BuildNotification();
            AppNotificationManager.Default.Show(notification);
            System.Diagnostics.Trace.WriteLine($"[notify] Permission toast shown for {sessionId}");
        }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"[notify] Permission toast error: {ex.Message}"); }
    }

    public static void ShowTurnComplete(string sessionId, string title)
    {
        if (!_registered || IsWindowFocused) return;
        try
        {
            var notification = new AppNotificationBuilder()
                .AddText($"✅ Assistant finished")
                .AddText(title)
                .AddArgument("action", "focus")
                .AddArgument("sessionId", sessionId)
                .BuildNotification();
            AppNotificationManager.Default.Show(notification);
            System.Diagnostics.Trace.WriteLine($"[notify] TurnComplete toast shown for {sessionId}");
        }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"[notify] TurnComplete toast error: {ex.Message}"); }
    }

    public static void ShowError(string sessionId, string message)
    {
        if (!_registered || IsWindowFocused) return;
        try
        {
            var notification = new AppNotificationBuilder()
                .AddText($"🔴 Chat error")
                .AddText(message)
                .AddArgument("action", "focus")
                .AddArgument("sessionId", sessionId)
                .BuildNotification();
            AppNotificationManager.Default.Show(notification);
            System.Diagnostics.Trace.WriteLine($"[notify] Error toast shown for {sessionId}");
        }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"[notify] Error toast error: {ex.Message}"); }
    }

    static void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        System.Diagnostics.Trace.WriteLine($"[notify] Notification clicked: {args.Argument}");
    }

    public static void Cleanup()
    {
        if (!_registered) return;
        try { AppNotificationManager.Default.Unregister(); } catch { }
    }
}

static class PInvoke
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(nint hWnd);
}
