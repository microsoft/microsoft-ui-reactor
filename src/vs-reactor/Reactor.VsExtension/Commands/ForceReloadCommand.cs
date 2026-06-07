#nullable enable

using System;
using System.ComponentModel.Design;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Reactor.VsExtension.Logging;
using Microsoft.UI.Reactor.VsExtension.Package;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Microsoft.UI.Reactor.VsExtension.Commands
{
    internal sealed class ForceReloadCommand
    {
        public static async Task InitializeAsync(AsyncPackage package, CancellationToken ct)
        {
            await package.JoinableTaskFactory.SwitchToMainThreadAsync(ct);
            var menuCommandService = await package.GetServiceAsync(typeof(IMenuCommandService)).ConfigureAwait(true) as IMenuCommandService;
            if (menuCommandService == null)
            {
                await OutputChannel.WriteLineAsync("IMenuCommandService unavailable; Force Reload command not registered.").ConfigureAwait(true);
                return;
            }

            menuCommandService.AddCommand(new MenuCommand(Execute, new CommandID(PackageGuids.CommandSetGuid, CommandIds.ForceReload)));
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
                var window = await package.FindToolWindowAsync(typeof(ReactorEmbedToolWindow), 0, create: false, package.DisposalToken).ConfigureAwait(true) as ReactorEmbedToolWindow;
                if (window?.Frame is IVsWindowFrame)
                {
                    window.Control.ForceReload();
                }
            }, "ForceReloadCommand");
        }
    }
}
