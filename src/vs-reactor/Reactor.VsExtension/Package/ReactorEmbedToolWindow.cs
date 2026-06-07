#nullable enable

using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Reactor.VsExtension.Commands;
using Microsoft.UI.Reactor.VsExtension.UI;
using Microsoft.VisualStudio.Shell;

namespace Microsoft.UI.Reactor.VsExtension.Package
{
    [Guid(PackageGuids.ToolWindowGuidString)]
    public sealed class ReactorEmbedToolWindow : ToolWindowPane
    {
        public ReactorEmbedToolWindow()
            : base(null)
        {
            Caption = "Reactor Preview";
            Content = new ReactorEmbedControl();
            ToolBar = new System.ComponentModel.Design.CommandID(PackageGuids.CommandSetGuid, CommandIds.ReactorPreviewToolbar);
        }

        public ReactorEmbedControl Control => (ReactorEmbedControl)Content;

        protected override void OnClose()
        {
            try
            {
                Control.OnToolWindowClosing();
            }
            catch
            {
            }

            base.OnClose();
        }
    }
}
