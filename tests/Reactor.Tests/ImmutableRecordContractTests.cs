using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Reactor.Tests;

/// <summary>
/// Regression test for issue #264: ensures public record types documented as
/// "immutable" truly have init-only setters (not plain set).
/// </summary>
public class ImmutableRecordContractTests
{
    /// <summary>
    /// At the IL level, an <c>init</c> setter is a <c>set</c> method whose
    /// return type carries <c>modreq(IsExternalInit)</c>. This helper detects that.
    /// </summary>
    private static bool IsInitOnly(PropertyInfo property)
    {
        var setter = property.GetSetMethod(nonPublic: true);
        if (setter is null)
            return false; // read-only (no setter at all) — still immutable

        var returnParam = setter.ReturnParameter;
        return returnParam.GetRequiredCustomModifiers()
            .Any(t => t.FullName == "System.Runtime.CompilerServices.IsExternalInit");
    }

    public static TheoryData<Type> ImmutableRecordTypes => new()
    {
        { typeof(TrayIconSpec) },
        { typeof(WindowSpec) },
        { typeof(Command) },
        { typeof(Command<>) },
    };

    [Theory]
    [MemberData(nameof(ImmutableRecordTypes))]
    public void All_Public_Properties_Are_InitOnly(Type type)
    {
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetSetMethod(nonPublic: true) is not null);

        foreach (var prop in properties)
        {
            Assert.True(
                IsInitOnly(prop),
                $"{type.Name}.{prop.Name} has a plain 'set' accessor — " +
                $"expected 'init' to preserve immutability contract.");
        }
    }

    [Theory]
    [MemberData(nameof(ImmutableRecordTypes))]
    public void Has_At_Least_One_Public_Property(Type type)
    {
        // Sanity check: ensure the test is actually verifying something.
        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Assert.NotEmpty(props);
    }
}
