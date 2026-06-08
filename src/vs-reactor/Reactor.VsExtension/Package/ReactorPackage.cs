#nullable enable

using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using Microsoft.UI.Reactor.VsExtension.Commands;
using Microsoft.UI.Reactor.VsExtension.Logging;
using Microsoft.UI.Reactor.VsExtension.UI;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Microsoft.UI.Reactor.VsExtension.Package
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("#110", "#111", "0.1", IconResourceID = 400)]
    [ProvideMenuResource("PackageCommands.CTMENU", 1)]
    [ProvideToolWindow(typeof(ReactorEmbedToolWindow), Style = VsDockStyle.Tabbed, Window = ToolWindowGuids80.SolutionExplorer)]
    // Load when a solution exists so active-document tracking is available for Reactor workspaces
    // without loading the package for every unrelated empty VS launch.
    [ProvideAutoLoad(UIContextGuids80.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
    [Guid(PackageGuids.PackageGuidString)]
    public sealed class ReactorPackage : AsyncPackage
    {
        public static ReactorPackage? Instance { get; private set; }

        public JoinableTaskFactory Jtf => JoinableTaskFactory;

        internal EditorTracker? EditorTracker { get; private set; }

        internal SolutionStateTracker? SolutionState { get; private set; }

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            try
            {
                await base.InitializeAsync(cancellationToken, progress).ConfigureAwait(true);

                if (Environment.GetEnvironmentVariable("Reactor_VsExtension_DebugBreakOnAttach") == "1")
                {
                    System.Diagnostics.Debugger.Launch();
                }

                Instance = this;

                await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

                OutputChannel.Initialize(this);
                await OutputChannel.WriteLineAsync("Reactor Preview package initializing").ConfigureAwait(true);

                await PreviewActiveFileCommand.InitializeAsync(this, cancellationToken).ConfigureAwait(true);
                await StopPreviewCommand.InitializeAsync(this, cancellationToken).ConfigureAwait(true);
                await ForceReloadCommand.InitializeAsync(this, cancellationToken).ConfigureAwait(true);
                await ShowReactorPreviewCommand.InitializeAsync(this, cancellationToken).ConfigureAwait(true);

                var solution = await GetServiceAsync(typeof(SVsSolution)).ConfigureAwait(true) as IVsSolution;
                var rdt = await GetServiceAsync(typeof(SVsRunningDocumentTable)).ConfigureAwait(true) as IVsRunningDocumentTable;
                var dte = await GetServiceAsync(typeof(DTE)).ConfigureAwait(true) as DTE;
                if (solution != null && dte != null)
                {
                    SolutionState = new SolutionStateTracker(solution, dte, JoinableTaskFactory);
                }
                else
                {
                    await OutputChannel.WriteLineAsync("Solution tracker unavailable; preview launches will require explicit project readiness.").ConfigureAwait(true);
                }

                if (rdt != null && dte != null)
                {
                    EditorTracker = new EditorTracker(rdt, dte, JoinableTaskFactory);
                }
                else
                {
                    await OutputChannel.WriteLineAsync("Editor tracker unavailable; active-file auto-select disabled.").ConfigureAwait(true);
                }

                await OutputChannel.WriteLineAsync("Reactor Preview package initialized").ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                try
                {
                    await OutputChannel.WriteLineAsync("[ReactorPackage.InitializeAsync] " + ex.GetType().Name + ": " + ex.Message).ConfigureAwait(false);
                    await OutputChannel.WriteLineAsync(ex.ToString()).ConfigureAwait(false);
                }
                catch
                {
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    EditorTracker?.Dispose();
                    EditorTracker = null;
                    SolutionState?.Dispose();
                    SolutionState = null;
                    if (ReferenceEquals(Instance, this))
                    {
                        Instance = null;
                    }
                }
                catch (Exception ex)
                {
                    SafeAsync.Run(() => OutputChannel.WriteLineAsync("[ReactorPackage.Dispose] " + ex.Message).GetAwaiter().GetResult(), "ReactorPackage.Dispose.Log");
                }
            }

            base.Dispose(disposing);
        }
    }
}
