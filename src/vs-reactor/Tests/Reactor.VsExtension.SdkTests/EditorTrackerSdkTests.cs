#nullable enable

using System.Threading.Tasks;
using EnvDTE;
using Microsoft.UI.Reactor.VsExtension.UI;
using Microsoft.VisualStudio.Sdk.TestFramework;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Xunit;

namespace Reactor.VsExtension.SdkTests
{
    [Collection(MockedVS.Collection)]
    public sealed class EditorTrackerSdkTests
    {
        private readonly GlobalServiceProvider _services;

        public EditorTrackerSdkTests(GlobalServiceProvider services)
        {
            _services = services;
            _services.Reset();
        }

        [Fact]
        public async Task EditorTracker_RaisesOnRDTEvent()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var rdt = VsSdkMocks.CreateRunningDocumentTable();
            var dte = VsSdkMocks.CreateDte();
            var path = @"C:\\repo\\Component.cs";
            using var tracker = new EditorTracker(rdt, dte, ThreadHelper.JoinableTaskFactory, () => path);

            string? raisedPath = null;
            tracker.ActiveDocumentChanged += (_, activePath) => raisedPath = activePath;

            tracker.OnAfterSave(1);
            await Task.Delay(250);

            Assert.Equal(path, raisedPath);
        }
    }
}
