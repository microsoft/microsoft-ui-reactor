using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using CmdPerf.Shared;
using static Microsoft.UI.Reactor.Factories;

// Parse CLI args before WinUI starts
var cliOptions = CmdCliOptions.Parse(args);
if (cliOptions.Headless)
    ConsoleHelper.EnsureConsole();

CmdPerfApp.Options = cliOptions;
ReactorApp.Run<CmdPerfApp>("CmdPerf.Reactor", width: 1200, height: 800);

// ---------------------------------------------------------------------------

class CmdPerfApp : Component
{
    private const string AppName = "CmdPerf.Reactor";

    public static CmdCliOptions Options { get; set; } = new();

    // Pre-build the command lookup by id for menu/toolbar mapping
    private static readonly Dictionary<string, CommandDef> CmdById =
        CommandSet.All.ToDictionary(c => c.Id);

    public override Element Render()
    {
        var (flags, setFlags) = UseState(EnableFlags.None);
        var (lastCmd, setLastCmd) = UseState("(none)");

        var perfRef = UseRef<CmdPerfTracker?>(null);
        if (perfRef.Current == null)
            perfRef.Current = new CmdPerfTracker();
        var perf = perfRef.Current;

        var autoTimerRef = UseRef<DispatcherTimer?>(null);
        var (autoToggling, setAutoToggling) = UseState(false);

        // Begin mount timing on first render
        var mountStarted = UseRef(false);
        if (!mountStarted.Current)
        {
            mountStarted.Current = true;
            perf.BeginMount();
        }

        // End mount timing after first commit
        UseEffect(() =>
        {
            perf.EndMount();

            if (Options.Headless)
                RunHeadlessScenario(perf, setFlags, flags);
        }, Array.Empty<object>());

        // Build Command records for all 500 commands
        var ductCommands = new Command[CommandSet.All.Length];
        for (int i = 0; i < CommandSet.All.Length; i++)
        {
            var cmd = CommandSet.All[i];
            ductCommands[i] = new Command
            {
                Label = cmd.Label,
                Icon = cmd.IconGlyph is not null ? new SymbolIconData(cmd.IconGlyph) : null,
                CanExecute = CommandSet.IsEnabled(cmd, flags),
                Execute = () => setLastCmd(cmd.Label),
            };
        }

        // Build a lookup from command id to Command
        var ductCmdById = new Dictionary<string, Command>(CommandSet.All.Length);
        for (int i = 0; i < CommandSet.All.Length; i++)
            ductCmdById[CommandSet.All[i].Id] = ductCommands[i];

        // Build MenuBar
        var menuBarDef = MenuLayout.GetMenuBar();
        var menuBarItems = new MenuBarItemData[menuBarDef.Length];
        for (int m = 0; m < menuBarDef.Length; m++)
        {
            var (title, items) = menuBarDef[m];
            menuBarItems[m] = Menu(title, MapMenuItems(items, ductCmdById));
        }

        // Build CommandBar primary commands from toolbar layout
        var toolbarItems = new AppBarItemBase[MenuLayout.ToolbarCommandIds.Length];
        for (int t = 0; t < MenuLayout.ToolbarCommandIds.Length; t++)
        {
            var id = MenuLayout.ToolbarCommandIds[t];
            if (ductCmdById.TryGetValue(id, out var dc))
                toolbarItems[t] = AppBarButton(dc);
            else
                toolbarItems[t] = AppBarButton(id, null);
        }

        // Build flag checkboxes
        var flagCheckboxes = new Element[CommandSet.FlagNames.Length];
        for (int f = 0; f < CommandSet.FlagNames.Length; f++)
        {
            var flagValue = CommandSet.FlagValues[f];
            var flagName = CommandSet.FlagNames[f];
            bool isSet = (flags & flagValue) != 0;
            flagCheckboxes[f] = CheckBox(isSet, toggled =>
            {
                perf.BeginToggle();
                if (toggled)
                    setFlags(flags | flagValue);
                else
                    setFlags(flags & ~flagValue);
                perf.EndToggle(flagName);
            }, label: flagName).Margin(4);
        }

        int enabledCount = CommandSet.CountEnabled(flags);
        double memMB = global::System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / (1024.0 * 1024);

        return VStack(
            MenuBar(menuBarItems),
            CommandBar(primaryCommands: toolbarItems),

            // Control panel
            TextBlock("Enable Flags").Bold().Margin(12, 8, 12, 4),
            HStack(4, flagCheckboxes).Margin(horizontal: 12, vertical: 0),

            // Auto toggle button
            HStack(8,
                Button(autoToggling ? "Stop Auto Toggle" : "Auto Toggle", () =>
                {
                    if (autoToggling)
                    {
                        autoTimerRef.Current?.Stop();
                        autoTimerRef.Current = null;
                        setAutoToggling(false);
                    }
                    else
                    {
                        setAutoToggling(true);
                        int toggleIndex = 0;
                        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                        timer.Tick += (_, _) =>
                        {
                            var flagVal = CommandSet.FlagValues[toggleIndex % CommandSet.FlagValues.Length];
                            var flagName = CommandSet.FlagNames[toggleIndex % CommandSet.FlagNames.Length];
                            perf.BeginToggle();
                            setFlags(flags ^ flagVal);
                            perf.EndToggle(flagName);
                            toggleIndex++;
                        };
                        timer.Start();
                        autoTimerRef.Current = timer;
                    }
                }).Margin(horizontal: 12, vertical: 8)
            ),

            // Status bar
            TextBlock($"Commands: {CommandSet.All.Length} | Enabled: {enabledCount} | " +
                 $"Last Toggle: {perf.LastToggleMs:F2} ms | Mount: {perf.MountTimeMs:F2} ms | " +
                 $"Memory: {memMB:F1} MB")
                .FontSize(12).Margin(horizontal: 12, vertical: 4),

            // Last executed command
            TextBlock($"Last command: {lastCmd}").Margin(horizontal: 12, vertical: 4)
        );
    }

