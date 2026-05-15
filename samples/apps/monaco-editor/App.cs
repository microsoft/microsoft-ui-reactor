// Monaco Editor sample — a simple text editor for testing the MonacoEditor control.
// Supports file open, save, drag-and-drop, and language selection.
//
// This sample doubles as an example of extending Reactor with a custom
// Reactor/WinUI component that leverages WebView2. The pieces:
//
//   Monaco/MonacoEditor.cs          — the WinUI UserControl (WebView2 host)
//   Monaco/monaco-editor.html       — the HTML shell loaded into WebView2
//   Monaco/Assets/                  — vendored Monaco Editor JS/CSS
//   Monaco/MonacoEditorElement.cs   — a Reactor Element + reconciler registration
//                                     so the editor behaves like any built-in
//                                     element (mount/update/unmount lifecycle,
//                                     props flow through the reconciler, no
//                                     manual Panel.Children manipulation).
//
// Program.Main below wires the registration into the host's reconciler.

using MonacoEditorApp;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Core.Theme;
using static MonacoEditorApp.MonacoDsl;
using Path = System.IO.Path;

public static class Program
{
    [STAThread]
    static void Main() => ReactorApp.Run<EditorApp>("Monaco Editor", width: 1200, height: 800,
#if DEBUG
        devtools: true,
#endif
        configure: host =>
    {
        // Spec 033 §6 — backdrop is declared on the root element via
        // .Backdrop(BackdropKind.Mica) below; the host applies it after each
        // reconcile pass. No imperative window-mutation needed here.
        // Register the custom MonacoEditorElement so the reconciler knows how to
        // mount/update/unmount it. Mirrors the pattern used by XamlInterop.Register.
        MonacoEditorRegistration.Register(host.Reconciler);
    });
}

class EditorApp : Component
{
    static readonly (string Label, string Id)[] Languages =
    [
        ("Plain Text", "plaintext"),
        ("C#", "csharp"),
        ("C/C++", "cpp"),
        ("CSS", "css"),
        ("Go", "go"),
        ("HTML", "html"),
        ("Java", "java"),
        ("JavaScript", "javascript"),
        ("JSON", "json"),
        ("Markdown", "markdown"),
        ("PowerShell", "powershell"),
        ("Python", "python"),
        ("Rust", "rust"),
        ("Shell", "shell"),
        ("SQL", "sql"),
        ("TypeScript", "typescript"),
        ("XML", "xml"),
        ("YAML", "yaml"),
    ];

