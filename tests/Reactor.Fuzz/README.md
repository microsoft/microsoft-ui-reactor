# Reactor.Fuzz

SharpFuzz-driven managed fuzz harnesses for Reactor's two attacker-influenced
text parsers. Tracks SDL work item **62373203** (Applications Security: Apply
Fuzzers to Your Code).

## Targets

| TargetName              | Entry point                                                   | Corpus            |
| ----------------------- | ------------------------------------------------------------- | ----------------- |
| `Reactor.Fuzz.Markdown` | `MarkdownHtml.Render` (drives `Md4cParser`)                   | `corpus/markdown` |
| `Reactor.Fuzz.PathData` | `PathDataParser.ParseTokens` (WinUI-free parse loop)          | `corpus/pathdata` |

`PathDataParser.ParseTokens` shares its number / whitespace / command-dispatch
loop with the production `Parse`; the only difference is that geometry / segment
construction is gated on a non-null `PathGeometry`. Production `Parse` cannot
run in a console process because `PathGeometry` requires XAML activation, so
the fuzz harness exercises the equivalent token walker.

## Build

```pwsh
dotnet build tests\Reactor.Fuzz\Reactor.Fuzz.csproj -c Release -p:Platform=x64
```

## Local smoke

`SharpFuzz.Fuzzer.OutOfProcess` is libFuzzer-driven, so a local smoke needs the
`sharpfuzz` instrumentation tool plus `libfuzzer-dotnet` from the SharpFuzz
release artifacts. See https://github.com/Metalnem/sharpfuzz for the typical
flow:

```pwsh
sharpfuzz <publish>\Reactor.dll
libfuzzer-dotnet --target_path=<publish>\Reactor.Fuzz.exe --target_arg=markdown corpus\markdown
```

## OneFuzz onboarding

The `OneFuzzConfig.json` in this directory wires both targets to SDL work item
`62373203`. Onboarding flow lives at https://aka.ms/onboardasan; the managed
SharpFuzz path applies (the native ASAN build branch flow does not — Reactor
is 100% managed C#). Once a job is registered, paste its OneFuzz Job ID into
ADO custom string 04 of the SDL work item; the Liquid claim auto-links via the
`SdlTaskId` field above.
