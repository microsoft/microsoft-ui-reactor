using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml.Markup;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// <c>ReactorHostControl</c> in XAML markup — <c>&lt;reactor:ReactorHostControl ComponentType="local:X"/&gt;</c>.
/// The control itself needs a live WinUI runtime, so these pin the pure-managed halves:
/// assignment-time validation of <c>ComponentType</c>, and root creation through the app's
/// XAML type information (faked here with an <see cref="IXamlMetadataProvider"/>).
/// The markup half — that the XAML compiler accepts the element and generates an activator
/// for the named component — is proved by building <c>samples/ReactorHostControlDemo</c>,
/// whose counter panel is declared that way.
/// </summary>
public sealed partial class ReactorHostControlXamlTests
{
    public sealed class Card : Component
    {
        public override Element Render() => TextBlock("card");
    }

    public abstract class AbstractCard : Component;

    public sealed class Generic<T> : Component
    {
        public override Element Render() => TextBlock(typeof(T).Name);
    }

    [Fact]
    public void Validate_AcceptsAConcreteComponent()
    {
        ReactorHostControl.ValidateComponentType(typeof(Card));
        ReactorHostControl.ValidateComponentType(typeof(Generic<int>));
    }

    [Theory]
    [InlineData(typeof(string), "must derive from")]
    [InlineData(typeof(Component), "abstract")]
    [InlineData(typeof(AbstractCard), "abstract")]
    [InlineData(typeof(Generic<>), "open generic")]
    public void Validate_RejectsTypesThatCanNeverBeARoot(Type type, string reason)
    {
        var ex = Assert.Throws<ArgumentException>(() => ReactorHostControl.ValidateComponentType(type));
        Assert.Contains(reason, ex.Message, StringComparison.Ordinal);
        Assert.Equal("ComponentType", ex.ParamName);
    }

    [Fact]
    public void Create_ActivatesThroughTheAppsXamlTypeInfo()
    {
        var provider = new FakeProvider();
        provider.Add(typeof(Card), () => new Card());

        var component = ReactorHostControl.CreateComponent(typeof(Card), provider);

        Assert.IsType<Card>(component);
        Assert.Equal(1, provider.Activations);
    }

    [Fact]
    public void Create_TypeWithoutXamlInfo_ThrowsPointingAtComponentFactory()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => ReactorHostControl.CreateComponent(typeof(Card), new FakeProvider()));

        Assert.Contains("ComponentFactory", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_WithoutAProvider_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => ReactorHostControl.CreateComponent(typeof(Card), provider: null));
    }

    [Fact]
    public void Create_NonConstructibleXamlType_Throws()
    {
        var provider = new FakeProvider();
        provider.Add(typeof(Card), activate: null);

        Assert.Throws<InvalidOperationException>(() => ReactorHostControl.CreateComponent(typeof(Card), provider));
    }

    [Fact]
    public void Create_ValidatesBeforeTouchingTheProvider()
    {
        var provider = new FakeProvider();
        provider.Add(typeof(string), () => "not a component");

        Assert.Throws<ArgumentException>(() => ReactorHostControl.CreateComponent(typeof(string), provider));
        Assert.Equal(0, provider.Activations);
    }

    // ── Fakes ────────────────────────────────────────────────────────────

    private sealed partial class FakeProvider : IXamlMetadataProvider
    {
        private readonly Dictionary<Type, FakeXamlType> _types = new();
        public int Activations { get; private set; }

        public void Add(Type type, Func<object>? activate)
            => _types[type] = new FakeXamlType(type, activate is null ? null : () => { Activations++; return activate(); });

        public IXamlType? GetXamlType(Type type) => _types.TryGetValue(type, out var t) ? t : null;
        public IXamlType? GetXamlType(string fullName) => _types.Values.FirstOrDefault(t => t.FullName == fullName);
        public XmlnsDefinition[] GetXmlnsDefinitions() => [];
    }

    private sealed partial class FakeXamlType(Type type, Func<object>? activate) : IXamlType
    {
        public string FullName => type.FullName!;
        public Type UnderlyingType => type;
        public bool IsConstructible => activate is not null;
        public object ActivateInstance() => activate!();

        public IXamlType? BaseType => null;
        public IXamlMember? ContentProperty => null;
        public bool IsArray => false;
        public bool IsCollection => false;
        public bool IsDictionary => false;
        public bool IsMarkupExtension => false;
        public bool IsBindable => false;
        public bool IsReturnTypeStub => false;
        public bool IsLocalType => true;
        public IXamlType? ItemType => null;
        public IXamlType? KeyType => null;
        public IXamlType? BoxedType => null;
        public IXamlMember? GetMember(string name) => null;
        public void AddToMap(object instance, object key, object item) => throw new NotSupportedException();
        public void AddToVector(object instance, object item) => throw new NotSupportedException();
        public void RunInitializer() { }
        public object CreateFromString(string value) => throw new NotSupportedException();
    }
}
