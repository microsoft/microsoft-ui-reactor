---
name: cs-build-packaging-review
description: >-
  Review C# MSBuild project files, NuGet dependencies, and analyzer
  configurations for build correctness and packaging safety:
  TargetFramework mismatches, missing nullable annotations, suppressed security
  warnings, floating package versions, known-vulnerable dependencies, diamond
  dependency conflicts, disabled security analyzers, missing PublicAPI tracking
  files, and .editorconfig contradictions.
  19 patterns across 3 sub-domains covering MSBuild-project-configuration,
  NuGet-dependencies, and analyzer-code-quality. Sources include .NET SDK
  documentation, NuGet best practices, Roslyn analyzer guidelines, and
  central package management (Directory.Packages.props) guidance.
  Use this skill when reviewing .csproj files, Directory.Build.props,
  Directory.Packages.props, NuGet package references, .editorconfig files,
  or analyzer suppression attributes in C# projects.
---

# C# Build & Packaging Code Review

## Quick Detection

**Primary Symptoms (in code under review)**:
- `.csproj` with `<TargetFramework>` that doesn't match dependency requirements
- Missing `<Nullable>enable</Nullable>` in new SDK-style projects
- `<NoWarn>` elements suppressing security-relevant warning codes (CS8600-CS8604, CA2100, CA5350-CA5395)
- Floating version specifiers (`*`, `[1.0,)`) in `PackageReference`
- Known-vulnerable NuGet packages (check with `dotnet list package --vulnerable`)
- Security analyzers disabled via `<EnableNETAnalyzers>false</EnableNETAnalyzers>`
- `SuppressMessage` attributes without justification comments
- Duplicate package versions across projects without `Directory.Packages.props`

**Key File Patterns to Search For**:
```xml
<!-- TargetFramework mismatch -->
<TargetFramework>net6.0</TargetFramework>
<!-- But PackageReference requires net8.0+ -->

<!-- Suppressed security warnings -->
<NoWarn>$(NoWarn);CA2100;CA5351</NoWarn>

<!-- Floating version — non-reproducible builds -->
<PackageReference Include="Newtonsoft.Json" Version="13.*" />

<!-- Missing nullable -->
<!-- (absence of <Nullable>enable</Nullable> in new project) -->
```

## Analysis Workflow

### Step 1: Map Project Configuration

Review the project structure and build configuration.

1. Identify all `.csproj`, `Directory.Build.props`, `Directory.Build.targets`, and `Directory.Packages.props` files in the PR.
2. Check the target framework(s) and platform configuration.
3. Build a **Project Configuration Map**:
   | Project | TargetFramework | Nullable | Analyzers | WarningsAsErrors |
   |---------|----------------|----------|-----------|-----------------|
   | `MyApp.csproj` | net8.0-windows | enable | enabled | true |
   | `MyLib.csproj` | netstandard2.0 | disable | disabled | false |

### Step 2: Scan for Pattern Matches

Apply the 19 build and packaging patterns across 3 sub-domains.

**Priority order** (by impact):
1. **NuGet & Dependencies** (6 patterns) — vulnerable packages, diamond dependency conflicts
2. **MSBuild & Project Configuration** (8 patterns) — framework mismatches, suppressed warnings
3. **Analyzer & Code Quality** (5 patterns) — disabled security analysis, missing API tracking

**Key detection queries per category**:

| Sub-domain | What to Look For | Risk |
|-----------|-----------------|------|
| NuGet Vulnerabilities | `dotnet list package --vulnerable`, known CVEs | High |
| Version Pinning | `Version="*"`, `Version="[1.0,)"` in PackageReference | Medium |
| Framework Mismatch | TargetFramework vs dependency minimum framework | High |
| Warning Suppression | `<NoWarn>` with CA/CS codes, `<TreatWarningsAsErrors>false` | Medium/High |
| Analyzer Config | `<EnableNETAnalyzers>false`, disabled rulesets | High |

### Step 3: Classify Findings

For each potential match:

1. **Confirm the defect**: Is the configuration actually problematic?
   - Is the suppressed warning justified and documented?
   - Does the floating version have a `packages.lock.json` to pin it at restore time?
   - Is the TargetFramework mismatch causing actual build failures or just a compatibility risk?
