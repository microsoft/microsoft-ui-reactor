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
