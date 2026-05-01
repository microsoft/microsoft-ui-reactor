# Support

Reactor is an experimental project under active development. The maintainers triage on a best-effort basis and there is no support SLA.

## How to file what

| You want to… | Where it goes |
|---|---|
| Report a bug in Reactor itself | [Open an issue](https://github.com/microsoft/microsoft-ui-reactor/issues/new/choose) using the **Bug report** template |
| Propose a new feature or design change | [Open an issue](https://github.com/microsoft/microsoft-ui-reactor/issues/new/choose) using the **Feature request** template |
| Ask "how do I do X with Reactor?" | [Start a discussion](https://github.com/microsoft/microsoft-ui-reactor/discussions) |
| Report a security vulnerability | **Do not file an issue.** See [SECURITY.md](SECURITY.md) |
| Report a bug in WinUI 3 itself | [microsoft/microsoft-ui-xaml](https://github.com/microsoft/microsoft-ui-xaml/issues) |
| Report a bug in the Windows App SDK | [microsoft/WindowsAppSDK](https://github.com/microsoft/WindowsAppSDK/issues) |

## Is this Reactor or WinUI?

A useful first check: does the repro require Reactor types (`Component`, `Element`, `ReactorApp`, hooks, the C# DSL)? If you can reproduce the same behavior in a vanilla WinUI 3 app with the same control, it's a WinUI issue, not a Reactor one — file it upstream.

If you're not sure, file it here and we'll route it.

## Triage expectations

- New issues are triaged in batches; expect days, not hours, for an initial response.
- Issues marked `needs-repro` will be closed if no repro is provided within 30 days.
- "Experimental" means the API surface may change in response to design feedback; bugs against pre-1.0 surfaces are still welcome but won't always block other work.

## Microsoft Support Policy

Support for this project is limited to the resources listed above.