2. **Severity**:
   - **High**: Known-vulnerable dependency, suppressed security warning without justification, framework mismatch causing runtime failure
   - **Medium**: Non-reproducible builds (floating versions), missing nullable annotations, missing analyzers
   - **Low**: Style/convention issues (duplicate versions, missing PublicAPI tracking)

### Step 4: Generate Fix

**Fix Strategy Decision Tree**:

```
What kind of build/packaging defect?
├── NuGet Vulnerability
│   ├── Direct dependency → Update to patched version
│   ├── Transitive dependency → Add explicit PackageReference to override
│   └── No patch available → Document risk, consider alternative package
├── Project Configuration
│   ├── TargetFramework mismatch → Align with dependency requirements
│   ├── Missing Nullable → Add <Nullable>enable</Nullable>
│   ├── Missing TreatWarningsAsErrors → Add to Directory.Build.props
│   └── Suppressed warnings → Remove suppression or add justification
├── Analyzer Configuration
│   ├── Security analyzer disabled → Re-enable with <EnableNETAnalyzers>true
│   ├── .editorconfig contradiction → Align rules with project analyzers
│   └── Missing PublicAPI tracking → Add PublicAPI.Shipped/Unshipped.txt
└── Dependency Management
    ├── Floating versions → Pin to specific version
    ├── Diamond conflict → Use Directory.Packages.props for central management
    └── Missing PrivateAssets → Add for build-only/development packages
```

**Fix template**:
```markdown
#### Finding: [Pattern-ID] — [Brief description]
**File**: `path/to/file.csproj` lines N-M
**Severity**: [High|Medium|Low]
**Pattern**: [Pattern ID and name]

**Before** (problematic):
```xml
<!-- Problematic project configuration -->
```

**After** (correct):
```xml
<!-- Fixed project configuration -->
```

**Verification**:
- [ ] `dotnet build` succeeds with no new warnings
- [ ] `dotnet list package --vulnerable` shows no known vulnerabilities
- [ ] Analyzer rules enforced (no suppressed security warnings without justification)
- [ ] CI pipeline passes with updated configuration
```

### Step 5: Verify Fix

1. **Build verification**: `dotnet build` with no new warnings or errors
2. **Vulnerability scan**: `dotnet list package --vulnerable` and `dotnet list package --deprecated`
3. **Restore verification**: `dotnet restore` resolves all packages without conflicts
4. **Analyzer verification**: Run `dotnet build /p:EnforceCodeStyleInBuild=true` to verify analyzer rules

---

## Pattern Catalog

### MSBuild & Project Configuration

#### BUILD-PROJ-01: TargetFramework mismatch with dependency requirements
**Severity**: High

A project targets a framework version that is lower than what its dependencies require. This causes runtime `MissingMethodException`, `TypeLoadException`, or `FileNotFoundException` even when the build appears to succeed.

```xml
<!-- BAD: Project targets net6.0 but dependency requires net8.0+ -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net6.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <!-- This package's latest version requires net8.0 -->
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="8.0.0" />
  </ItemGroup>
</Project>
```

```xml
<!-- GOOD: TargetFramework aligned with dependency requirements -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="8.0.0" />
  </ItemGroup>
</Project>

<!-- GOOD (alternative): Use older package version compatible with target framework -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net6.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="6.0.1" />
  </ItemGroup>
</Project>
```

---

#### BUILD-PROJ-02: Nullable not enabled in new project
**Severity**: Medium

New SDK-style projects should enable nullable reference types to catch null-safety issues at compile time. Omitting `<Nullable>enable</Nullable>` means the compiler does not warn about potential null dereferences.

```xml
<!-- BAD: No nullable context — null safety warnings suppressed -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <!-- Missing: <Nullable>enable</Nullable> -->
  </PropertyGroup>
</Project>
```

```xml
<!-- GOOD: Nullable enabled — compiler catches null-safety issues -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>

<!-- GOOD (repo-wide): Set in Directory.Build.props -->
<!-- Directory.Build.props -->
<Project>
  <PropertyGroup>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
```

---

#### BUILD-PROJ-03: Missing TreatWarningsAsErrors
**Severity**: Medium

Without `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, warnings accumulate over time and important issues (nullable violations, obsolete API usage, security warnings) are ignored until they cause runtime failures.

```xml
<!-- BAD: Warnings don't fail the build — they accumulate and are ignored -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <!-- 47 warnings in build output — nobody reads them -->
  </PropertyGroup>
