using Xunit;

namespace Microsoft.UI.Reactor.Tests.Docking;

/// <summary>
/// xUnit collection marker for tests that mutate process-global docking
/// state (<c>DockHostRegistry</c>, <c>DockingStrings.Resolver</c>,
/// <c>PreviousContainerTracker</c>, etc.). xUnit runs distinct collections
/// in parallel; the constructor/<see cref="System.IDisposable.Dispose"/>
/// save/restore pattern only protects within the same class, so tests
/// that touch these globals share this collection to serialize their
/// runs. Spec 045 §2.21 / §2.26 — see the review note on test isolation.
/// </summary>
[CollectionDefinition("DockingGlobals", DisableParallelization = true)]
public sealed class DockingGlobalsCollection { }
