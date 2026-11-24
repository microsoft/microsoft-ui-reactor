namespace Microsoft.UI.Reactor.Cli;

// Issue #1664: simple assembly names are case-insensitive, so an app assembly named like an
// assembly Reactor ships shadows it at run time and crashes on startup. The build rejects it
// too (REACTOR_BUILD_001); `mur --create` refuses the name before scaffolding anything.
internal static class ProjectNameRules
{
    internal enum Collision { None, AssemblyName, SolutionProject }

    // Assemblies the Microsoft.UI.Reactor* packages put on an app's reference path. The shipped
    // assemblies are named after their package ids, so a project with one of these names would
    // also fail restore as a dependency cycle (NU1108). The pre-rename short names (Reactor,
    // Reactor.Advanced) are not listed: `mur --create` scaffolds against this repo's own
    // Reactor.csproj, which no longer produces them.
    internal static readonly string[] ReservedAssemblyNames =
    [
        "Microsoft.UI.Reactor",
        "Microsoft.UI.Reactor.Advanced",
        "Microsoft.UI.Reactor.Devtools",
        "Reactor.Interop.WinForms",
        "Reactor.Wrappers.Abstractions",
    ];

    // The .sln `mur --create` writes also lists ..\src\Reactor\Reactor.csproj as project "Reactor",
    // and a solution can't hold two projects with the same name (MSB5004).
    internal const string GeneratedSolutionReactorProject = "Reactor";

    internal static Collision Check(string name) =>
        Array.Exists(ReservedAssemblyNames, n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) ? Collision.AssemblyName
        : string.Equals(name, GeneratedSolutionReactorProject, StringComparison.OrdinalIgnoreCase) ? Collision.SolutionProject
        : Collision.None;
}
