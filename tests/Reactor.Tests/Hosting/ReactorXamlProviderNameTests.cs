using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Markup;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Hosting;

/// <summary>
/// <c>ReactorApplication</c> resolves the XAML compiler's generated metadata provider by an
/// assembly-qualified name and silently falls back to WinUI's provider when it does not resolve,
/// so a rename of the assembly or of <c>Reactor.csproj</c> would go unnoticed at run time.
/// Only resolves the type; constructing it needs a live WinUI runtime.
/// </summary>
public sealed class ReactorXamlProviderNameTests
{
    [Fact]
    public void GeneratedProviderName_ResolvesInTheCoreAssembly()
    {
        Assert.Equal(typeof(Element).Assembly.GetName().Name, ReactorApplication.ReactorProviderAssemblyName);

        var type = global::System.Type.GetType(
            ReactorApplication.ReactorProviderTypeName + ", " + ReactorApplication.ReactorProviderAssemblyName,
            throwOnError: false);

        Assert.NotNull(type);
        Assert.Same(typeof(Element).Assembly, type!.Assembly);
        Assert.True(typeof(IXamlMetadataProvider).IsAssignableFrom(type));
    }
}
