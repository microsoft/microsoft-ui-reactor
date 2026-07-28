using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.UI.Reactor.Hosting.Shell;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Guards the AOT-safe <c>[GeneratedComInterface]</c> taskbar interop. Every
/// <c>ITaskbarList3</c> vtable method returns its HRESULT directly, so each one MUST
/// be <c>[PreserveSig]</c>. Without it the COM source generator treats the <c>int</c>
/// return as a <c>[retval]</c> out-param and translates failed HRESULTs into
/// exceptions — an ABI mismatch that silently breaks every caller that inspects the
/// code (e.g. <c>if (hr &lt; 0)</c>). CI's live-COM selftests can't catch this
/// regression because <c>TaskbarComSingleton.TryGet()</c> returns null on headless
/// runners (no real taskbar COM object), so this headless reflection check is the
/// guard.
/// </summary>
public class TaskbarComInteropTests
{
    [Fact]
    public void ITaskbarList3_AllMethods_ArePreserveSig()
    {
        var methods = typeof(ITaskbarList3).GetMethods(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        Assert.NotEmpty(methods);

        var missing = methods
            .Where(m => !m.GetMethodImplementationFlags().HasFlag(MethodImplAttributes.PreserveSig))
            .Select(m => m.Name)
            .ToArray();

        Assert.True(
            missing.Length == 0,
            "ITaskbarList3 methods missing [PreserveSig] — their int return would be marshalled " +
            "as a [retval] and failed HRESULTs translated to exceptions: " + string.Join(", ", missing));
    }
}
