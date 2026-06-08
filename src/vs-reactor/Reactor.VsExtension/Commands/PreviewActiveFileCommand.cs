#nullable enable

using System;
using System.ComponentModel.Design;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using Microsoft.UI.Reactor.VsExtension.Embed;
using Microsoft.UI.Reactor.VsExtension.Logging;
using Microsoft.UI.Reactor.VsExtension.Package;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Microsoft.UI.Reactor.VsExtension.Commands
{
    internal sealed class PreviewActiveFileCommand
    {
        public static async Task InitializeAsync(AsyncPackage package, CancellationToken ct)
        {
            await package.JoinableTaskFactory.SwitchToMainThreadAsync(ct);
            var menuCommandService = await package.GetServiceAsync(typeof(IMenuCommandService)).ConfigureAwait(true) as IMenuCommandService;
            if (menuCommandService == null)
            {
                await OutputChannel.WriteLineAsync("IMenuCommandService unavailable; Preview Active File command not registered.").ConfigureAwait(true);
                return;
            }

            var cmdId = new CommandID(PackageGuids.CommandSetGuid, CommandIds.PreviewActiveFile);
            var menuCommand = new MenuCommand(Execute, cmdId);
            menuCommandService.AddCommand(menuCommand);
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
                var dte = await package.GetServiceAsync(typeof(DTE)).ConfigureAwait(true) as DTE;
                var docPath = dte?.ActiveDocument?.FullName;
                if (string.IsNullOrEmpty(docPath))
                {
                    await OutputChannel.WriteLineAsync("No active document; aborting Preview Active File").ConfigureAwait(true);
                    return;
                }

                var activePath = docPath!;
                var csproj = await Task.Run(() => ProjectContextResolver.FindContainingCsproj(activePath), package.DisposalToken).ConfigureAwait(true);
                if (csproj == null)
                {
                    await OutputChannel.WriteLineAsync("No .csproj found above " + docPath).ConfigureAwait(true);
                    return;
                }

                if (package.SolutionState != null && !package.SolutionState.CanPreviewProject(csproj, out var message))
                {
                    await OutputChannel.WriteLineAsync(message).ConfigureAwait(true);
                    return;
                }

                await ShowToolWindowAndStartAsync(csproj, componentName: null).ConfigureAwait(true);
            }, "PreviewActiveFileCommand");
        }

        internal static async Task ShowToolWindowAndStartAsync(string csprojPath, string? componentName)
        {
            var package = ReactorPackage.Instance;
            if (package == null)
            {
                return;
            }

            await package.JoinableTaskFactory.SwitchToMainThreadAsync();
            var window = await package.FindToolWindowAsync(typeof(ReactorEmbedToolWindow), 0, create: true, package.DisposalToken).ConfigureAwait(true) as ReactorEmbedToolWindow;
            if (window?.Frame is IVsWindowFrame frame)
            {
                ErrorHandler.ThrowOnFailure(frame.Show());
                window.Control.StartSession(csprojPath, componentName);
            }
        }
    }
}
