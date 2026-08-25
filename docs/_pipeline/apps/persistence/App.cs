using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

// Doc app for `persistence.md` — tiny notes editor showing the
// in-memory UsePersisted patterns and the disk-backed UseEffect bridge.
ReactorApp.Run<PersistenceApp>("Persistence Demo", width: 440, height: 360
);

class PersistenceApp : Component
{
    public override Element Render() => Component<NotesEditor>();
}

// <snippet:basic-hook>
// UsePersisted with explicit Window scope. The text outlives a re-mount
// of the component (e.g. navigation away and back) but is dropped when
// the host window closes. Replace PersistedScope.Window with
// PersistedScope.Application for process-lifetime persistence.
class NotesEditor : Component
{
    public override Element Render()
    {
        var (text, setText) = UsePersisted(
            "notes/body",
            initialValue: "",
            scope: PersistedScope.Window);

        return VStack(8,
            SubHeading("Notes"),
            TextBox(text, setText, placeholderText: "Start typing…").Width(380),
            TextBlock($"{text.Length} characters").Opacity(0.6)
        ).Padding(16);
    }
}
// </snippet:basic-hook>

// <snippet:versioned-shape>
// Versioned persisted shape. When the field set changes, bump the
// version and migrate forward. The reader matches on the stored shape
// and never trusts the cache to hold a current schema.
record NotesStateV1(string Body, DateTimeOffset LastEdit);
record NotesStateV2(string Body, DateTimeOffset LastEdit, string Title);

class VersionedNotesEditor : Component
{
    public override Element Render()
    {
        var (state, setState) = UsePersisted(
            "notes/state-v2",
            initialValue: new NotesStateV2("", DateTimeOffset.Now, ""),
            scope: PersistedScope.Application);

        return VStack(8,
            TextBox(state.Title, t => setState(state with { Title = t }),
                placeholderText: "Title"),
            TextBox(state.Body, b => setState(state with { Body = b, LastEdit = DateTimeOffset.Now }),
                placeholderText: "Body")
        );
    }

    // One-shot migration from the v1 key to the v2 key. Run once at app
    // startup; thereafter the v1 key is empty and never consulted again.
    public static void MigrateOnce(IPersistedStateScope scope)
    {
        if (scope.TryGet<NotesStateV1>("notes/state-v1", out var v1))
        {
            scope.Set("notes/state-v2",
                new NotesStateV2(v1.Body, v1.LastEdit, Title: ""));
            scope.Remove("notes/state-v1");
        }
    }
}
// </snippet:versioned-shape>

// <snippet:disk-bridge>
// Disk-backed bridge. UsePersisted alone is in-memory only — to outlive
// a process restart, mirror to disk in UseEffect. The state hook holds
// the live value; the effect writes it whenever it changes.
class PersistentSettings : Component
{
    private static readonly string SettingsPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MyApp", "settings.json");

    public override Element Render()
    {
        // Seed from disk once via UseMemo; UsePersisted holds the live value
        // across re-mounts; the effect mirrors writes to disk.
        var initial = UseMemo(LoadFromDisk, Array.Empty<object>());
        var (settings, setSettings) = UsePersisted(
            "settings", initial, PersistedScope.Application);

        UseEffect(() =>
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, AppSettingsJsonContext.Default.AppSettings));
            return () => { };
        }, settings);

        return ToggleSwitch(settings.NotificationsOn,
            on => setSettings(settings with { NotificationsOn = on }),
            header: "Notifications");
    }

    private static AppSettings LoadFromDisk() =>
        File.Exists(SettingsPath)
            ? JsonSerializer.Deserialize(File.ReadAllText(SettingsPath), AppSettingsJsonContext.Default.AppSettings)
                ?? new AppSettings(NotificationsOn: true)
            : new AppSettings(NotificationsOn: true);
}

record AppSettings(bool NotificationsOn);

// JSON source-generated context so the disk read/write is trim- and
// NativeAOT-safe (no reflection-based JsonSerializer overloads).
[JsonSerializable(typeof(AppSettings))]
partial class AppSettingsJsonContext : JsonSerializerContext;
// </snippet:disk-bridge>

// ────────────────────────────────────────────────────────────────────
//  Explicit scope selection.
// ────────────────────────────────────────────────────────────────────

class ScopedStateDemo : Component
{
    public override Element Render()
    {
        // <snippet:scopes>
        // Survives a tab swap; dropped on window close.
        var (filter, setFilter) = UsePersisted(
            "list/filter", "", PersistedScope.Window);

        // Survives navigation across windows in this process.
        var (token, setToken) = UsePersisted(
            "auth/token", "", PersistedScope.Application);
        // </snippet:scopes>

        return VStack(8,
            TextBox(filter, setFilter, placeholderText: "Filter"),
            TextBlock(token.Length == 0 ? "(signed out)" : "(signed in)"));
    }
}