</Project>
```

```xml
<!-- GOOD: Warnings fail the build — forces immediate resolution -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>

<!-- GOOD (repo-wide): Set in Directory.Build.props -->
<!-- Directory.Build.props -->
<Project>
  <PropertyGroup>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

---

#### BUILD-PROJ-04: NoWarn suppressing security-relevant warnings
**Severity**: High

`<NoWarn>` elements that suppress security-related analyzer warnings (CA2100 SQL injection, CA5350-CA5395 cryptography, CS8600-CS8604 nullable) hide real vulnerabilities.

```xml
<!-- BAD: Suppressing security-critical warnings project-wide -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <NoWarn>$(NoWarn);CA2100;CA5351;CA5359;CS8602</NoWarn>
    <!-- CA2100: SQL injection review
         CA5351: Broken crypto (DES/RC2)
         CA5359: Disabled cert validation
         CS8602: Possible null dereference -->
  </PropertyGroup>
</Project>
```

```xml
<!-- GOOD: No project-wide suppression of security warnings -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <!-- Only suppress non-security warnings with documented justification -->
    <NoWarn>$(NoWarn);CS1591</NoWarn> <!-- CS1591: Missing XML doc — acceptable for internal projects -->
  </PropertyGroup>
</Project>
```

```csharp
// If a specific instance must be suppressed, do it locally with justification:
[SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities",
    Justification = "Query uses parameterized inputs validated by SqlParameterBuilder")]
public DataTable ExecuteReport(string reportId)
{
    // ...
}
```

---

#### BUILD-PROJ-05: Platform target mismatch (AnyCPU library consumed by x64-only app)
**Severity**: High

An AnyCPU library that loads native x64 DLLs will crash on ARM64. Conversely, an x64-only library referenced by an AnyCPU application causes `BadImageFormatException` when the app runs as x86.

```xml
<!-- BAD: AnyCPU library with native x64 dependency — crashes on ARM64 -->
<!-- MyNativeWrapper.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <PlatformTarget>AnyCPU</PlatformTarget>  <!-- But loads native x64 DLLs -->
  </PropertyGroup>
</Project>
```

```xml
<!-- GOOD: Platform target matches native dependency architecture -->
<!-- MyNativeWrapper.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <PlatformTarget>x64</PlatformTarget>  <!-- Explicit: matches native DLLs -->
  </PropertyGroup>
</Project>

<!-- GOOD (multi-platform): Use RuntimeIdentifier for platform-specific builds -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <RuntimeIdentifiers>win-x64;win-arm64</RuntimeIdentifiers>
  </PropertyGroup>
  <ItemGroup Condition="'$(RuntimeIdentifier)' == 'win-x64'">
    <NativeLibrary Include="native\x64\mylib.dll" />
  </ItemGroup>
  <ItemGroup Condition="'$(RuntimeIdentifier)' == 'win-arm64'">
    <NativeLibrary Include="native\arm64\mylib.dll" />
  </ItemGroup>
</Project>
```

---

#### BUILD-PROJ-06: Missing EnableNETAnalyzers
**Severity**: Medium

.NET analyzers (the CA rules from Microsoft.CodeAnalysis.NetAnalyzers) are included in the .NET SDK but can be disabled. Projects that don't have them enabled miss important correctness, performance, and security warnings.

```xml
<!-- BAD: Analyzers explicitly disabled — misses important warnings -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <EnableNETAnalyzers>false</EnableNETAnalyzers>
  </PropertyGroup>
</Project>
```

```xml
<!-- GOOD: Analyzers enabled (default for net5.0+, but explicit is clearer) -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <EnableNETAnalyzers>true</EnableNETAnalyzers>
    <AnalysisLevel>latest-recommended</AnalysisLevel>  <!-- Use latest rule set -->
  </PropertyGroup>
</Project>
```

---

#### BUILD-PROJ-07: InternalsVisibleTo without strong-name constraint
**Severity**: Medium

`InternalsVisibleTo` without specifying the public key of the friend assembly allows any assembly with the matching name to access internals. For signed assemblies, this weakens encapsulation because anyone can create an assembly with the same name.

```csharp
// BAD: InternalsVisibleTo without PublicKey — any assembly named "MyApp.Tests" gets access
[assembly: InternalsVisibleTo("MyApp.Tests")]
```

