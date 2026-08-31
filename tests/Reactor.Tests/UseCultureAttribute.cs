using System.Globalization;
using System.Reflection;
using Xunit.v3;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Pins <see cref="CultureInfo.CurrentCulture"/> (and optionally the UI culture) for the duration
/// of a test, restoring the previous value afterwards.
/// </summary>
/// <remarks>
/// <para>
/// Issue #1159. Two distinct uses, and the distinction matters:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///     Pin <c>en-US</c> on a test whose literal expectation is incidental to what it checks — it
///     asserts a formatted string only because that is how the value is observable. Such a test is
///     otherwise silently host-dependent.
///     </description>
///   </item>
///   <item>
///     <description>
///     Pin a <em>comma-decimal</em> culture (e.g. <c>nl-NL</c>) on a test whose point is that the
///     product honours the ambient culture. An en-US pin cannot prove that: invariant and en-US
///     format identically, so the assertion holds either way and would not notice a regression to
///     invariant. Only the comma output discriminates.
///     </description>
///   </item>
/// </list>
/// <para>
/// <strong>Synchronous tests only.</strong> <see cref="CultureInfo.CurrentCulture"/> is thread-static
/// and does not flow across <c>await</c>, so an <c>async</c> test may resume on a thread that never
/// saw the assignment. Every test carrying this attribute must be synchronous.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
internal sealed class UseCultureAttribute : BeforeAfterTestAttribute
{
    private readonly Lazy<CultureInfo> _culture;
    private readonly Lazy<CultureInfo> _uiCulture;

    private CultureInfo? _originalCulture;
    private CultureInfo? _originalUICulture;

    /// <param name="culture">Culture name, e.g. <c>en-US</c> or <c>nl-NL</c>.</param>
    public UseCultureAttribute(string culture)
        : this(culture, culture)
    {
    }

    /// <param name="culture">Culture name used for formatting and parsing.</param>
    /// <param name="uiCulture">Culture name used for resource lookup.</param>
    public UseCultureAttribute(string culture, string uiCulture)
    {
        _culture = new Lazy<CultureInfo>(() => new CultureInfo(culture, useUserOverride: false));
        _uiCulture = new Lazy<CultureInfo>(() => new CultureInfo(uiCulture, useUserOverride: false));
    }

    public override void Before(MethodInfo methodUnderTest, IXunitTest test)
    {
        _originalCulture = CultureInfo.CurrentCulture;
        _originalUICulture = CultureInfo.CurrentUICulture;

        CultureInfo.CurrentCulture = _culture.Value;
        CultureInfo.CurrentUICulture = _uiCulture.Value;
    }

    public override void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        if (_originalCulture is not null)
            CultureInfo.CurrentCulture = _originalCulture;
        if (_originalUICulture is not null)
            CultureInfo.CurrentUICulture = _originalUICulture;
    }
}