    static readonly Dictionary<string, string> ExtToLang = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"] = "csharp", [".csx"] = "csharp",
        [".c"] = "cpp", [".cpp"] = "cpp", [".cc"] = "cpp", [".h"] = "cpp", [".hpp"] = "cpp",
        [".css"] = "css",
        [".go"] = "go",
        [".html"] = "html", [".htm"] = "html",
        [".java"] = "java",
        [".js"] = "javascript", [".mjs"] = "javascript",
        [".json"] = "json",
        [".md"] = "markdown",
        [".ps1"] = "powershell", [".psm1"] = "powershell",
        [".py"] = "python",
        [".rs"] = "rust",
        [".sh"] = "shell", [".bash"] = "shell",
        [".sql"] = "sql",
        [".ts"] = "typescript", [".tsx"] = "typescript",
        [".xml"] = "xml", [".xaml"] = "xml", [".csproj"] = "xml", [".sln"] = "xml",
        [".yaml"] = "yaml", [".yml"] = "yaml",
    };

    static int LangIndex(string langId) =>
        Math.Max(0, Array.FindIndex(Languages, l => l.Id == langId));

    static string DetectLanguage(string path) =>
        ExtToLang.TryGetValue(Path.GetExtension(path), out var lang) ? lang : "plaintext";

    public override Element Render()
    {
        var (text, setText) = UseState("");
        var (langIndex, setLangIndex) = UseState(0);
        var (theme, setTheme) = UseState("vs-dark");
        var (filePath, setFilePath) = UseState<string?>(null);
        var (isDirty, setIsDirty) = UseState(false);
        var (status, setStatus) = UseState("Ready");
        var editorRef = UseRef<MonacoEditor?>(null);

        var language = Languages[langIndex].Id;

        void OnTextChanged(string newText) { setText(newText); setIsDirty(true); }

        void LoadFile(string path, string content)
        {
            setText(content);
            setFilePath(path);
            setIsDirty(false);
            setLangIndex(LangIndex(DetectLanguage(path)));
            setStatus($"Opened: {path}");
        }

        async void OnOpen()
        {
            var picker = new global::Windows.Storage.Pickers.FileOpenPicker();
            picker.FileTypeFilter.Add("*");
            InitPicker(picker);

            var file = await picker.PickSingleFileAsync();
            if (file is null) return;

            var content = await File.ReadAllTextAsync(file.Path);
            LoadFile(file.Path, content);
        }

        async void OnSave()
        {
            if (filePath is not null)
            {
                await File.WriteAllTextAsync(filePath, text);
                setIsDirty(false);
                setStatus($"Saved: {filePath}");
            }
            else
            {
                OnSaveAs();
            }
        }

        async void OnSaveAs()
        {
            var picker = new global::Windows.Storage.Pickers.FileSavePicker();
            picker.SuggestedStartLocation = global::Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add("All Files", [".txt", ".cs", ".js", ".json", ".xml", ".md"]);
            if (filePath is not null)
                picker.SuggestedFileName = Path.GetFileName(filePath);
            InitPicker(picker);

            var file = await picker.PickSaveFileAsync();
            if (file is null) return;

            await File.WriteAllTextAsync(file.Path, text);
            setFilePath(file.Path);
            setIsDirty(false);
            setLangIndex(LangIndex(DetectLanguage(file.Path)));
            setStatus($"Saved: {file.Path}");
        }

        void OnNew() { setText(""); setFilePath(null); setIsDirty(false); setLangIndex(0); setStatus("New file"); }

        // ── Commands ─────────────────────────────────────────────
        var newCmd = new Command
        {
            Label = "New",
            Execute = OnNew,
            Accelerator = Accelerator(VirtualKey.N, VirtualKeyModifiers.Control),
        };
        var openCmd = StandardCommand.Open((Action)OnOpen);
        var saveCmd = StandardCommand.Save((Action)OnSave);
        var saveAsCmd = new Command
        {
            Label = "Save As...",
            Execute = (Action)OnSaveAs,
            Accelerator = Accelerator(VirtualKey.S, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift),
        };

        var title = filePath is not null
            ? $"{Path.GetFileName(filePath)}{(isDirty ? " *" : "")}"
            : isDirty ? "Untitled *" : "Untitled";

        bool showPreview = language == "markdown";
        GridSize[] columns = showPreview
            ? [GridSize.Star(), GridSize.Auto, GridSize.Star()]
            : [GridSize.Star()];

        var titleBar = (TitleBar("Monaco Editor") with
        {
            Subtitle = title,
            Content = (AutoSuggestBox("") with { PlaceholderText = "Search" })
                .Width(250)
                .Set(asb => asb.TextChanged += (s, _) =>
                    editorRef.Current?.FindText(((Microsoft.UI.Xaml.Controls.AutoSuggestBox)s).Text ?? "")),
        })
            .Grid(row: 0, columnSpan: columns.Length);

        var toolbar = (FlexRow(
            Button(newCmd),
            Button(openCmd),
            Button(saveCmd),
            Button(saveAsCmd),
            TextBlock("").Flex(grow: 1),
            TextBlock("Language:").VAlign(VerticalAlignment.Center),
            ComboBox(
                Languages.Select(l => l.Label).ToArray(),
                langIndex,
                i => setLangIndex(i)
            ).Width(160),
            TextBlock("").Width(8),
            TextBlock("Theme:").VAlign(VerticalAlignment.Center),
            ComboBox(
                ["Light", "Dark", "High Contrast"],
                theme switch { "vs" => 0, "vs-dark" => 1, "hc-black" => 2, _ => 1 },
                i => setTheme(i switch { 0 => "vs", 1 => "vs-dark", 2 => "hc-black", _ => "vs-dark" })
            ).Width(140)
        ) with { AlignItems = FlexAlign.Center, ColumnGap = 6 })
        .Padding(8)
        .Grid(row: 1, columnSpan: columns.Length);

        var statusBar = (FlexRow(
            Caption(title),
            Flex().Flex(grow: 1),
            Caption(status).Foreground(SecondaryText)
        ) with { ColumnGap = 12 })
        .Padding(horizontal: 8, vertical: 4)
        .Grid(row: 3, columnSpan: columns.Length);

        var editor = (MonacoEditor(text, OnTextChanged, language, theme) with
        {
            IsReadOnly = false,
            FontSize = 14,
            WordWrap = false,
            MinimapEnabled = true,
            OnControlMounted = ctrl => editorRef.Current = ctrl,
        })
        .Grid(row: 2, column: 0);

        if (!showPreview)
        {
            return CommandHost([newCmd, openCmd, saveCmd, saveAsCmd],
                Grid(columns, [GridSize.Auto, GridSize.Auto, GridSize.Star(), GridSize.Auto],
                    titleBar, toolbar, editor, statusBar
                )).Backdrop(BackdropKind.Mica);
        }

        var previewPane = ScrollView(
            Markdown(text).Margin(16).Foreground(PrimaryText)
        ).Set(sv => sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled)
         .Background(CardBackground);

        return CommandHost([newCmd, openCmd, saveCmd, saveAsCmd],
            FlexColumn(
                VStack(titleBar).Flex(shrink:0),
                VStack(toolbar).Flex(shrink:0),
                FlexRow(editor.Flex(grow:1, basis:0), previewPane.Flex(grow: 1, basis:0)).Flex(grow:1, basis:0),
                VStack(statusBar).Flex(shrink:0)
            )).Backdrop(BackdropKind.Mica);
    }

    static void InitPicker(object picker)
    {
        var window = ReactorApp.PrimaryWindow?.Host.Window;
        if (window is null) return;
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
    }
}