```csharp
// GOOD: InternalsVisibleTo with PublicKey constraint (for strong-named assemblies)
[assembly: InternalsVisibleTo("MyApp.Tests, PublicKey=002400000480000094000000060200000024000052534131000400000100010007d1fa57c4aed9f0a32e84aa0faefd0de9e8fd6aec8f87fb03766c834c99921eb23be79ad9d5dcc1dd9ad236132102900b723cf980957fc4e177108fc607774f29e8320e92ea05ece4e821c0a5efe8f1645c4c0c93c1ab99285d622caa652c1dfad63d745d6f2de5f17e5eaf0fc4963d261c8a12436518206dc093344d5ad293")]

// GOOD (alternative for non-signed assemblies): Document the intent
// If assemblies are not strong-named, add a comment explaining the exposure:
[assembly: InternalsVisibleTo("MyApp.Tests")]  // Test project only — unsigned, internal use
```

---

#### BUILD-PROJ-08: Duplicate PackageReference versions across projects
**Severity**: Low — use Directory.Packages.props

When multiple projects in a solution reference the same package at different versions, NuGet resolves to the highest version, which may introduce breaking changes. Central Package Management via `Directory.Packages.props` prevents version drift.

```xml
<!-- BAD: Different versions of same package across projects -->
<!-- ProjectA.csproj -->
<PackageReference Include="Newtonsoft.Json" Version="13.0.1" />

<!-- ProjectB.csproj -->
<PackageReference Include="Newtonsoft.Json" Version="13.0.3" />

<!-- ProjectC.csproj -->
<PackageReference Include="Newtonsoft.Json" Version="12.0.3" />  <!-- Very old! -->
```

```xml
<!-- GOOD: Central Package Management with Directory.Packages.props -->
<!-- Directory.Packages.props (at solution root) -->
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="Newtonsoft.Json" Version="13.0.3" />
  </ItemGroup>
</Project>

<!-- ProjectA.csproj — version centrally managed -->
<PackageReference Include="Newtonsoft.Json" />  <!-- No Version attribute needed -->

<!-- ProjectB.csproj -->
<PackageReference Include="Newtonsoft.Json" />  <!-- Same version as ProjectA -->
```

---

### NuGet & Dependencies

#### BUILD-PKG-01: Direct dependency with known vulnerability
**Severity**: High

A `PackageReference` to a NuGet package with a known security vulnerability (CVE) exposes the application. Use `dotnet list package --vulnerable` to check.

```xml
<!-- BAD: Known vulnerable version of System.Text.Json -->
<PackageReference Include="System.Text.Json" Version="6.0.0" />
<!-- CVE-2024-30105: Denial of Service vulnerability in System.Text.Json < 6.0.10 -->
```

```xml
<!-- GOOD: Updated to patched version -->
<PackageReference Include="System.Text.Json" Version="8.0.5" />
```

```bash
# Detection: Run vulnerability check
dotnet list package --vulnerable --include-transitive
```

---

#### BUILD-PKG-02: Floating version in PackageReference
**Severity**: Medium — build non-reproducibility

Floating versions (`*`, `[1.0,)`, `1.*`) mean the same source code can produce different binaries depending on when/where `dotnet restore` runs. This breaks reproducible builds and makes debugging production issues harder.

```xml
<!-- BAD: Floating versions — build output depends on NuGet feed state at restore time -->
<ItemGroup>
  <PackageReference Include="Serilog" Version="3.*" />
  <PackageReference Include="AutoMapper" Version="[12.0,)" />
  <PackageReference Include="FluentValidation" Version="*" />
</ItemGroup>
```

```xml
<!-- GOOD: Pinned versions — reproducible builds -->
<ItemGroup>
  <PackageReference Include="Serilog" Version="3.1.1" />
  <PackageReference Include="AutoMapper" Version="12.0.1" />
  <PackageReference Include="FluentValidation" Version="11.9.0" />
</ItemGroup>

<!-- GOOD (additional): Use packages.lock.json for full reproducibility -->
<!-- In .csproj or Directory.Build.props: -->
<PropertyGroup>
  <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
</PropertyGroup>
```

---

#### BUILD-PKG-03: Transitive dependency pinned to vulnerable version
**Severity**: Medium

Even when direct dependencies are up to date, a transitive (indirect) dependency may resolve to a vulnerable version. This is harder to detect because it doesn't appear in `.csproj`.

