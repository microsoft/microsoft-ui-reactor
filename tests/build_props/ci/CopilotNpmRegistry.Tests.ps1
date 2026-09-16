<#
.SYNOPSIS
    Headless tests for tools/CopilotNpmRegistry.props — the MSBuild-evaluation-time
    selection of the npm mirror GitHub.Copilot.SDK downloads its native CLI from.

.DESCRIPTION
    Evaluates the props file in isolation (a bare non-SDK project in a temp
    directory outside the repo, so no Directory.Build.props is inherited) and
    asserts the resolved $(CopilotNpmRegistryUrl) for each input shape.

    No network access, package restore, or credentials required, and no case
    reads the developer's real npm configuration: each supplies its own
    NPM_CONFIG_USERCONFIG / NPM_CONFIG_REGISTRY, or redirects USERPROFILE to drive
    the default ~/.npmrc fallback.

    This suite is the ONLY coverage this policy gets. Every CI build sets CI=true,
    which makes Directory.Build.props set CopilotSkipCliDownload=true, which makes
    the props file inert — so no ordinary CI job would notice it rotting.
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:Pass = 0
$script:Fail = 0
$script:Failures = New-Object System.Collections.Generic.List[string]

function Assert-Equal {
    param($Expected, $Actual, [string]$Message)
    if ($Expected -eq $Actual) { $script:Pass++ }
    else {
        $script:Fail++
        $script:Failures.Add("$Message`n    expected: [$Expected]`n    actual:   [$Actual]")
    }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$propsPath = Join-Path $repoRoot 'tools\CopilotNpmRegistry.props'
if (-not (Test-Path -LiteralPath $propsPath -PathType Leaf)) {
    throw "Missing props under test: $propsPath"
}

# Deliberately outside the repo: a harness project inside it would inherit the
# repo's own Directory.Build.props and stop measuring this file in isolation.
$tmp = Join-Path ([IO.Path]::GetTempPath()) ("copilot-npm-registry-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null

$harness = Join-Path $tmp 'probe.proj'
Set-Content -LiteralPath $harness -Encoding UTF8 -Value @"
<Project DefaultTargets="Echo">
  <Import Project="$propsPath" />
  <Target Name="Echo">
    <Message Importance="high" Text="RESOLVED=[`$(CopilotNpmRegistryUrl)]" />
    <Message Importance="high" Text="USERCONFIG=[`$(_ReactorNpmUserConfig)]" />
  </Target>
</Project>
"@

# The harness inherits this process's environment, so anything the developer (or
# the CI runner) already exports would leak into every case. Clear the full set
# once, then let each case opt back in.
$scrubbed = @(
    'NPM_CONFIG_REGISTRY'
    'NPM_CONFIG_USERCONFIG'
    'CopilotNpmRegistryUrl'
    'CopilotCliBinaryPath'
    'CopilotSkipCliDownload'
    # CI is scrubbed too: Directory.Build.props derives CopilotSkipCliDownload
    # from it, the runner exports it, and one case below sets it deliberately.
    'CI'
)
$saved = @{}
foreach ($name in $scrubbed) { $saved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }

function Invoke-Probe {
    param(
        [hashtable]$Environment = @{},
        [string[]]$Properties = @()
    )

    $output = Invoke-Msbuild -Environment $Environment -Properties $Properties
    $match = [regex]::Match($output, 'RESOLVED=\[(?<v>[^\]]*)\]')
    if (-not $match.Success) { throw "Probe produced no RESOLVED marker:`n$output" }
    return $match.Groups['v'].Value
}

function Invoke-Msbuild {
    param(
        [hashtable]$Environment = @{},
        [string[]]$Properties = @(),
        [string]$Verbosity = 'm'
    )

    foreach ($name in $scrubbed) { [Environment]::SetEnvironmentVariable($name, $null, 'Process') }

    # Restore anything this case sets. Cases may drive variables outside $scrubbed
    # (USERPROFILE), which must not leak into the next probe.
    $restore = @{}
    foreach ($key in $Environment.Keys) {
        $restore[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
        [Environment]::SetEnvironmentVariable($key, $Environment[$key], 'Process')
    }

    try {
        $output = & dotnet msbuild $harness -nologo "-v:$Verbosity" @Properties 2>&1 | Out-String
        $rc = $LASTEXITCODE
        $global:LASTEXITCODE = 0
    } finally {
        foreach ($key in $restore.Keys) { [Environment]::SetEnvironmentVariable($key, $restore[$key], 'Process') }
    }
    if ($rc -ne 0) { throw "dotnet msbuild exited $rc`n$output" }
    return $output
}

function Invoke-ProbeProperty {
    param(
        [Parameter(Mandatory)][string]$Property,
        [hashtable]$Environment = @{},
        [string[]]$Properties = @()
    )

    $output = Invoke-Msbuild -Environment $Environment -Properties $Properties
    $match = [regex]::Match($output, "$Property=\[(?<v>[^\]]*)\]")
    if (-not $match.Success) { throw "Probe produced no $Property marker:`n$output" }
    return $match.Groups['v'].Value
}

function New-NpmRc {
    param([Parameter(Mandatory)][string]$Name, [Parameter(Mandatory)][string[]]$Lines)
    $dir = Join-Path $tmp $Name
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    $path = Join-Path $dir '.npmrc'
    Set-Content -LiteralPath $path -Value $Lines -Encoding UTF8
    return $path
}

$proxy = 'https://packagefeedproxy.microsoft.io/npm/'
$public = 'https://registry.npmjs.org/'

try {
    # --- The public-contributor path: nothing configured, nothing overridden. ---
    # Points NPM_CONFIG_USERCONFIG at a path that does not exist so the developer's
    # real ~/.npmrc cannot decide this case.
    Assert-Equal '' (Invoke-Probe -Environment @{ NPM_CONFIG_USERCONFIG = (Join-Path $tmp 'absent\.npmrc') }) `
        'no npm configuration leaves the SDK public default untouched'

    $publicOnly = New-NpmRc -Name 'public-only' -Lines @("registry=$public")
    Assert-Equal '' (Invoke-Probe -Environment @{ NPM_CONFIG_USERCONFIG = $publicOnly }) `
        'a public-registry .npmrc is not treated as a mirror'

    # --- The blocked-network path. ---
    $proxyOnly = New-NpmRc -Name 'proxy-only' -Lines @("registry=$proxy")
    Assert-Equal $proxy (Invoke-Probe -Environment @{ NPM_CONFIG_USERCONFIG = $proxyOnly }) `
        'a proxy .npmrc selects the mirror'

    # A mirror line AFTER a public one wins, which a "first registry= line wins,
    # then validate the host" reading would get wrong — the exact shape the
    # bootstrap resolver's own tests pin. The converse ordering is covered
    # immediately below and resolves to no mirror, because npm lets a later line
    # override an earlier one.
    $publicThenProxy = New-NpmRc -Name 'public-then-proxy' -Lines @("registry=$public", "registry=$proxy")
    Assert-Equal $proxy (Invoke-Probe -Environment @{ NPM_CONFIG_USERCONFIG = $publicThenProxy }) `
        'the mirror is found when it follows a public registry line'

    # ...but a LATER public line overrides it, because that is the registry npm
    # itself would use. Selecting the mirror here would keep redirecting a
    # developer who had already appended a public override — and one who has
    # since left that network would get an opaque 401/404 from a Microsoft host.
    $proxyThenPublic = New-NpmRc -Name 'proxy-then-public' -Lines @("registry=$proxy", "registry=$public")
    Assert-Equal '' (Invoke-Probe -Environment @{ NPM_CONFIG_USERCONFIG = $proxyThenPublic }) `
        'a later public registry line overrides an earlier mirror line'

    # A real ~/.npmrc is mostly auth lines and prose comments. Quotes and
    # parentheses in that prose must not derail MSBuild's property-function parse.
    $messy = New-NpmRc -Name 'messy' -Lines @(
        "; Don't share this value with anyone (including support)."
        '//pkgs.dev.azure.com/org/_packaging/feed/npm/registry/:username=VssSessionToken'
        '//pkgs.dev.azure.com/org/_packaging/feed/npm/registry/:_password=Zm9vPT0='
        "@scope:registry=$public"
        "registry=$proxy"
    )
    Assert-Equal $proxy (Invoke-Probe -Environment @{ NPM_CONFIG_USERCONFIG = $messy }) `
        'quotes and parentheses in .npmrc comments do not break evaluation'

    # A commented-out line is not configuration.
    $commented = New-NpmRc -Name 'commented' -Lines @("; registry=$proxy", "# registry=$proxy")
    Assert-Equal '' (Invoke-Probe -Environment @{ NPM_CONFIG_USERCONFIG = $commented }) `
        'a commented-out registry line is ignored'

    # A path with a space is ordinary on Windows (C:\Users\First Last\).
    $spaced = New-NpmRc -Name 'a path with spaces' -Lines @("registry=$proxy")
    Assert-Equal $proxy (Invoke-Probe -Environment @{ NPM_CONFIG_USERCONFIG = $spaced }) `
        'a user-config path containing spaces is still read'

    # npm's ini parser strips surrounding quotes, so these are the same value.
    $dq = New-NpmRc -Name 'double-quoted' -Lines @("registry=`"$proxy`"")
    Assert-Equal $proxy (Invoke-Probe -Environment @{ NPM_CONFIG_USERCONFIG = $dq }) `
        'a double-quoted registry value is accepted'

    $sq = New-NpmRc -Name 'single-quoted' -Lines @("registry='$proxy'")
    Assert-Equal $proxy (Invoke-Probe -Environment @{ NPM_CONFIG_USERCONFIG = $sq }) `
        'a single-quoted registry value is accepted'

    # A scoped registry is not the default registry.
    $scoped = New-NpmRc -Name 'scoped-only' -Lines @("@github:registry=$proxy")
    Assert-Equal '' (Invoke-Probe -Environment @{ NPM_CONFIG_USERCONFIG = $scoped }) `
        'a @scope:registry= line is not treated as the default registry'

    # A registry URL can legitimately carry a token in its query string. Matching
    # it would publish that token into an MSBuild property and every binlog.
    $withQuery = New-NpmRc -Name 'query' -Lines @("registry=$proxy" + '?token=fake-token-4f19')
    Assert-Equal '' (Invoke-Probe -Environment @{ NPM_CONFIG_USERCONFIG = $withQuery }) `
        'a mirror URL carrying a query string is rejected'

    $withFragment = New-NpmRc -Name 'fragment' -Lines @("registry=$proxy" + '#fragment')
    Assert-Equal '' (Invoke-Probe -Environment @{ NPM_CONFIG_USERCONFIG = $withFragment }) `
        'a mirror URL carrying a fragment is rejected'

    # --- Precedence. ---
    Assert-Equal $proxy (Invoke-Probe -Environment @{ NPM_CONFIG_REGISTRY = $proxy; NPM_CONFIG_USERCONFIG = $publicOnly }) `
        'NPM_CONFIG_REGISTRY selects the mirror ahead of the user config'

    Assert-Equal '' (Invoke-Probe -Environment @{ NPM_CONFIG_REGISTRY = $public; NPM_CONFIG_USERCONFIG = $proxyOnly }) `
        'an explicit non-mirror NPM_CONFIG_REGISTRY is not overruled by ~/.npmrc'

    # --- Host allow-list. These are the security-relevant cases. ---
    # Without the '/' separator demanded after '\.io', this spoof matches its own
    # prefix and a native binary gets downloaded from an attacker-controlled host.
    Assert-Equal '' (Invoke-Probe -Environment @{ NPM_CONFIG_REGISTRY = 'https://packagefeedproxy.microsoft.io.evil.example/npm/' }) `
        'a lookalike host that merely starts with the mirror name is rejected'

    Assert-Equal '' (Invoke-Probe -Environment @{ NPM_CONFIG_REGISTRY = 'https://evil.example/packagefeedproxy.microsoft.io/' }) `
        'the mirror name appearing in a path is rejected'

    # Composed from a variable rather than spelled out: a literal basic-auth URL
    # is what credential scrubbers rewrite, and a rewritten input would silently
    # turn this assertion into a test of a garbage string.
    $userInfo = 'user:secret'
    Assert-Equal '' (Invoke-Probe -Environment @{ NPM_CONFIG_REGISTRY = "https://$userInfo@packagefeedproxy.microsoft.io/npm/" }) `
        'a credentialed mirror URL is rejected'

    Assert-Equal '' (Invoke-Probe -Environment @{ NPM_CONFIG_REGISTRY = 'http://packagefeedproxy.microsoft.io/npm/' }) `
        'a plaintext-http mirror URL is rejected'

    Assert-Equal '' (Invoke-Probe -Environment @{ NPM_CONFIG_REGISTRY = 'https://packagefeedproxy.microsoft.io.evil.example' }) `
        'a bare lookalike host is rejected'

    Assert-Equal '' (Invoke-Probe -Environment @{ NPM_CONFIG_REGISTRY = $proxy + '?token=fake-token-4f19' }) `
        'a mirror URL carrying a query string is rejected from the environment'

    Assert-Equal '' (Invoke-Probe -Environment @{ NPM_CONFIG_REGISTRY = $proxy + '#fragment' }) `
        'a mirror URL carrying a fragment is rejected from the environment'

    # The bare-host form is a legitimate registry value the bootstrap resolver
    # accepts. It is only safe because the pattern anchors the end of the value;
    # see the lookalike case above, which shares that mechanism.
    Assert-Equal 'https://packagefeedproxy.microsoft.io' `
        (Invoke-Probe -Environment @{ NPM_CONFIG_REGISTRY = 'https://packagefeedproxy.microsoft.io' }) `
        'the mirror host with no path is accepted'

    # System.Uri normalizes scheme and host before the bootstrap resolver compares
    # them, so a literal case-sensitive match here would reject a URL npm and
    # bootstrap both honor — leaving the developer on the blocked public registry.
    Assert-Equal 'HTTPS://PACKAGEFEEDPROXY.MICROSOFT.IO/npm' `
        (Invoke-Probe -Environment @{ NPM_CONFIG_REGISTRY = 'HTTPS://PACKAGEFEEDPROXY.MICROSOFT.IO/npm' }) `
        'scheme and host are matched case-insensitively'

    Assert-Equal 'https://packagefeedproxy.microsoft.io:443/npm/' `
        (Invoke-Probe -Environment @{ NPM_CONFIG_REGISTRY = 'https://packagefeedproxy.microsoft.io:443/npm/' }) `
        'an explicit port is accepted'

    # Allowing a port must not open a userinfo bypass: in `https://a:443@b/`, the
    # real host is `b`. The end-anchored path group is what rejects it.
    Assert-Equal '' (Invoke-Probe -Environment @{ NPM_CONFIG_REGISTRY = 'https://packagefeedproxy.microsoft.io:443@evil.example/npm/' }) `
        'a port-shaped userinfo prefix does not smuggle in a foreign host'

    Assert-Equal '' (Invoke-Probe -Environment @{ NPM_CONFIG_REGISTRY = 'https://packagefeedproxy.microsoft.io:443.evil.example/npm/' }) `
        'a lookalike host after a port-shaped segment is rejected'

    # Uri.TryCreate rejects a port above 65535, so the bootstrap resolver takes the
    # no-mirror path. An unbounded \d+ here would instead hand the SDK a URL that
    # bootstrap refused.
    Assert-Equal 'https://packagefeedproxy.microsoft.io:65535/npm/' `
        (Invoke-Probe -Environment @{ NPM_CONFIG_REGISTRY = 'https://packagefeedproxy.microsoft.io:65535/npm/' }) `
        'the highest valid port is accepted'

    Assert-Equal '' (Invoke-Probe -Environment @{ NPM_CONFIG_REGISTRY = 'https://packagefeedproxy.microsoft.io:65536/npm/' }) `
        'a port above the valid range is rejected'

    # --- Whitespace is 'unset', matching [string]::IsNullOrWhiteSpace in the resolver. ---
    # Treating an all-whitespace NPM_CONFIG_REGISTRY as set would skip the .npmrc
    # entirely and silently leave the developer on the blocked public registry.
    Assert-Equal $proxy (Invoke-Probe -Environment @{ NPM_CONFIG_REGISTRY = '   '; NPM_CONFIG_USERCONFIG = $proxyOnly }) `
        'an all-whitespace NPM_CONFIG_REGISTRY falls through to the user config'


    # --- Parity cases raised by PR review. ---
    # PowerShell's -match is case-insensitive by default, so the bootstrap resolver
    # honors a Registry= assignment; ignoring it here would leave the developer on
    # the blocked public registry.
    $capitalKey = New-NpmRc -Name 'capital-key' -Lines @("Registry=$proxy")
    Assert-Equal $proxy (Invoke-Probe -Environment @{ NPM_CONFIG_USERCONFIG = $capitalKey }) `
        'a capitalised Registry= key is honored'

    $upperKey = New-NpmRc -Name 'upper-key' -Lines @("REGISTRY=$proxy")
    Assert-Equal $proxy (Invoke-Probe -Environment @{ NPM_CONFIG_USERCONFIG = $upperKey }) `
        'an uppercase REGISTRY= key is honored'

    # Explicit [Aa] classes, not (?i:...): .NET ignore-case folding is culture
    # sensitive and drops this exact value under tr-TR. See the props comment.
    $mixedHost = New-NpmRc -Name 'mixed-case-host' -Lines @('registry=HTTPS://PACKAGEFEEDPROXY.MICROSOFT.IO/npm')
    Assert-Equal 'HTTPS://PACKAGEFEEDPROXY.MICROSOFT.IO/npm' `
        (Invoke-Probe -Environment @{ NPM_CONFIG_USERCONFIG = $mixedHost }) `
        'a mixed-case scheme and host in .npmrc is honored'

    # Turkish dotless/dotted i must never be folded into the ASCII host.
    foreach ($homograph in @(
        'https://packagefeedproxy.microsoft.ıo/npm/'
        'https://packagefeedproxy.mıcrosoft.io/npm/'
        'https://packagefeedproxy.microsoft.İo/npm/'
    )) {
        Assert-Equal '' (Invoke-Probe -Environment @{ NPM_CONFIG_REGISTRY = $homograph }) `
            "a Turkish-homograph host is rejected: $homograph"
    }

    # Bootstrap trims before validating and returns the trimmed value.
    Assert-Equal $proxy (Invoke-Probe -Environment @{ NPM_CONFIG_REGISTRY = "  $proxy  " }) `
        'a whitespace-padded NPM_CONFIG_REGISTRY is trimmed, not rejected'

    # The default user-config branch, driven for real. $(UserProfile) reads the
    # USERPROFILE environment variable -- which is what bootstrap reads -- so unlike
    # SpecialFolder.UserProfile it can be redirected, and this branch can be tested
    # rather than merely asserted about.
    # The redirected value is deliberately DISTINCT from $proxy. A developer running
    # this suite very likely has the mirror in their own ~/.npmrc, so asserting
    # $proxy here would pass whether the fallback honored the redirect or silently
    # read the real profile — the healthy and broken branches would coincide.
    $redirectedUrl = 'https://packagefeedproxy.microsoft.io/npm-redirected/'
    $redirected = Join-Path $tmp 'redirected-profile'
    New-Item -ItemType Directory -Path $redirected -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $redirected '.npmrc') -Value "registry=$redirectedUrl" -Encoding UTF8
    Assert-Equal $redirectedUrl (Invoke-Probe -Environment @{ USERPROFILE = $redirected }) `
        'the default ~/.npmrc fallback reads USERPROFILE, matching bootstrap'

    # A path containing an apostrophe is valid on Windows. MSBuild fixes
    # property-function argument boundaries before expanding $(...), so the
    # apostrophe never participates in parsing -- pinned here because it looks like
    # it should break and has been reported as a defect twice.
    $apostrophe = New-NpmRc -Name "O'Brien dir" -Lines @("registry=$proxy")
    Assert-Equal $proxy (Invoke-Probe -Environment @{ NPM_CONFIG_USERCONFIG = $apostrophe }) `
        'a user-config path containing an apostrophe is still read'

    # MSBuild's Exists() is true for directories too. If it gated the read, a
    # NPM_CONFIG_USERCONFIG pointing at a directory would reach ReadAllText and
    # throw MSB4184, failing EVERY project evaluation in the repo over a stray npm
    # setting. The resolver's Test-Path -PathType Leaf just ignores it.
    $directoryConfig = Join-Path $tmp 'config-is-a-directory'
    New-Item -ItemType Directory -Path $directoryConfig -Force | Out-Null
    Assert-Equal '' (Invoke-Probe -Environment @{ NPM_CONFIG_USERCONFIG = $directoryConfig }) `
        'a user-config path that is a directory is ignored, not an evaluation error'

    # Uri.TryCreate reads :00080 as port 80, so bootstrap selects the mirror.
    Assert-Equal 'https://packagefeedproxy.microsoft.io:00080/npm/' `
        (Invoke-Probe -Environment @{ NPM_CONFIG_REGISTRY = 'https://packagefeedproxy.microsoft.io:00080/npm/' }) `
        'a zero-padded port is accepted'

    Assert-Equal '' (Invoke-Probe -Environment @{ NPM_CONFIG_REGISTRY = 'https://packagefeedproxy.microsoft.io:065536/npm/' }) `
        'a zero-padded port above the valid range is still rejected'

    # .NET \d matches every Unicode decimal digit outside ECMAScript mode, so
    # Arabic-Indic digits satisfied the port grammar while Uri.TryCreate -- and
    # therefore the resolver -- rejects them. The grammar uses [0-9].
    $arabicPort = 'https://packagefeedproxy.microsoft.io:' + [char]0x0661 + [char]0x0662 + [char]0x0663 + '/npm/'
    Assert-Equal '' (Invoke-Probe -Environment @{ NPM_CONFIG_REGISTRY = $arabicPort }) `
        'a port written in non-ASCII digits is rejected'

    # Refutes a review claim: an UNQUOTED value ending in a quote character is
    # accepted by the resolver (it Trim()s quotes unconditionally), and the path
    # group backtracks to leave the trailing quote to the lookahead, so both sides
    # yield the same trimmed value.
    $trailingQuote = New-NpmRc -Name 'trailing-quote' -Lines @("registry=https://packagefeedproxy.microsoft.io/npm/foo'")
    Assert-Equal 'https://packagefeedproxy.microsoft.io/npm/foo' `
        (Invoke-Probe -Environment @{ NPM_CONFIG_USERCONFIG = $trailingQuote }) `
        'an unquoted value ending in a quote character is trimmed, matching the resolver'

    # --- Contract pin on the SDK this props steers. ---
    # Everything above tests OUR parser. None of it would notice GitHub.Copilot.SDK
    # renaming or dropping CopilotNpmRegistryUrl, which is the property the whole
    # file exists to set -- the ordinary CI matrix sets CI=true, making the props
    # inert, so a silent contract break would ship. Reading the SDK's own targets
    # here would mean a package restore and a reachable feed, which would cost this
    # suite its hermeticity, so instead the reviewed version is pinned: a bump fails
    # this assertion and the workflow's path filter guarantees the job runs for it.
    # On failure, re-read build/GitHub.Copilot.SDK.targets in the new package,
    # confirm CopilotNpmRegistryUrl / CopilotSkipCliDownload / CopilotCliBinaryPath
    # still behave as tools/CopilotNpmRegistry.props assumes, then bump the value.
    $reviewedSdkVersion = '1.0.11'
    $packagesProps = Join-Path $repoRoot 'Directory.Packages.props'
    $sdkPinned = [regex]::Match(
        [IO.File]::ReadAllText($packagesProps),
        '<PackageVersion\s+Include="GitHub\.Copilot\.SDK"\s+Version="(?<v>[^"]+)"').Groups['v'].Value
    Assert-Equal $reviewedSdkVersion $sdkPinned `
        'the GitHub.Copilot.SDK version whose CopilotNpmRegistryUrl contract was reviewed is still the pinned one'

    # The resolver only overwrites on a line whose value starts non-whitespace, so
    # a trailing valueless registry= does not clear an earlier mirror.
    $blankOverride = New-NpmRc -Name 'blank-override' -Lines @("registry=$proxy", 'registry=')
    Assert-Equal $proxy (Invoke-Probe -Environment @{ NPM_CONFIG_USERCONFIG = $blankOverride }) `
        'a later valueless registry= line does not override the mirror'

    $wsOverride = New-NpmRc -Name 'whitespace-override' -Lines @("registry=$proxy", 'registry=   ')
    Assert-Equal $proxy (Invoke-Probe -Environment @{ NPM_CONFIG_USERCONFIG = $wsOverride }) `
        'a later whitespace-only registry= line does not override the mirror'

    # The resolver accepts anything Uri.TryCreate does, so an apostrophe in the
    # URL path is legal. The path group allows one inside the value but not at
    # its end, which is what keeps a closing quote distinguishable from path data.
    $apostrophePath = New-NpmRc -Name 'apostrophe-in-url' -Lines @("registry=https://packagefeedproxy.microsoft.io/npm/o'clock/")
    Assert-Equal "https://packagefeedproxy.microsoft.io/npm/o'clock/" `
        (Invoke-Probe -Environment @{ NPM_CONFIG_USERCONFIG = $apostrophePath }) `
        'an apostrophe inside the registry URL path is preserved'

    $bareSlash = New-NpmRc -Name 'bare-slash' -Lines @('registry=https://packagefeedproxy.microsoft.io/')
    Assert-Equal 'https://packagefeedproxy.microsoft.io/' `
        (Invoke-Probe -Environment @{ NPM_CONFIG_USERCONFIG = $bareSlash }) `
        'a bare-slash path is accepted'
    Assert-Equal (Join-Path $redirected '.npmrc') `
        (Invoke-ProbeProperty -Property 'USERCONFIG' `
        -Environment @{ NPM_CONFIG_USERCONFIG = '   '; USERPROFILE = $redirected }) `
        'an all-whitespace NPM_CONFIG_USERCONFIG falls back to the default path'

    # --- The resolved user-config path itself. ---
    # The contents of this branch are driven above by redirecting USERPROFILE;
    # these two pin the path computation on either side of the NPM_CONFIG_USERCONFIG
    # override.
    # Driven through the synthetic profile rather than the real one: leaving
    # USERPROFILE alone would let the production ReadAllText branch consume the
    # developer's own ~/.npmrc, making the result depend on local configuration
    # and feeding real feed credentials through evaluation.
    $expectedDefault = Join-Path $redirected '.npmrc'
    Assert-Equal $expectedDefault (Invoke-ProbeProperty -Property 'USERCONFIG' `
        -Environment @{ USERPROFILE = $redirected }) `
        'with NPM_CONFIG_USERCONFIG unset the user config path falls back to ~/.npmrc'

    Assert-Equal 'C:\redirected\.npmrc' (Invoke-ProbeProperty -Property 'USERCONFIG' `
        -Environment @{ NPM_CONFIG_USERCONFIG = 'C:\redirected\.npmrc' }) `
        'NPM_CONFIG_USERCONFIG overrides the default user config path'

    # --- Opt-outs. ---
    Assert-Equal 'https://contoso.example/npm' `
        (Invoke-Probe -Environment @{ NPM_CONFIG_USERCONFIG = $proxyOnly } -Properties @('-p:CopilotNpmRegistryUrl=https://contoso.example/npm')) `
        'an explicitly-set CopilotNpmRegistryUrl is never overridden'

    Assert-Equal '' (Invoke-Probe -Environment @{ NPM_CONFIG_USERCONFIG = $proxyOnly } -Properties @('-p:CopilotSkipCliDownload=true')) `
        'the props is inert when the CLI download is skipped (the CI configuration)'

    Assert-Equal '' (Invoke-Probe -Environment @{ NPM_CONFIG_USERCONFIG = $proxyOnly } -Properties @('-p:CopilotCliBinaryPath=C:\prefetched\copilot.exe')) `
        'the props is inert when a pre-downloaded CLI binary is supplied'

    # --- The credentials in ~/.npmrc must not reach the build log. ---
    # A real user .npmrc is mostly `//host/:_password=` feed tokens. Binding that
    # text to a named property would publish it in any binlog's property table,
    # in `-pp` output, and — as asserted here — at diagnostic verbosity, which is
    # exactly what a contributor attaches to a build-failure report. Diagnostic
    # verbosity is the broadest name-independent check available: it dumps the
    # environment, the initial properties, and every property reassignment, so it
    # catches a leak regardless of what the property gets called.
    $secret = 'fake-token-b3e1c9d47f2a48d0'
    $credentialed = New-NpmRc -Name 'credentialed' -Lines @(
        '//pkgs.dev.azure.com/org/_packaging/feed/npm/registry/:username=VssSessionToken'
        "//pkgs.dev.azure.com/org/_packaging/feed/npm/registry/:_password=$secret"
        "registry=$proxy"
    )
    $diagnostic = Invoke-Msbuild -Environment @{ NPM_CONFIG_USERCONFIG = $credentialed } -Verbosity 'diag'

    # Positive control first: without proof the probe CAN see this file's contents,
    # "the token is absent" is indistinguishable from a probe that measured nothing.
    # The registry line comes from the same read, so its presence establishes that
    # the file was parsed and that its text reaches this output when it is exposed.
    Assert-Equal $true ($diagnostic -match [regex]::Escape($proxy)) `
        'positive control: the diagnostic log does reflect the .npmrc that was read'

    Assert-Equal $false ($diagnostic -match [regex]::Escape($secret)) `
        'a credential in ~/.npmrc never reaches the build log'

    # --- Integration: the repo actually wires the props in. ---
    # Everything above evaluates the props in isolation, which would keep passing
    # if the Import were deleted from Directory.Build.props. This probe goes
    # through the real file, so it also pins the import and its placement after
    # the CopilotSkipCliDownload/CI block the props depends on.
    $integration = Join-Path $tmp 'integration.proj'
    Set-Content -LiteralPath $integration -Encoding UTF8 -Value @"
<Project DefaultTargets="Echo">
  <Import Project="$(Join-Path $repoRoot 'Directory.Build.props')" />
  <Target Name="Echo">
    <Message Importance="high" Text="RESOLVED=[`$(CopilotNpmRegistryUrl)]" />
  </Target>
</Project>
"@
    $savedHarness = $harness
    try {
        $harness = $integration
        Assert-Equal $proxy (Invoke-Probe -Environment @{ NPM_CONFIG_USERCONFIG = $proxyOnly }) `
            'Directory.Build.props imports the props and resolves the mirror'
        # CI sets CI=true, which Directory.Build.props turns into
        # CopilotSkipCliDownload=true; the mirror must then be left alone.
        Assert-Equal '' (Invoke-Probe -Environment @{ NPM_CONFIG_USERCONFIG = $proxyOnly; CI = 'true' }) `
            'Directory.Build.props leaves the mirror unset under CI=true'
    } finally {
        $harness = $savedHarness
    }

    # --- Differential parity with the PowerShell resolver. ---
    # tools/CopilotNpmRegistry.props claims to implement the same policy as
    # Resolve-ReactorNpmRegistry in a second language. Assert it rather than
    # trusting it: feed both the same .npmrc corpus and require agreement.
    # Bootstrap normalizes with TrimEnd('/'), so compare normalized.
    . (Join-Path $repoRoot 'tools\BootstrapFeedResolver.ps1')
    $corpus = [ordered]@{
        'proxy only'              = @("registry=$proxy")
        'public then proxy'       = @("registry=$public", "registry=$proxy")
        'proxy then public'       = @("registry=$proxy", "registry=$public")
        'public only'             = @("registry=$public")
        'double-quoted'           = @("registry=`"$proxy`"")
        'single-quoted'           = @("registry='$proxy'")
        'query string'            = @("registry=$proxy" + '?token=fake-token-4f19')
        'fragment'                = @("registry=$proxy" + '#fragment')
        'semicolon comment'       = @("; registry=$proxy")
        'hash comment'            = @("# registry=$proxy")
        'scoped only'             = @("@github:registry=$proxy")
        'proxy then scoped'       = @("registry=$proxy", "@github:registry=$public")
        'lookalike host'          = @('registry=https://packagefeedproxy.microsoft.io.evil.example/npm/')
        'mixed-case host'         = @('registry=HTTPS://PACKAGEFEEDPROXY.MICROSOFT.IO/npm')
        'explicit port'           = @('registry=https://packagefeedproxy.microsoft.io:443/npm/')
        'port-shaped userinfo'    = @('registry=https://packagefeedproxy.microsoft.io:443@evil.example/npm/')
        'port above range'        = @('registry=https://packagefeedproxy.microsoft.io:65536/npm/')
        'capitalised key'         = @("Registry=$proxy")
        'uppercase key'           = @("REGISTRY=$proxy")
        'capital key then public' = @("Registry=$proxy", "registry=$public")
        'blank override'          = @("registry=$proxy", 'registry=')
        'whitespace override'     = @("registry=$proxy", 'registry=   ')
        'blank then proxy'        = @('registry=', "registry=$proxy")
        'zero-padded port'        = @('registry=https://packagefeedproxy.microsoft.io:00080/npm/')
        'padded port over range'  = @('registry=https://packagefeedproxy.microsoft.io:065536/npm/')
        'apostrophe in url path'  = @("registry=https://packagefeedproxy.microsoft.io/npm/o'clock/")
        'bare slash path'         = @('registry=https://packagefeedproxy.microsoft.io/')
        'trailing apostrophe'     = @("registry=https://packagefeedproxy.microsoft.io/npm/foo'")
        'trailing double quote'   = @('registry=https://packagefeedproxy.microsoft.io/npm/foo"')
        'highest valid port'      = @('registry=https://packagefeedproxy.microsoft.io:65535/npm/')
        'bare lookalike host'     = @('registry=https://packagefeedproxy.microsoft.io.evil.example')
        'plaintext http'          = @('registry=http://packagefeedproxy.microsoft.io/npm/')
        'indented'                = @("  registry=$proxy")
        'spaces around equals'    = @("registry = $proxy")
        'bare mirror host'        = @('registry=https://packagefeedproxy.microsoft.io')
        'auth lines then proxy'   = @('//pkgs.dev.azure.com/o/_packaging/f/npm/registry/:_password=Zm9v', "registry=$proxy")
    }
    $agreed = 0
    $mirrorSelected = 0
    foreach ($name in $corpus.Keys) {
        $rc = New-NpmRc -Name ('parity-' + [Guid]::NewGuid().ToString('N')) -Lines $corpus[$name]
        # Resolve-ReactorNpmRegistry consults $env:NPM_CONFIG_USERCONFIG ahead of
        # its -UserProfile parameter, so point the environment at this case's file
        # before asking it. Without this the reference answer lags one iteration
        # behind the file under test and the comparison is meaningless.
        [Environment]::SetEnvironmentVariable('NPM_CONFIG_REGISTRY', $null, 'Process')
        [Environment]::SetEnvironmentVariable('NPM_CONFIG_USERCONFIG', $rc, 'Process')
        $resolved = Resolve-ReactorNpmRegistry
        $expected = if ($resolved) { $resolved.Registry } else { '' }
        $actual = (Invoke-Probe -Environment @{ NPM_CONFIG_USERCONFIG = $rc }).TrimEnd('/')
        Assert-Equal $expected $actual "parity with Resolve-ReactorNpmRegistry: $name"
        if ($expected -eq $actual) { $agreed++ }
        if ($expected -ne '') { $mirrorSelected++ }
    }

    # Positive control. Every comparison above would also agree if BOTH sides
    # returned '' for everything — e.g. if the corpus files were never written,
    # or the resolver silently stopped matching. Require that the reference
    # implementation actually selected the mirror on a healthy share of the
    # corpus, so the agreement is known to discriminate.
    Assert-Equal $true ($mirrorSelected -ge 5) `
        "positive control: the parity corpus contains mirror-selecting cases (got $mirrorSelected)"
} finally {
    foreach ($name in $scrubbed) { [Environment]::SetEnvironmentVariable($name, $saved[$name], 'Process') }
    Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host "CopilotNpmRegistry.props: $script:Pass passed, $script:Fail failed"
if ($script:Fail -gt 0) {
    Write-Host ''
    foreach ($failure in $script:Failures) { Write-Host "  [FAIL] $failure" -ForegroundColor Red }
    exit 1
}
exit 0
