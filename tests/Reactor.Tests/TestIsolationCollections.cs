using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// xUnit collection marker for tests that replace process-wide console or trace
/// state. These tests must not overlap any other collection because
/// <see cref="Console.Out"/>, <see cref="Console.Error"/>, and
/// <see cref="System.Diagnostics.Trace.Listeners"/> are global to the test process.
/// </summary>
[CollectionDefinition("ConsoleTests", DisableParallelization = true)]
public sealed class ConsoleTestsCollection { }

/// <summary>
/// xUnit collection marker for tests that subscribe to
/// <see cref="TaskScheduler.UnobservedTaskException"/> and force finalization. The
/// event is process-wide, so these tests need exclusive execution to avoid counting
/// faulted tasks owned by unrelated tests.
/// </summary>
[CollectionDefinition("UnobservedTaskException", DisableParallelization = true)]
public sealed class UnobservedTaskExceptionCollection { }

/// <summary>
/// xUnit collection marker for tests that mutate
/// <see cref="Microsoft.UI.Reactor.Core.ApplicationPersistedScope.Default"/>.
/// The singleton is process-wide, so tests that clear or write it must not run
/// concurrently with other tests that assert values remain present.
/// </summary>
[CollectionDefinition("PersistedStateCache", DisableParallelization = true)]
public sealed class PersistedStateCacheCollection { }

/// <summary>
/// xUnit collection marker for tests that mutate
/// <see cref="Microsoft.UI.Reactor.JumpList"/> static state
/// (<c>AppUserModelId</c>, <c>ShowRecent</c>, <c>ShowFrequent</c>) or call
/// <c>JumpList.ResetForTests()</c>. The statics are process-wide, so a concurrent
/// <c>ResetForTests()</c> from another class can clobber an in-flight test's
/// configuration mid-execution.
/// </summary>
[CollectionDefinition("JumpListGlobals", DisableParallelization = true)]
public sealed class JumpListGlobalsCollection { }

/// <summary>
/// xUnit collection marker for tests that mutate the process-wide source-map /
/// devtools flags — <see cref="Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled"/>
/// and <c>ReactorApp.DevtoolsEnabled</c>.
///
/// <para>These are one flag in two guises: the <c>DevtoolsEnabled</c> setter mirrors
/// its value into <c>ReactorSourceMap.Enabled</c>, and
/// <c>ResetDevtoolsEnabledForTests()</c> writes it to false directly. So a devtools
/// test's constructor or Dispose can clear the flag out from under a source-map test
/// that just set it, in a different class, mid-assertion. Both families therefore
/// share this collection rather than each getting their own.</para>
/// </summary>
[CollectionDefinition("SourceMapGlobals", DisableParallelization = true)]
public sealed class SourceMapGlobalsCollection { }

/// <summary>
/// xUnit collection marker for tests that mutate
/// <see cref="Microsoft.UI.Reactor.Hosting.HotReloadService"/> process-wide
/// state (the pending-update flag via <c>UpdateApplication</c>). These tests
/// must run exclusively so a concurrent test does not observe or clear a
/// pending flag they raised.
/// </summary>
[CollectionDefinition("HotReload", DisableParallelization = true)]
public sealed class HotReloadCollection { }

/// <summary>
/// xUnit collection marker for tests that mutate process-wide
/// <see cref="Microsoft.UI.Reactor.Diagnostics.LayoutFootgunDetector"/> state — the diagnostic
/// <c>Sink</c>, the emit-once dedup set (<c>ResetForTests()</c>), and
/// <see cref="Microsoft.UI.Reactor.Core.ReactorFeatureFlags.WarnLayoutFootguns"/>. These statics
/// are global to the test process, so the tests must run exclusively to avoid cross-test
/// interference.
/// </summary>
[CollectionDefinition("LayoutFootgunDetector", DisableParallelization = true)]
public sealed class LayoutFootgunDetectorCollection { }

/// <summary>
/// xUnit collection marker for tests that probe MSIX package identity — they call
/// <c>PackageRuntime.ResetForTests()</c> / poke its cached flag and install a
/// process-wide <see cref="AppDomain.FirstChanceException"/> handler. Both are global
/// to the test process: a concurrent reset from another class would make the caching
/// assertions flaky, and a first-chance handler would otherwise observe exceptions
/// thrown by unrelated tests running in parallel.
/// </summary>
[CollectionDefinition("PackageIdentityProbe", DisableParallelization = true)]
public sealed class PackageIdentityProbeCollection { }

/// <summary>
/// xUnit collection marker for tests that create and delete icon assets under
/// <see cref="AppContext.BaseDirectory"/> — the test binary's own output directory —
/// and/or mutate the process-wide app-root override used by the <c>TitleBar</c> icon
/// default (<c>TitleBarIconDefault.SetBaseDirectoryForTests</c>).
/// <para>
/// Both are global to the test process. Two classes writing into the same
/// <c>Assets</c> directory can delete each other's fixtures mid-run, and a concurrent
/// override would make one class's resolution assertions read another's app root.
/// </para>
/// </summary>
[CollectionDefinition("AppBaseDirectoryAssets", DisableParallelization = true)]
public sealed class AppBaseDirectoryAssetsCollection { }
