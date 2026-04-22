using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using SWF = global::System.Windows.Forms;
using XamlApp = Microsoft.UI.Xaml.Application;

namespace Microsoft.UI.Reactor.Interop.WinForms;

/// <summary>
/// Bootstraps WinAppSDK/WinUI infrastructure for WinForms-primary applications.
///
/// Uses <see cref="XamlApp.Start"/> to initialize the native XAML runtime, then
/// hands the message loop to WinForms via <see cref="SWF.Application.Run()"/>.
/// This ensures WinForms Tab navigation (IsDialogMessage), focus management,
/// and IMessageFilter all work correctly alongside XAML Islands.
///
/// Usage:
///   XamlIslandBootstrap.Run(() =>
///   {
///       var form = new MyWinFormsForm();
///       form.Show();
///   });
///
/// To exit: call <see cref="SWF.Application.Exit()"/> (not XamlApp.Current.Exit).
/// </summary>
public static partial class XamlIslandBootstrap
{
    private static Action? _onReady;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessDpiAwarenessContext(nint value);

    private static readonly nint DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;

    [DllImport("Microsoft.UI.Windowing.Core.dll", EntryPoint = "ContentPreTranslateMessage")]
    private static extern int ContentPreTranslateMessage(ref NativeMsg msg);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int nExitCode);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMsg
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int pt_x;
        public int pt_y;
    }

    /// <summary>
    /// Initializes WinAppSDK, starts the XAML runtime, and calls
    /// <paramref name="onReady"/> once the infrastructure is ready.
    ///
    /// This method blocks (it runs the WinForms message loop). Create and show
    /// your WinForms windows inside <paramref name="onReady"/>.
    /// Call <see cref="SWF.Application.Exit()"/> to quit.
    ///
    /// Must be called on the STA UI thread.
    /// </summary>
    public static void Run(Action onReady)
    {
        SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        WinRT.ComWrappersSupport.InitializeComWrappers();

        _onReady = onReady;

        // Application.Start initializes the native XAML runtime and creates a
        // DispatcherQueue. The callback runs before Application.Start enters its
        // own message loop — we block inside OnLaunched with WinForms' Application.Run
        // so WinForms owns the message loop instead.
        XamlApp.Start(_ =>
        {
            var context = new DispatcherQueueSynchronizationContext(
                DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new IslandApplication();
        });
    }

    /// <summary>
    /// Minimal WinUI Application for XAML Islands. Loads theme resources, hooks the
    /// keyboard message filter, and fires the <see cref="_onReady"/> callback.
    /// No WinUI Window is created — the WinForms app owns the windows.
    /// </summary>
    private partial class IslandApplication : XamlApp, IXamlMetadataProvider
    {
        private readonly IXamlMetadataProvider _provider =
            new Microsoft.UI.Xaml.XamlTypeInfo.XamlControlsXamlMetaDataProvider();

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            // Must access Resources here (not in constructor) — the native XAML
            // runtime isn't fully initialized until Application.Start's setup phase completes.
            Resources.MergedDictionaries.Add(new XamlControlsResources());

            // Route keyboard/input messages through WinAppSDK for XAML Islands.
            // This filter is invoked by WinForms' Application.Run message loop.
            SWF.Application.AddMessageFilter(new XamlPreTranslateFilter());

            _onReady?.Invoke();
            _onReady = null;

            // Block here with WinForms' message loop instead of falling through to
            // Application.Start's built-in loop. WinForms' loop calls IsDialogMessage
            // (enabling Tab navigation) and invokes IMessageFilter (enabling
            // ContentPreTranslateMessage for XAML Islands). Both WinForms and WinUI
            // HWNDs receive messages correctly — they're all on the same thread.
            SWF.Application.Run();

            // WinForms loop exited — post WM_QUIT so Application.Start's
            // DispatcherQueue.RunEventLoop() exits immediately when we return.
            PostQuitMessage(0);
        }

        public IXamlType GetXamlType(Type type) => _provider.GetXamlType(type);
        public IXamlType GetXamlType(string fullName) => _provider.GetXamlType(fullName);
        public XmlnsDefinition[] GetXmlnsDefinitions() => _provider.GetXmlnsDefinitions();
    }

    /// <summary>
    /// Routes Win32 messages through WinAppSDK so keyboard input, accelerators,
    /// and Tab navigation work inside XAML Islands hosted in WinForms.
    /// </summary>
    private class XamlPreTranslateFilter : SWF.IMessageFilter
    {
        public bool PreFilterMessage(ref SWF.Message m)
        {
            var msg = new NativeMsg
            {
                hwnd = m.HWnd,
                message = (uint)m.Msg,
                wParam = m.WParam,
                lParam = m.LParam,
            };
            return ContentPreTranslateMessage(ref msg) != 0;
        }
    }
}
