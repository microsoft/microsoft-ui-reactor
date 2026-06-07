#nullable enable

using System;
using System.ComponentModel.Design;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Reactor.VsExtension.Commands;
using Microsoft.UI.Reactor.VsExtension.Logging;
using Microsoft.UI.Reactor.VsExtension.Package;
using Microsoft.VisualStudio.Sdk.TestFramework;
using Microsoft.VisualStudio.Sdk.TestFramework.Mocks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Xunit;

namespace Reactor.VsExtension.SdkTests
{
    [Collection(MockedVS.Collection)]
    public sealed class ReactorPackageSdkTests : IDisposable
    {
        private readonly GlobalServiceProvider _services;
        private readonly MenuCommandService _commands;

        public ReactorPackageSdkTests(GlobalServiceProvider services)
        {
            _services = services;
            _services.Reset();
            _commands = new MenuCommandService(new ServiceContainer());
            OutputChannel.ResetForTest();
            RegisterCommonServices();
        }

        [Fact]
        public async Task Package_InitializeAsync_Succeeds()
        {
            var package = new ReactorPackage();
            try
            {
                await InitializePackageAsync(package);

                Assert.Same(package, ReactorPackage.Instance);
            }
            finally
            {
                DisposePackage(package);
            }
        }

        [Fact]
        public void Package_RegistersToolWindow()
        {
            var attributes = typeof(ReactorPackage).GetCustomAttributes(typeof(ProvideToolWindowAttribute), inherit: false);

            Assert.Single(attributes);
        }

        [Fact(Skip = "Microsoft.VisualStudio.SDK.TestFramework does not surface IMenuCommandService through AsyncPackage.GetServiceAsync under dotnet test; see src/vs-reactor/TESTING.md.")]
        public async Task Commands_DispatchedThroughOleCommandTarget()
        {
            var package = new ReactorPackage();
            try
            {
                await InitializePackageAsync(package);

                Assert.NotNull(_commands.FindCommand(new CommandID(PackageGuids.CommandSetGuid, CommandIds.PreviewActiveFile)));
                Assert.NotNull(_commands.FindCommand(new CommandID(PackageGuids.CommandSetGuid, CommandIds.StopPreview)));
                Assert.NotNull(_commands.FindCommand(new CommandID(PackageGuids.CommandSetGuid, CommandIds.ForceReload)));
                _commands.FindCommand(new CommandID(PackageGuids.CommandSetGuid, CommandIds.StopPreview))!.Invoke();
            }
            finally
            {
                DisposePackage(package);
            }
        }

        [Fact(Skip = "VS SDK TestFramework does not provide enough IVsUIShell frame services for AsyncPackage.FindToolWindowAsync under dotnet test; see src/vs-reactor/TESTING.md.")]
        public async Task Package_FindsToolWindowAsync_ReturnsInstance()
        {
            var package = new ReactorPackage();
            try
            {
                await InitializePackageAsync(package);

                var window = await package.FindToolWindowAsync(typeof(ReactorEmbedToolWindow), id: 0, create: true, cancellationToken: CancellationToken.None);

                Assert.IsType<ReactorEmbedToolWindow>(window);
            }
            finally
            {
                DisposePackage(package);
            }
        }

        [Fact(Skip = "Solution-close disposal requires a real IVsSolution event source; covered by Tier C.1 manual smoke.")]
        public void Package_OnSolutionClose_DisposesLauncher()
        {
        }

        public void Dispose()
        {
            OutputChannel.ResetForTest();
        }

        private void RegisterCommonServices()
        {
            _services.AddService(typeof(SVsActivityLog), new MockVsActivityLog());
            _services.AddService(typeof(SVsOutputWindow), VsSdkMocks.CreateOutputWindow());
            _services.AddService(typeof(SVsRunningDocumentTable), VsSdkMocks.CreateRunningDocumentTable());
            _services.AddService(typeof(IMenuCommandService), _commands);
        }

        private static async Task InitializePackageAsync(ReactorPackage package)
        {
            var method = typeof(ReactorPackage).GetMethod("InitializeAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            var task = (Task)method!.Invoke(package, new object?[] { CancellationToken.None, null })!;
            await task;
        }

        private static void DisposePackage(ReactorPackage package)
        {
            var method = typeof(ReactorPackage).GetMethod("Dispose", BindingFlags.Instance | BindingFlags.NonPublic, binder: null, new[] { typeof(bool) }, modifiers: null);
            method?.Invoke(package, new object[] { true });
        }
    }
}