```xml
<!-- BAD: Direct package is safe, but it pulls in a vulnerable transitive dependency -->
<PackageReference Include="SomePackage" Version="2.0.0" />
<!-- SomePackage 2.0.0 depends on System.Net.Http 4.3.0 which has CVE-2018-8292 -->
```

```xml
<!-- GOOD: Explicitly pin the transitive dependency to a safe version -->
<ItemGroup>
  <PackageReference Include="SomePackage" Version="2.0.0" />
  <!-- Override transitive dependency to patched version -->
  <PackageReference Include="System.Net.Http" Version="4.3.4" />
</ItemGroup>
```

```bash
# Detection: Check transitive vulnerabilities
dotnet list package --vulnerable --include-transitive
```

---

#### BUILD-PKG-04: Package downgrade causing diamond dependency conflict
**Severity**: High

When two packages depend on different versions of the same transitive dependency, NuGet uses the "nearest wins" rule. This can silently downgrade a dependency, causing `MissingMethodException` or `TypeLoadException` at runtime.

```xml
<!-- BAD: Diamond dependency — ProjectA gets version 4.3.0 even though PackageX needs 4.5.0 -->
<ItemGroup>
  <PackageReference Include="PackageX" Version="2.0.0" />  <!-- Depends on Newtonsoft.Json >= 13.0.3 -->
  <PackageReference Include="PackageY" Version="1.0.0" />  <!-- Depends on Newtonsoft.Json >= 12.0.3 -->
  <PackageReference Include="Newtonsoft.Json" Version="12.0.3" />  <!-- Explicit pin downgrades! -->
</ItemGroup>
<!-- NU1605 warning: Detected package downgrade: Newtonsoft.Json from 13.0.3 to 12.0.3 -->
```

```xml
<!-- GOOD: Pin to the highest required version -->
<ItemGroup>
  <PackageReference Include="PackageX" Version="2.0.0" />
  <PackageReference Include="PackageY" Version="1.0.0" />
  <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />  <!-- Satisfies both -->
</ItemGroup>

<!-- GOOD (additional): Treat NU1605 as error to catch downgrades -->
<PropertyGroup>
  <WarningsAsErrors>$(WarningsAsErrors);NU1605</WarningsAsErrors>
</PropertyGroup>
```

---

#### BUILD-PKG-05: Missing PrivateAssets on build-only packages
**Severity**: Medium

Packages used only at build time (analyzers, source generators, build tasks) should be marked with `<PrivateAssets>all</PrivateAssets>` to prevent them from being included as runtime dependencies in the output or in downstream packages.

```xml
<!-- BAD: Analyzer package flows to consumers as a runtime dependency -->
<PackageReference Include="Microsoft.CodeAnalysis.NetAnalyzers" Version="8.0.0" />
<!-- Consumers of this library will unnecessarily get the analyzer package -->
```

```xml
<!-- GOOD: PrivateAssets prevents analyzer from flowing to consumers -->
<PackageReference Include="Microsoft.CodeAnalysis.NetAnalyzers" Version="8.0.0">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
</PackageReference>

<!-- Common build-only packages that need PrivateAssets: -->
<PackageReference Include="Microsoft.SourceLink.GitHub" Version="8.0.0" PrivateAssets="all" />
<PackageReference Include="MinVer" Version="5.0.0" PrivateAssets="all" />
<PackageReference Include="StyleCop.Analyzers" Version="1.2.0-beta.556" PrivateAssets="all" />
```

---

#### BUILD-PKG-06: Development dependency not marked as such
**Severity**: Medium

Packages that are only needed during development (test helpers, benchmarking tools, code generation) should be excluded from published output to reduce deployment size and attack surface.

```xml
<!-- BAD: Development packages included in published output -->
<ItemGroup>
  <PackageReference Include="BenchmarkDotNet" Version="0.13.12" />
  <PackageReference Include="Bogus" Version="35.4.0" />  <!-- Fake data generator -->
  <!-- These will be included in dotnet publish output -->
</ItemGroup>
```

