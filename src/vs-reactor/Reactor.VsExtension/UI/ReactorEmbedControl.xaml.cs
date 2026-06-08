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
        private bool _solutionStateSubscribed;

        public ReactorEmbedControl()
        {
            InitializeComponent();
            ViewModel = new ReactorEmbedControlViewModel(ReactorPackage.Instance?.Jtf);
            DataContext = ViewModel;
            Placeholder.PlaceholderResized += OnPlaceholderResized;
            ViewModel.PlaceholderRectChanged += OnViewModelPlaceholderRectChanged;
            ViewModel.ForceReloadRequested += OnForceReloadRequested;
            TrySubscribeEditorTracker();
            TrySubscribeSolutionState();
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
            UnsubscribeSolutionState();
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

        private void TrySubscribeSolutionState()
        {
            var solutionState = ReactorPackage.Instance?.SolutionState;
            if (solutionState == null || _solutionStateSubscribed)
            {
                return;
            }

            solutionState.SolutionClosing += OnSolutionClosing;
            solutionState.ProjectUnloading += OnProjectUnloading;
            _solutionStateSubscribed = true;
        }

        private void UnsubscribeSolutionState()
        {
            var solutionState = ReactorPackage.Instance?.SolutionState;
            if (solutionState == null || !_solutionStateSubscribed)
            {
                return;
            }

            solutionState.SolutionClosing -= OnSolutionClosing;
            solutionState.ProjectUnloading -= OnProjectUnloading;
            _solutionStateSubscribed = false;
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
            if (package == null || session == null || package.SolutionState?.IsSolutionReady == false)
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
                if (sender is not EmbedSession oldSession || !ReferenceEquals(_session, oldSession))
                {
                    args.NewSession.Dispose();
                    return;
                }

                DetachSession(oldSession);
                oldSession.Dispose();
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

        private void OnSolutionClosing(object? sender, EventArgs e)
        {
            Stop();
        }

        private void OnProjectUnloading(object? sender, string projectPath)
        {
            var session = _session;
            var currentProject = session == null
                ? null
                : System.IO.Path.GetFullPath(session.CsprojPath).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
            if (currentProject != null && string.Equals(currentProject, projectPath, StringComparison.OrdinalIgnoreCase))
            {
                Stop();
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            TrySubscribeEditorTracker();
            TrySubscribeSolutionState();
            ViewModel.OnLoaded();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            ViewModel.OnUnloaded();
        }
    }
}
