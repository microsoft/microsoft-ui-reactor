#nullable enable

using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Microsoft.UI.Reactor.VsExtension.Embed;
using Microsoft.UI.Reactor.VsExtension.Logging;
using Microsoft.UI.Reactor.VsExtension.Package;
using Microsoft.UI.Reactor.VsExtension.Session;

namespace Microsoft.UI.Reactor.VsExtension.UI
{
    public partial class ReactorEmbedControl : UserControl
    {
        private EmbedSession? _session;
        private bool _editorTrackerSubscribed;

        public ReactorEmbedControl()
        {
            InitializeComponent();
            ViewModel = new ReactorEmbedControlViewModel();
            DataContext = ViewModel;
            Placeholder.PlaceholderResized += OnPlaceholderResized;
            ViewModel.PlaceholderRectChanged += OnViewModelPlaceholderRectChanged;
            ViewModel.ForceReloadRequested += OnForceReloadRequested;
            TrySubscribeEditorTracker();
        }

        internal HwndHostPlaceholder Placeholder => PlaceholderHost;

        internal ReactorEmbedControlViewModel ViewModel { get; }

        private IntPtr PlaceholderHwnd => Placeholder?.PlaceholderHwnd ?? IntPtr.Zero;

        internal void StartSession(string csprojPath, string? componentName)
        {
            var package = ReactorPackage.Instance;
            if (package == null)
            {
                return;
            }

            if (ElevationCheck.IsCurrentProcessElevated())
            {
                ViewModel.ShowError("Visual Studio is elevated", "Embedded preview will silently drop input due to UIPI. Restart VS non-elevated to use embedded preview, or use the standalone preview (mur devtools).");
                ViewModel.TransitionTo(EmbedStatus.BuildFailed);
                return;
            }

            if (_session != null)
            {
                DetachSession(_session);
                _session.Dispose();
                _session = null;
            }

            var session = new EmbedSession(
                csprojPath,
                PlaceholderHwnd,
                () => ViewModel.LastPlaceholderRect,
                ViewModel,
                package.Jtf,
                ownerMode: false);
            AttachSession(session);
            _session = session;
            SafeAsync.Run(package.Jtf, () => session.StartAsync(componentName, CancellationToken.None), "StartSession");
        }

        internal void Stop()
        {
            var package = ReactorPackage.Instance;
            var session = _session;
            if (session == null || package == null)
            {
                return;
            }

            SafeAsync.Run(package.Jtf, async () =>
            {
                await session.StopAsync(CancellationToken.None).ConfigureAwait(true);
                DetachSession(session);
                session.Dispose();
                if (ReferenceEquals(_session, session))
                {
                    _session = null;
                }
            }, "Stop");
        }

        internal void ForceReload()
        {
            var package = ReactorPackage.Instance;
            if (_session == null || package == null)
            {
                return;
            }

            SafeAsync.Run(package.Jtf, () => _session.ForceReloadAsync(ViewModel.SelectedComponent, CancellationToken.None), "ForceReload");
        }

        internal void OnToolWindowClosing()
        {
            if (_session != null)
            {
                DetachSession(_session);
                _session.Dispose();
                _session = null;
            }

            UnsubscribeEditorTracker();
        }

        private void AttachSession(EmbedSession session)
        {
            session.ProjectSwitchRequested += OnProjectSwitchRequested;
        }

        private void DetachSession(EmbedSession session)
        {
            session.ProjectSwitchRequested -= OnProjectSwitchRequested;
        }

        private void TrySubscribeEditorTracker()
        {
            var tracker = ReactorPackage.Instance?.EditorTracker;
            if (tracker == null || _editorTrackerSubscribed)
            {
                return;
            }

            tracker.ActiveDocumentChanged += OnActiveDocumentChanged;
            _editorTrackerSubscribed = true;
        }

        private void UnsubscribeEditorTracker()
        {
            var tracker = ReactorPackage.Instance?.EditorTracker;
            if (tracker == null || !_editorTrackerSubscribed)
            {
                return;
            }

            tracker.ActiveDocumentChanged -= OnActiveDocumentChanged;
            _editorTrackerSubscribed = false;
        }

        private void OnActiveDocumentChanged(object? sender, string? path)
        {
            var package = ReactorPackage.Instance;
            var session = _session;
            if (package == null || session == null)
            {
                return;
            }

            SafeAsync.Run(package.Jtf, () => session.OnActiveDocumentChangedAsync(path, CancellationToken.None), "ActiveDocumentChanged");
        }

        private void OnProjectSwitchRequested(object? sender, ProjectSwitchEventArgs args)
        {
            var package = ReactorPackage.Instance;
            if (package == null)
            {
                return;
            }

            SafeAsync.Run(package.Jtf, async () =>
            {
                await package.Jtf.SwitchToMainThreadAsync();
                if (sender is EmbedSession oldSession && ReferenceEquals(_session, oldSession))
                {
                    DetachSession(oldSession);
                    oldSession.Dispose();
                }

                _session = args.NewSession;
                AttachSession(args.NewSession);
                await args.NewSession.StartAsync(args.ComponentToSelect, CancellationToken.None).ConfigureAwait(true);
            }, "ProjectSwitchRequested");
        }

        private void OnPlaceholderResized(object? sender, Rect rect)
        {
            ViewModel.OnPlaceholderResized(rect);
        }

        private void OnViewModelPlaceholderRectChanged(object? sender, Rect rect)
        {
            var package = ReactorPackage.Instance;
            if (_session == null || package == null)
            {
                return;
            }

            SafeAsync.Run(package.Jtf, () => _session.PostResizeAsync((int)rect.Width, (int)rect.Height, CancellationToken.None), "ResizeForward");
        }

        private void OnForceReloadRequested(object? sender, EventArgs e)
        {
            ForceReload();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            TrySubscribeEditorTracker();
            ViewModel.OnLoaded();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            ViewModel.OnUnloaded();
        }
    }
}
