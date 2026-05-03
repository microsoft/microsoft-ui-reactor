# Spectre.Console chat demo

## Demo Prompt

This demo shows how to build a real-time text chat UI using .NET 10
top-level statements and the Spectre.Console library. Single-file mode —
each step is a self-contained `step-NN.cs` runnable via `dotnet run`. Use
file-level `#:package` directives to pull in NuGet dependencies. Audience
is intermediate .NET developers; keep each step focused on a single
concept.

## Steps

1. **Hello World baseline**
   Start with a minimal top-level statements app that prints a styled
   greeting using Spectre.Console. This establishes the file structure
   and confirms the NuGet reference works.

2. **Add a message list**
   Render a static list of three fake chat messages in a Spectre.Console
   `Table` with two columns: sender (color-coded) and message body.

3. **Simulate live updates**
   Add a loop using `AnsiConsole.Live(table)` that appends a new message
   every second with a randomised sender. The app exits when the user
   presses Esc.

4. **Add an input prompt**
   Below the live table, add an input prompt using
   `AnsiConsole.Ask<string>` so the user can send their own message,
   which is appended to the table. Show how to interleave `Live` updates
   with prompts.