    /// <summary>
    /// Recursively maps MenuLayout items (MenuItem, Separator, SubMenu) to Reactor MenuFlyoutItemBase[].
    /// </summary>
    private static MenuFlyoutItemBase[] MapMenuItems(object[] items, Dictionary<string, Command> cmdLookup)
    {
        var result = new List<MenuFlyoutItemBase>(items.Length);
        foreach (var item in items)
        {
            switch (item)
            {
                case MenuLayout.MenuItem mi:
                    if (cmdLookup.TryGetValue(mi.CommandId, out var dc))
                        result.Add(MenuItem(dc));
                    break;

                case MenuLayout.Separator:
                    result.Add(MenuSeparator());
                    break;

                case MenuLayout.SubMenu sub:
                    result.Add(MenuSubItem(sub.Label, MapMenuItems(sub.Items, cmdLookup)));
                    break;
            }
        }
        return result.ToArray();
    }

    /// <summary>
    /// Runs headless benchmark scenarios after mount, then exits.
    /// </summary>
    private void RunHeadlessScenario(CmdPerfTracker perf, Action<EnableFlags> setFlags, EnableFlags currentFlags)
    {
        var scenario = Options.Scenario;
        int iterations = Options.Iterations;
        int iterationsDone = 0;
        int phaseIndex = 0; // 0=mount, 1=toggle, 2=bulk

        bool doMount = scenario == "mount" || scenario == "all";
        bool doToggle = scenario == "toggle" || scenario == "all";
        bool doBulk = scenario == "bulk" || scenario == "all";

        // Mount time is already recorded. If only mount, report and exit.
        if (doMount && !doToggle && !doBulk)
        {
            perf.RecordMemoryAfterToggles();
            perf.WriteReportFile(AppName, scenario);
            Application.Current.Exit();
            return;
        }

        // Skip to appropriate phase
        if (!doToggle)
            phaseIndex = 2;

        var flags = currentFlags;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1) };
        timer.Tick += (_, _) =>
        {
            if (phaseIndex == 1 && doToggle)
            {
                // Toggle phase: cycle through each of 10 flags
                int flagIdx = iterationsDone % CommandSet.FlagValues.Length;
                var flagVal = CommandSet.FlagValues[flagIdx];
                var flagName = CommandSet.FlagNames[flagIdx];

                perf.BeginToggle();
                flags ^= flagVal;
                setFlags(flags);
                perf.EndToggle(flagName);

                iterationsDone++;
                if (iterationsDone >= iterations * CommandSet.FlagValues.Length)
                {
                    iterationsDone = 0;
                    phaseIndex = 2;
                }
            }
            else if (phaseIndex == 2 && doBulk)
            {
                // Bulk phase: toggle ALL flags at once
                perf.BeginToggle();
                flags = flags == EnableFlags.None
                    ? (EnableFlags)((1 << CommandSet.FlagValues.Length) - 1)
                    : EnableFlags.None;
                setFlags(flags);
                perf.EndBulkToggle();

                iterationsDone++;
                if (iterationsDone >= iterations)
                {
                    timer.Stop();
                    perf.RecordMemoryAfterToggles();
                    perf.WriteReportFile(AppName, scenario);
                    Application.Current.Exit();
                }
            }
            else
            {
                // Phase complete or not needed, advance
                phaseIndex++;
                iterationsDone = 0;
                if (phaseIndex > 2)
                {
                    timer.Stop();
                    perf.RecordMemoryAfterToggles();
                    perf.WriteReportFile(AppName, scenario);
                    Application.Current.Exit();
                }
            }
        };

        // Start at phase 1 (toggle) or phase 2 (bulk) depending on scenario
        phaseIndex = doToggle ? 1 : 2;
        timer.Start();
    }
}