```xml
<!-- GOOD: Mark development dependencies appropriately -->
<ItemGroup>
  <!-- For the main project, these should only be in test/benchmark projects -->
  <!-- If they must be in the main project, exclude from publish: -->
  <PackageReference Include="BenchmarkDotNet" Version="0.13.12">
    <PrivateAssets>all</PrivateAssets>
    <ExcludeAssets>runtime</ExcludeAssets>
  </PackageReference>
</ItemGroup>

<!-- BETTER: Move to dedicated test/benchmark project -->
<!-- MyApp.Benchmarks.csproj -->
<ItemGroup>
  <PackageReference Include="BenchmarkDotNet" Version="0.13.12" />
  <ProjectReference Include="..\MyApp\MyApp.csproj" />
</ItemGroup>
```

---

### Analyzer & Code Quality

#### BUILD-ANAL-01: Security analyzer disabled or excluded
**Severity**: High

Disabling the .NET security analyzers (CA2xxx, CA5xxx rules) or excluding them from the build means security-relevant code patterns (SQL injection, insecure cryptography, SSRF) go undetected.

```xml
<!-- BAD: Security analyzers completely disabled -->
<PropertyGroup>
  <EnableNETAnalyzers>false</EnableNETAnalyzers>
</PropertyGroup>

<!-- BAD: Security category excluded from analysis -->
<PropertyGroup>
  <AnalysisMode>None</AnalysisMode>
</PropertyGroup>
```

```xml
<!-- GOOD: Security analyzers enabled with recommended rule set -->
<PropertyGroup>
  <EnableNETAnalyzers>true</EnableNETAnalyzers>
  <AnalysisLevel>latest-recommended</AnalysisLevel>
  <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
</PropertyGroup>
```

```ini
# GOOD: .editorconfig with security rules as errors
[*.cs]
dotnet_diagnostic.CA2100.severity = error  # SQL injection
dotnet_diagnostic.CA2300.severity = error  # Insecure deserialization
dotnet_diagnostic.CA5350.severity = error  # Weak crypto
dotnet_diagnostic.CA5351.severity = error  # Broken crypto
dotnet_diagnostic.CA5359.severity = error  # Disabled cert validation
dotnet_diagnostic.CA5394.severity = warning  # Insecure randomness
```

---

#### BUILD-ANAL-02: .editorconfig rules contradicting project analyzers
**Severity**: Medium

When `.editorconfig` sets a rule to `none` or `suggestion` but the project's `AnalysisLevel` includes it as `warning` or `error`, the `.editorconfig` takes precedence and silently disables the rule.

```ini
# BAD: .editorconfig silently overrides project analyzer severity
[*.cs]
dotnet_diagnostic.CA1062.severity = none       # Validates public method arguments — disabled!
dotnet_diagnostic.CA2007.severity = none       # ConfigureAwait — disabled!
dotnet_diagnostic.CA1822.severity = suggestion  # Mark members as static — won't fail build
```

```ini
# GOOD: .editorconfig aligns with project intent
[*.cs]
dotnet_diagnostic.CA1062.severity = warning   # Validate public method arguments
dotnet_diagnostic.CA2007.severity = warning   # ConfigureAwait in library code
dotnet_diagnostic.CA1822.severity = warning   # Mark members as static
```

```xml
<!-- Ensure .editorconfig rules are enforced in build -->
<PropertyGroup>
  <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
</PropertyGroup>
```

---

#### BUILD-ANAL-03: SuppressMessage without justification comment
**Severity**: Medium

`[SuppressMessage]` attributes without a `Justification` make it impossible to determine whether the suppression is legitimate or was added to silence an inconvenient warning. Over time, unjustified suppressions accumulate and mask real issues.

```csharp
// BAD: No justification — why is this suppressed?
[SuppressMessage("Microsoft.Security", "CA2100")]
public void ExecuteQuery(string sql) { /* ... */ }

[SuppressMessage("Microsoft.Design", "CA1062")]
public void ProcessInput(string input) { /* ... */ }
```

```csharp
// GOOD: Justification documents the reasoning
[SuppressMessage("Microsoft.Security", "CA2100:Review SQL queries for security vulnerabilities",
    Justification = "SQL is built from compile-time constants only; no user input concatenated")]
public void ExecuteQuery(string sql) { /* ... */ }

[SuppressMessage("Microsoft.Design", "CA1062:Validate arguments of public methods",
    Justification = "Called only from internal code that already validates; guard clause would be redundant")]
public void ProcessInput(string input) { /* ... */ }
```

---

#### BUILD-ANAL-04: Custom analyzer throwing at build time
**Severity**: High — disrupts CI

