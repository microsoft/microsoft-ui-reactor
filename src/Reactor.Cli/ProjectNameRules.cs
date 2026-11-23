namespace Microsoft.UI.Reactor.Cli;

// Issue #1664: simple assembly names are case-insensitive, so an app assembly named like an
// assembly Reactor ships shadows it at run time and crashes on startup. The build rejects it
// too (REACTOR_BUILD_001); `mur --create` refuses the name before scaffolding anything.
internal static class ProjectNameRules
{
    internal enum Collision { None, AssemblyName, PackageId }

    // Assemblies the Microsoft.UI.Reactor* packages put on an app's reference path.
    internal static readonly string[] ReservedAssemblyNames =
    [
        "Reactor",
        "Reactor.Advanced",
        "Reactor.Interop.WinForms",
        "Reactor.Wrappers.Abstractions",
        "Microsoft.UI.Reactor.Devtools",
    ];

    // Package ids that aren't also assembly names. A project named after one doesn't crash,
    // but restore treats it as the package itself and fails with a dependency cycle (NU1108).
    internal static readonly string[] ReservedPackageIds =
    [
        "Microsoft.UI.Reactor",
        "Microsoft.UI.Reactor.Advanced",
    ];

    internal static Collision Check(string name) =>
        Array.Exists(ReservedAssemblyNames, n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) ? Collision.AssemblyName
        : Array.Exists(ReservedPackageIds, n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) ? Collision.PackageId
        : Collision.None;
}
