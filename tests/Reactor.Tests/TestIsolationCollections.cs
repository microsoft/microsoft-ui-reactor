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
