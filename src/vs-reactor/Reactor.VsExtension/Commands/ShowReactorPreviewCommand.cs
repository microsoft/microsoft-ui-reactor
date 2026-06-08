#nullable enable

using System;
using System.ComponentModel.Design;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using Microsoft.UI.Reactor.VsExtension.Embed;
using Microsoft.UI.Reactor.VsExtension.Logging;
using Microsoft.UI.Reactor.VsExtension.Package;
using Microsoft.UI.Reactor.VsExtension.UI;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Microsoft.UI.Reactor.VsExtension.Commands
{
    /// <summary>
    /// Opens the Reactor Preview tool window via View → Other Windows → Reactor Preview.
    /// If a Reactor project / .cs file is currently open, also attempts to auto-start
    /// the preview against the containing .csproj.
    /// </summary>
    internal sealed class ShowReactorPreviewCommand
    {
        public static async Task InitializeAsync(AsyncPackage package, CancellationToken ct)
        {
            await package.JoinableTaskFactory.SwitchToMainThreadAsync(ct);
            var menuCommandService = await package.GetServiceAsync(typeof(IMenuCommandService)).ConfigureAwait(true) as IMenuCommandService;
            if (menuCommandService == null)
            {
                await OutputChannel.WriteLineAsync("IMenuCommandService unavailable; Show Reactor Preview command not registered.").ConfigureAwait(true);
                return;
            }

            var cmdId = new CommandID(PackageGuids.CommandSetGuid, CommandIds.ShowReactorPreview);
            var menuCommand = new MenuCommand(Execute, cmdId);
            menuCommandService.AddCommand(menuCommand);
            await OutputChannel.WriteLineAsync("Show Reactor Preview command registered (View → Other Windows → Reactor Preview).").ConfigureAwait(true);
        }

        private static void Execute(object sender, EventArgs e)
        {
            var package = ReactorPackage.Instance;
            if (package == null)
            {
                return;
            }

            SafeAsync.Run(package.Jtf, async () =>
            {
                await package.JoinableTaskFactory.SwitchToMainThreadAsync();

                // Always open the tool window first so the user sees the placeholder.
                var window = await package.FindToolWindowAsync(
                    typeof(ReactorEmbedToolWindow), 0, create: true, package.DisposalToken).ConfigureAwait(true)
                    as ReactorEmbedToolWindow;
                if (window?.Frame is IVsWindowFrame frame)
                {
                    ErrorHandler.ThrowOnFailure(frame.Show());
                }
                else
                {
                    await OutputChannel.WriteLineAsync("Failed to open Reactor Preview tool window.").ConfigureAwait(true);
                    return;
                }

                // Best-effort: if there's an active .cs document inside a .csproj, auto-start.
                var dte = await package.GetServiceAsync(typeof(DTE)).ConfigureAwait(true) as DTE;
                var docPath = dte?.ActiveDocument?.FullName;
                if (string.IsNullOrEmpty(docPath))
                {
                    await OutputChannel.WriteLineAsync("Reactor Preview opened. Open a .cs file inside a Reactor project, then use the Preview Active File toolbar button to start.").ConfigureAwait(true);
                    return;
                }

                var activePath = docPath!;
                if (!IsUnderLoadedSolution(dte, activePath))
                {
                    await OutputChannel.WriteLineAsync("Reactor Preview opened. Active file is outside the loaded solution; use Preview Active File to launch it explicitly.").ConfigureAwait(true);
                    return;
                }

                var csproj = await Task.Run(() => ProjectContextResolver.FindContainingCsproj(activePath), package.DisposalToken).ConfigureAwait(true);
                if (csproj == null)
                {
                    await OutputChannel.WriteLineAsync("Reactor Preview opened. No .csproj found above " + docPath + "; nothing to auto-launch.").ConfigureAwait(true);
                    return;
                }

                if (package.SolutionState != null && !package.SolutionState.CanPreviewProject(csproj, out var message))
                {
                    await OutputChannel.WriteLineAsync("Reactor Preview opened. " + message).ConfigureAwait(true);
                    return;
                }

                await OutputChannel.WriteLineAsync("Reactor Preview opened; auto-starting against " + csproj).ConfigureAwait(true);
                window.Control.StartSession(csproj, componentName: null);
            }, "ShowReactorPreviewCommand");
        }

        private static bool IsUnderLoadedSolution(DTE? dte, string filePath)
        {
            var solutionPath = dte?.Solution?.FullName;
            if (string.IsNullOrWhiteSpace(solutionPath))
            {
                return false;
            }

            var solutionDir = Path.GetDirectoryName(solutionPath);
            if (string.IsNullOrWhiteSpace(solutionDir))
            {
                return false;
            }

            var root = Path.GetFullPath(solutionDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var fullPath = Path.GetFullPath(filePath);
            return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
    }
}
