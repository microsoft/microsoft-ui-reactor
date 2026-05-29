#nullable enable

namespace Microsoft.UI.Reactor.Cli.Find;

internal static class Notes
{
    public static string[]? GetNotes(string? notesKey)
    {
        if (notesKey is null) return null;
        return _notes.GetValueOrDefault(notesKey);
    }

    private static readonly Dictionary<string, string[]> _notes = new()
    {
        ["Button"] =
        [
            "Button(label, onClick) is the basic factory. For icon buttons, use Button(Icon(symbol), onClick).",
            "Use .AccentButton() for primary actions, .SubtleButton() for toolbar/chrome, .TextLink() for hyperlink-style.",
            "For command-pattern buttons, use Button(command) where Command has Label, Execute, and CanExecute."
        ],
        ["CheckBox"] =
        [
            "CheckBox(isChecked, onIsCheckedChanged, label) is the basic checkbox. Two-way binding pattern.",
            "For three-state (checked/unchecked/indeterminate), use ThreeStateCheckBox with bool? state.",
            "Label is optional — if omitted, place the CheckBox next to a TextBlock in an HStack."
        ],
        ["ComboBox"] =
        [
            "ComboBox(items, selectedIndex, onSelectedIndexChanged) for string arrays. ComboBox(elements, selectedIndex, onChange) for custom elements.",
            "selectedIndex is 0-based. -1 means no selection. The onChange callback receives the new index.",
            "For searchable/filterable dropdowns, use AutoSuggestBox instead."
        ],
        ["ContentDialog"] =
        [
            "ContentDialog is non-routed — show it via `UseDialog().Show(...)` from a hook, not by mounting it as a child element.",
            "Primary/secondary/close buttons map to the three result branches. For yes/no/cancel, provide all three texts."
        ],
        ["DataGrid"] =
        [
            "DataGrid<T> takes an IDataSource<T>, not a raw list. Wrap with IDataSource.From(items) for in-memory data; use a custom IDataSource for virtualized fetch.",
            "Column<T>(...) is the column builder. The accessor returns the cell value; format with the format parameter, don't synthesize strings.",
            "For sortable/filterable in-memory data, pass IDataSource.From(items) and the source handles sort/filter internally."
        ],
        ["FlexColumn"] =
        [
            "FlexColumn is a CSS flexbox column container. Children flow top-to-bottom with flex properties.",
            "Use .Flex(grow: 1) on a child to fill remaining vertical space. Common for page layouts.",
            "Combine with .Backdrop(BackdropKind.Mica) on the root FlexColumn for Win11 window chrome."
        ],
        ["FlexRow"] =
        [
            "FlexRow is a CSS flexbox row container. Children flow left-to-right with flex properties (grow, shrink, basis, alignSelf).",
            "Use .Flex(grow: 1) on a child to fill remaining horizontal space. .Flex(shrink: 0) prevents a child from shrinking.",
            "FlexRow defaults to FlexWrap.NoWrap. Set .FlexWrap(FlexWrap.Wrap) for wrapping content."
        ],
        ["ForEach"] =
        [
            "ForEach<T>(items, render) maps a collection to elements. Always add .WithKey(uniqueId) on the outer element of each item.",
            "ForEach is not virtualized — it renders all items immediately. For large lists (100+ items), use ListView<T> or LazyVStack<T>.",
            "The render function receives (item) or (item, index). Prefer the item overload; index-based keys break on reorder."
        ],
        ["FormField"] =
        [
            "FormField wraps an input with label + required marker + error display. Use with UseValidationContext for validation.",
            "The showWhen parameter controls error visibility: ShowWhen.WhenTouched (default) hides errors until the user interacts; ShowWhen.Always shows immediately.",
            "FormField is a layout wrapper, not a validator. Attach .Validate() on the input element inside the FormField."
        ],
        ["Grid"] =
        [
            "Grid(columns, rows, children) creates a WinUI Grid. Use GridSize helpers: Auto, Star(n), Pixel(n).",
            "Place children with .Grid(column, row) or .Grid(column, row, columnSpan, rowSpan).",
            "For simple stacks, prefer VStack/HStack. Grid is for 2D layouts with explicit column/row sizing."
        ],
        ["HStack"] =
        [
            "HStack arranges children horizontally with optional spacing. HStack(8, child1, child2) adds 8px between children.",
            "Children are laid out left-to-right. Use .Flex(grow: 1) on a child to make it fill remaining space.",
            "HStack shrink-wraps. Combine with VStack for simple layouts; use FlexRow for complex flex scenarios."
        ],
        ["Image"] =
        [
            "Image(source) takes a URI string (ms-appx:///Assets/..., https://..., or file path).",
            "Use .Width(n).Height(n) to constrain size. Without constraints, Image expands to natural size.",
            "For icon-sized images, prefer FontIcon or SymbolIcon for better scaling and theming."
        ],
        ["lists"] =
        [
            "Lists produced by `items.Select(...).ToArray()` MUST include `.WithKey(item.Id)` on every element. Without keys, focus, animation, and child state drift across reorders.",
            "`UseState<List<T>>` mutating in place does not re-render. Use `UseReducer<TState, TAction>` or `UseCollection`."
        ],
        ["NavigationView"] =
        [
            "NavigationView provides sidebar/hamburger navigation. Wire with .WithNavigation(nav, toTag, toRoute) for typed routing.",
            "NavItem tag must be a string. Map to your Route enum via toTag/toRoute functions.",
            "NavigationHost renders the matched page. Child pages access the nav handle via UseNavigation<TRoute>()."
        ],
        ["ScrollView"] =
        [
            "ScrollView(child) is the modern scrolling container (WinUI ScrollView). Use for scrollable content regions.",
            "Don't wrap a ListView or ItemsRepeater in ScrollView — they have built-in scrolling. Double-scrolling breaks virtualization.",
            "For legacy ScrollViewer compatibility, use ScrollViewer(child) — but prefer ScrollView for new code."
        ],
        ["TextField"] =
        [
            "TextField(value, onChanged, placeholder) is the text input factory. Two-way binding: pass state value and setter.",
            "Use .EmailInput(), .NumericInput(), .PhoneInput() for input scope hints. .MaxLength(n) caps input.",
            ".Validate(fieldName, value, ...validators) attaches form validation. Requires UseValidationContext ancestor."
        ],
        ["Theme"] =
        [
            "Use Theme.* tokens (Theme.PrimaryText, Theme.CardBackground). Hardcoded colors trip REACTOR_THEME_001.",
            "`.Resources(r => r.Set(\"ButtonBackground\", …))` applies lightweight styling without a global Theme override.",
            "Theme tokens automatically re-resolve on light/dark/high-contrast switches. Hardcoded values don't."
        ],
        ["UseCallback"] =
        [
            "UseCallback memoizes a delegate so child components receiving it don't re-render when the parent renders. Wrap event handlers passed as props.",
            "Deps array determines when the callback is recreated. Capture only the values you need — stale closures over state are the #1 bug.",
            "If the callback needs current state, consider UseReducer + dispatch (which is stable) instead of UseCallback with state deps."
        ],
        ["UseContext"] =
        [
            "UseContext<T> reads the nearest ancestor Provider<T>'s value. If no provider exists, it throws — always wrap with a provider at or above the consumer.",
            "Context re-renders all consumers when the value changes. For fine-grained updates, split into multiple contexts or use selectors.",
            "The provider value should be memoized (UseMemo) if it's an object/record — otherwise every parent render creates a new reference and all consumers re-render."
        ],
        ["UseEffect"] =
        [
            "Effects run AFTER render commits. Don't read state set inside the same render unless via UseEffect's cleanup or a deps change.",
            "Return a cleanup lambda when the effect subscribes to anything. The cleanup runs before the next effect AND on unmount.",
            "Empty deps `[]` means 'run once on mount' — but the effect still re-runs if the component remounts due to key change."
        ],
        ["UseMemo"] =
        [
            "UseMemo caches a computed value and only recalculates when deps change. Use for expensive derivations, not for simple field access.",
            "Deps are compared by value (record equality or reference equality for objects). A freshly-allocated array/list in deps defeats memoization.",
            "UseMemo runs during render — don't put side effects in the factory. Use UseEffect for side effects."
        ],
        ["UseReducer"] =
        [
            "UseReducer is the recommended hook for list/collection state. UseState<List<T>> won't re-render on .Add()/.Remove() because the reference is unchanged.",
            "The reducer function must be pure — same (state, action) always produces the same next-state. Side effects belong in UseEffect, not the reducer.",
            "Dispatch is stable across renders — safe to pass to child components without wrapping in UseCallback."
        ],
        ["UseRef"] =
        [
            "UseRef<T> returns a mutable container that persists across renders without triggering re-render on assignment.",
            "For DOM element refs, use UseRef<FrameworkElement>() and attach via .Ref(myRef). The ref is populated after mount, not during Render().",
            "Don't read UseRef during render to make decisions — the value is from the previous render. Use UseState if the value should trigger re-render."
        ],
        ["UseResource"] =
        [
            "UseResource re-runs the fetcher when deps change, on retry, and on focus revalidation. Use UseMutation for writes (POST/PUT/DELETE).",
            "Match on AsyncValue<T> to render loading / error / data — don't unwrap by checking null.",
            "Deps must be scalar values or memoized references. A freshly-allocated array in deps causes infinite re-fetch."
        ],
        ["UseState"] =
        [
            "UseState with a List<T> does NOT re-render on `.Add()` / `.Remove()` — same reference. Use UseReducer for collections.",
            "UseState returns (value, setter). The setter is stable across renders — safe to omit from dependency arrays.",
            "Call UseState unconditionally at the top of Render. Hooks track slot identity by call order."
        ],
        ["UseValidationContext"] =
        [
            "UseValidationContext owns per-field validation state. Inputs attach via .Validate(fieldName, currentValue, ...validators).",
            "Call ctx.MarkTouched(fieldName) in the input's onChange handler to trigger error display for WhenTouched mode.",
            "ctx.IsValid() returns true only when ALL registered fields pass. Use for submit button gating."
        ],
        ["VStack"] =
        [
            "VStack arranges children vertically with optional spacing. VStack(12, child1, child2) adds 12px between children.",
            "VStack shrink-wraps to content. For a VStack that fills available space, wrap in a Flex container or use .Flex(grow: 1).",
            "For horizontal layout, use HStack. For CSS flexbox-style layout, use FlexColumn/FlexRow."
        ],
        ["WithKey"] =
        [
            "Required on every element produced from `.Select(...)` inside a layout container. Without it the analyzer emits `REACTOR_DSL_001` and reordering breaks focus/animation.",
            "Key must be stable across renders. Don't key by index for reorderable lists — that defeats the purpose."
        ]
    };
}