A custom Roslyn analyzer that throws an unhandled exception during analysis causes the build to fail with an opaque error, often `AD0001`. This blocks CI pipelines and is difficult to diagnose.

```csharp
// BAD: Custom analyzer with unhandled exception path
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class MyCustomAnalyzer : DiagnosticAnalyzer
{
    public override void Initialize(AnalysisContext context)
    {
        context.RegisterSyntaxNodeAction(AnalyzeNode, SyntaxKind.InvocationExpression);
    }

    private void AnalyzeNode(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        // BUG: GetSymbolInfo can return null — NullReferenceException crashes analyzer
        var symbol = context.SemanticModel.GetSymbolInfo(invocation).Symbol;
        var name = symbol.Name;  // Throws if symbol is null → AD0001 build error
    }
}
```

```csharp
// GOOD: Defensive analyzer code with null checks and exception handling
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class MyCustomAnalyzer : DiagnosticAnalyzer
{
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeNode, SyntaxKind.InvocationExpression);
    }

    private void AnalyzeNode(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);

        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
            return;  // Safe: gracefully handle missing symbol

        // Proceed with analysis using methodSymbol
    }
}
```

---

#### BUILD-ANAL-05: Missing PublicAPI.Shipped.txt / PublicAPI.Unshipped.txt for public library
**Severity**: Medium

Libraries consumed by external teams should track their public API surface using the `Microsoft.CodeAnalysis.PublicApiAnalyzers` package. Without the `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt` tracking files, accidental public API changes go undetected.

```xml
<!-- BAD: Public library without API surface tracking -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsPackable>true</IsPackable>
    <!-- No public API tracking — breaking changes undetected -->
  </PropertyGroup>
</Project>
```

```xml
<!-- GOOD: Public API tracking enabled -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsPackable>true</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.PublicApiAnalyzers" Version="3.3.4">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
  </ItemGroup>
</Project>
```

```
# PublicAPI.Shipped.txt — API surface that has been released
MyNamespace.MyClass
MyNamespace.MyClass.MyClass() -> void
MyNamespace.MyClass.Process(string input) -> bool

# PublicAPI.Unshipped.txt — new API surface not yet released
MyNamespace.MyClass.ProcessAsync(string input, System.Threading.CancellationToken ct) -> System.Threading.Tasks.Task<bool>
```

---

## Build & Packaging Checklist

Use this checklist when reviewing C# project files and build configuration.

### Project Configuration
- [ ] `<TargetFramework>` matches dependency minimum requirements
- [ ] `<Nullable>enable</Nullable>` is set for new projects
- [ ] `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` is set
- [ ] `<NoWarn>` does not suppress security-relevant warnings (CA2xxx, CA5xxx)
- [ ] `<PlatformTarget>` matches native dependency architecture
- [ ] `<EnableNETAnalyzers>true</EnableNETAnalyzers>` is set
- [ ] `<AnalysisLevel>latest-recommended</AnalysisLevel>` is set

### NuGet Dependencies
- [ ] No packages with known vulnerabilities (`dotnet list package --vulnerable`)
- [ ] No floating version specifiers (`*`, `[x,)`) — pin to specific versions
- [ ] No transitive dependency downgrades (NU1605 treated as error)
- [ ] Build-only packages marked with `<PrivateAssets>all</PrivateAssets>`
- [ ] Development packages not included in published output
- [ ] Central package management via `Directory.Packages.props` for multi-project solutions

### Analyzer & Code Quality
- [ ] .NET analyzers enabled, not disabled or excluded
- [ ] `.editorconfig` rules align with project analyzer configuration
- [ ] `[SuppressMessage]` attributes include `Justification` parameter
- [ ] Custom analyzers handle null/missing symbols gracefully (no `AD0001` crashes)
- [ ] Public libraries track API surface with `PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt`

## References

1. .NET SDK project file reference — Microsoft Learn: "MSBuild reference for .NET SDK projects"
2. NuGet Central Package Management — Microsoft Learn: "Central Package Management"
3. .NET code analysis — Microsoft Learn: "Code analysis in .NET"
4. Roslyn Analyzers — GitHub: dotnet/roslyn-analyzers
5. `dotnet list package` documentation — Microsoft Learn: "dotnet list package"
6. PublicApiAnalyzers — GitHub: dotnet/roslyn-analyzers (PublicApiAnalyzers)
