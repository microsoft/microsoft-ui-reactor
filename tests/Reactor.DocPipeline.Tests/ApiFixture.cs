#pragma warning disable CS0169, CS0414, IDE0051, IDE0060, CA1822, CA1823

namespace Microsoft.UI.Reactor.Cli.Docs.Tests.ApiFixture;

// Public surface that ReferenceFromAssemblyTests reads back from this test
// assembly's metadata. Nothing calls these members.

public enum FixtureMode { Off = 0, On = 1, Auto = 4 }

[Flags]
public enum FixtureFlags : byte { None = 0, Left = 1, Right = 2, Both = Left | Right }

public class FixtureApi
{
    public const double Ratio = 0.25;

    public string? Name { get; set; }

    public int? Count { get; init; }

    public (int Index, string? Label) Pair { get; } = default;

    public (int? Index, string? Label) Mixed { get; } = default;

    public KeyValuePair<string?, int[]?> Entry { get; } = default;

    public Dictionary<int, string?> Lookup { get; } = new();

    public (string? First, int Second)? MaybePair { get; } = default;

    public static float Curve(float tension = 0.8f, double scale = 1.5, FixtureMode mode = FixtureMode.On,
        string label = "x", string? missing = null, CancellationToken token = default) => tension;

    public (int Index, string? Name) Find(IReadOnlyList<string?> items, int? start = null) => default;

    protected void ForDerived() { }

    protected internal void ForDerivedOrAssembly() { }

    private protected void PrivateProtected() { }

    internal void Hidden() { }

    private int _secret;
}

public sealed class SealedFixture
{
    public void Shown() { }
}

public class GenericBaseFixture<T>
{
    public static T Pick(T fallback = default!) => fallback;
}

public class DerivedFromGenericFixture : GenericBaseFixture<string>
{
}

public class RequiredFixture
{
    public required string Key { get; init; }

    public string? Note { get; init; }
}

public class RequiredWithObsoleteCtorFixture
{
    [Obsolete("Use the factory.")]
    public RequiredWithObsoleteCtorFixture() { }

    public required int Id { get; init; }
}

internal sealed class InternalFixture
{
    public void NotShown() { }
}
