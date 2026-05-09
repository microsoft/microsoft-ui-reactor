using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Reactor.Hosting;

namespace Microsoft.UI.Reactor;

/// <summary>
/// Shape of a single jump-list entry. (spec 036 §11.3 / §11.6)
/// </summary>
/// <param name="Title">User-visible label for the entry.</param>
/// <param name="Arguments">
/// Command-line arguments handed to a fresh process invocation when the user
/// activates the entry. <b>Recommended convention:</b> use a deep-link URI
/// string (the same shape <see cref="Microsoft.UI.Reactor.Navigation.DeepLinkMap{TRoute}.Resolve(string)"/>
/// accepts) and resolve via <see cref="LaunchActivation.TryResolve{TRoute}"/>
/// in the startup callback. The <see cref="ForUri(string, string, string?, WindowIcon?, string?)"/>
/// factory makes that convention discoverable.
/// <para><b>Security:</b> these strings round-trip through the shell back
/// into the next process launch. Apps must validate them via
/// <c>Reactor.Cli</c>'s arg parser or
/// <see cref="Microsoft.UI.Reactor.Navigation.DeepLinkMap{TRoute}"/> before
/// acting on them — Reactor itself never auto-executes the arguments.</para>
/// </param>
/// <param name="Kind">Whether this is a regular task, a separator, or part of a custom group.</param>
/// <param name="Description">Optional tooltip / accessible description.</param>
/// <param name="Icon">
/// Optional icon shown next to the title.
/// <para><b>⚠ Packaged-only:</b> the WinRT jump-list path consumes
/// <c>WindowIcon.FromResource</c> values (<c>ms-appx:///...</c> URIs) only.
/// Filesystem paths from <c>WindowIcon.FromPath</c> are silently ignored on
/// the packaged path because the WinRT <c>JumpListItem.Logo</c> requires a
/// packaged <c>Uri</c>. The unpackaged Win32 jump-list path likewise prefers
/// resource-style sources today; ship a sidecar <c>.ico</c> alongside the
/// executable and reference it via <c>FromPath</c> for unpackaged scenarios
/// only after confirming the icon shows up in your build.
/// </para>
/// </param>
/// <param name="GroupCategory">Group label for <see cref="JumpListItemKind.Custom"/> items.</param>
public sealed record JumpListItem(
    string Title,
    string Arguments,
    JumpListItemKind Kind = JumpListItemKind.Task,
    string? Description = null,
    WindowIcon? Icon = null,
    string? GroupCategory = null)
{
    /// <summary>
    /// Convenience factory for the recommended "Arguments == deep-link URI"
    /// convention. Equivalent to <c>new JumpListItem(title, uri, Task, ...)</c>;
    /// makes the navigation bridge discoverable so apps don't need to read the
    /// Arguments doc to know they can hand the activation to a
    /// <see cref="Microsoft.UI.Reactor.Navigation.DeepLinkMap{TRoute}"/>.
    /// (spec 036 §11.3 / §11.6)
    /// </summary>
    public static JumpListItem ForUri(
        string title,
        string uri,
        string? description = null,
        WindowIcon? icon = null,
        string? groupCategory = null)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(uri);
        return new JumpListItem(
            Title: title,
            Arguments: uri,
            Kind: groupCategory is null ? JumpListItemKind.Task : JumpListItemKind.Custom,
            Description: description,
            Icon: icon,
            GroupCategory: groupCategory);
    }

    /// <summary>
    /// Build a jump-list entry whose <see cref="Arguments"/> string is
    /// tokenized: each element of <paramref name="arguments"/> becomes one
    /// argv slot when the launched process parses its command line via
    /// <c>CommandLineToArgvW</c>. Values containing whitespace, quotes, or
    /// backslashes are escaped so a malicious value can't break out into a
    /// neighbouring argument.
    /// <para>Use this factory whenever the arguments include data that did
    /// not originate from a string literal in your source code (deep-link
    /// payloads, file paths, user-typed identifiers). The raw-string
    /// <see cref="JumpListItem(string, string, JumpListItemKind, string?, WindowIcon?, string?)"/>
    /// constructor and <see cref="ForUri(string, string, string?, WindowIcon?, string?)"/>
    /// are still appropriate when the caller has already produced a
    /// well-formed deep-link URI. (spec 036 §11.3 security checklist)</para>
    /// </summary>
    public static JumpListItem ForCommandLine(
        string title,
        IEnumerable<string> arguments,
        string? description = null,
        WindowIcon? icon = null,
        string? groupCategory = null)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(arguments);
        return new JumpListItem(
            Title: title,
            Arguments: EncodeArguments(arguments),
            Kind: groupCategory is null ? JumpListItemKind.Task : JumpListItemKind.Custom,
            Description: description,
            Icon: icon,
            GroupCategory: groupCategory);
    }

    /// <summary>
    /// Encode a sequence of argv-style argument values as a single command-line
    /// string compatible with <c>CommandLineToArgvW</c>. Whitespace, quotes,
    /// and trailing-backslash sequences are quoted/escaped per the documented
    /// MSVC parsing rules so that round-tripping the result through the OS
    /// command-line parser yields back the original sequence.
    /// </summary>
    /// <remarks>
    /// Useful directly for jump-list entries (see <see cref="ForCommandLine"/>),
    /// thumbnail-toolbar buttons, and any other surface where an argument
    /// string is handed to the shell for re-launch. Reactor's own CLI parser
    /// (<c>Reactor.Cli</c>) consumes <c>CommandLineToArgvW</c>-encoded input,
    /// so encoded strings round-trip without further escaping.
    /// </remarks>
    public static string EncodeArguments(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var sb = new global::System.Text.StringBuilder();
        bool first = true;
        foreach (var arg in arguments)
        {
            if (arg is null)
                throw new ArgumentException("Argument values must be non-null.", nameof(arguments));
            if (!first) sb.Append(' ');
            first = false;
            EncodeOneArgument(sb, arg);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Encode a single argv value per <c>CommandLineToArgvW</c> rules.
    /// Public so callers building command lines piecewise can reuse the same
    /// escaper Reactor uses internally for jump-list entries.
    /// </summary>
    public static string EncodeArgument(string argument)
    {
        ArgumentNullException.ThrowIfNull(argument);
        var sb = new global::System.Text.StringBuilder(argument.Length + 2);
        EncodeOneArgument(sb, argument);
        return sb.ToString();
    }

    private static void EncodeOneArgument(global::System.Text.StringBuilder sb, string arg)
    {
        // CommandLineToArgvW parsing rules:
        //  - 2n backslashes followed by a `"` produce n backslashes + start/end quote
        //  - 2n+1 backslashes followed by a `"` produce n backslashes + literal quote
        //  - Backslashes not followed by `"` are literal
        // No special chars → emit as-is (saves a pair of quotes in the common case).
        if (arg.Length > 0 && arg.IndexOfAny(s_argvSpecialChars) < 0)
        {
            sb.Append(arg);
            return;
        }

        sb.Append('"');
        int pendingBackslashes = 0;
        foreach (var c in arg)
        {
            if (c == '\\')
            {
                pendingBackslashes++;
            }
            else if (c == '"')
            {
                // Each backslash before a `"` must be doubled, then the `"` itself escaped.
                sb.Append('\\', pendingBackslashes * 2 + 1);
                sb.Append('"');
                pendingBackslashes = 0;
            }
            else
            {
                if (pendingBackslashes > 0) { sb.Append('\\', pendingBackslashes); pendingBackslashes = 0; }
                sb.Append(c);
            }
        }
        // Trailing backslashes immediately before the closing quote must be doubled.
        if (pendingBackslashes > 0) sb.Append('\\', pendingBackslashes * 2);
        sb.Append('"');
    }

    private static readonly char[] s_argvSpecialChars = { ' ', '\t', '\n', '\v', '"' };
}

/// <summary>
/// Kind of jump-list entry. (spec 036 §11.3)
/// </summary>
public enum JumpListItemKind
{
    /// <summary>A regular task in the standard "Tasks" group.</summary>
    Task,
    /// <summary>A task in a custom <see cref="JumpListItem.GroupCategory"/>.</summary>
    Custom,
    /// <summary>A horizontal separator in the same group as the previous item.</summary>
    Separator,
}

/// <summary>
/// Process-scoped jump list — the right-click flyout that appears on the
/// app's taskbar button or Start tile. Driven through
/// <c>Windows.UI.StartScreen.JumpList</c> for packaged apps and Win32
/// <c>ICustomDestinationList</c> for unpackaged. (spec 036 §11.3)
/// </summary>
/// <remarks>
/// <para><b>Security note:</b> <see cref="JumpListItem.Arguments"/> are
/// command-line strings handed to a freshly launched process by the shell.
/// Apps must parse them through <c>Reactor.Cli</c>'s arg parser before
/// acting on them — a malformed shortcut, a forged jump-list entry left by
/// another process, or a stale entry from an earlier installation will
/// otherwise reach the new process unchecked. Reactor itself never
/// auto-acts on these strings; <see cref="ReactorAppContext.LaunchActivation"/>
/// surfaces them so the app can validate before dispatching.</para>
/// <para>Unpackaged apps must set <see cref="AppUserModelId"/> before the
/// first <see cref="UpdateAsync(IEnumerable{JumpListItem})"/> call — without
/// it the shell has no stable identity to attach the jump list to.</para>
/// </remarks>
public static class JumpList
{
    private static string? _appUserModelId;
    private static int _showRecent;
    private static int _showFrequent;

    /// <summary>
    /// Application User Model ID used by the shell to associate the jump list
    /// with this process. Required for unpackaged apps; ignored when the
    /// process runs under an MSIX package (the package manifest supplies it).
    /// </summary>
    public static string? AppUserModelId
    {
        get => Volatile.Read(ref _appUserModelId);
        set => Volatile.Write(ref _appUserModelId, value);
    }

    /// <summary>
    /// Whether the OS-managed "Recent" category is shown. The list contents
    /// are owned by the shell — apps can only toggle visibility.
    /// </summary>
    public static bool ShowRecent
    {
        get => Volatile.Read(ref _showRecent) != 0;
        set => Volatile.Write(ref _showRecent, value ? 1 : 0);
    }

    /// <summary>
    /// Whether the OS-managed "Frequent" category is shown. The list contents
    /// are owned by the shell — apps can only toggle visibility.
    /// </summary>
    public static bool ShowFrequent
    {
        get => Volatile.Read(ref _showFrequent) != 0;
        set => Volatile.Write(ref _showFrequent, value ? 1 : 0);
    }

    /// <summary>
    /// Replace the jump list's user-defined entries. UI-thread only.
    /// </summary>
    /// <remarks>
    /// Clears any previously-set entries first; the OS-managed Recent and
    /// Frequent categories are unaffected (toggle them via
    /// <see cref="ShowRecent"/> / <see cref="ShowFrequent"/>). Best-effort —
    /// platform failures (downlevel, group-policy lockdown) log to
    /// <c>Debug.WriteLine</c> and do not throw.
    /// </remarks>
    public static async Task UpdateAsync(IEnumerable<JumpListItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        ThreadAffinity.ThrowIfNotOnUIThread(nameof(UpdateAsync));

        var snapshot = items as IReadOnlyList<JumpListItem> ?? items.ToList();

        // Validate before we touch any platform API so a single bad entry
        // doesn't leave a half-populated jump list behind.
        for (int i = 0; i < snapshot.Count; i++)
        {
            var item = snapshot[i] ?? throw new ArgumentException(
                "JumpList entries must be non-null.", nameof(items));
            if (item.Kind != JumpListItemKind.Separator && string.IsNullOrEmpty(item.Title))
                throw new ArgumentException(
                    "JumpListItem.Title must be non-empty for non-separator entries.", nameof(items));
        }

        if (TryUpdatePackaged(snapshot, out var task))
        {
            try { await task!.ConfigureAwait(false); }
            catch (Exception ex) { Debug.WriteLine($"[Reactor] JumpList packaged update failed: {ex.GetType().Name}: {ex.Message}"); }
            return;
        }

        // Unpackaged path — caller-error gates run synchronously (before we
        // hand off to the threadpool) so configuration mistakes surface as
        // exceptions instead of disappearing into Debug.WriteLine. Platform-
        // best-effort failures (missing COM, group policy, downlevel shell)
        // still get swallowed.
        if (string.IsNullOrEmpty(AppUserModelId))
            throw new InvalidOperationException(
                "JumpList.AppUserModelId must be set before UpdateAsync on unpackaged apps. (spec 036 §11.3)");

        try
        {
            await Task.Run(() => UpdateUnpackaged(snapshot)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Reactor] JumpList unpackaged update failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Clear all user-defined jump-list entries. UI-thread only.</summary>
    public static Task ClearAsync()
        => UpdateAsync(global::System.Array.Empty<JumpListItem>());

    private static bool TryUpdatePackaged(IReadOnlyList<JumpListItem> items, out Task? task)
    {
        task = null;
        if (!Hosting.Shell.PackageRuntime.IsPackaged) return false;

        try
        {
            // Resolve via reflection-free WinRT API. Microsoft.WindowsAppSDK exposes
            // Windows.UI.StartScreen.JumpList as an interop type; calling it on an
            // unpackaged app throws — that's why we gate on IsPackaged first.
            task = UpdatePackagedCore(items);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Reactor] JumpList packaged path threw: {ex.GetType().Name}: {ex.Message}");
            task = null;
            return false;
        }
    }

    private static async Task UpdatePackagedCore(IReadOnlyList<JumpListItem> items)
    {
        // The WinRT JumpList class lives in the Windows.UI.StartScreen namespace.
        // Methods on it are async; await on a UI thread completes via the captured
        // SynchronizationContext.
        global::Windows.UI.StartScreen.JumpList list;
        try
        {
            list = await global::Windows.UI.StartScreen.JumpList.LoadCurrentAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Reactor] JumpList.LoadCurrentAsync failed: {ex.GetType().Name}: {ex.Message}");
            return;
        }

        list.Items.Clear();
        list.SystemGroupKind = ResolveSystemGroupKind();

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            try
            {
                if (item.Kind == JumpListItemKind.Separator)
                {
                    list.Items.Add(global::Windows.UI.StartScreen.JumpListItem.CreateSeparator());
                    continue;
                }

                var native = global::Windows.UI.StartScreen.JumpListItem.CreateWithArguments(
                    item.Arguments ?? string.Empty,
                    item.Title);
                if (!string.IsNullOrEmpty(item.Description))
                    native.Description = item.Description;
                if (item.Icon is { IsResource: true, Source: var src } && !string.IsNullOrEmpty(src))
                    native.Logo = new Uri(src);
                if (item.Kind == JumpListItemKind.Custom && !string.IsNullOrEmpty(item.GroupCategory))
                    native.GroupName = item.GroupCategory;
                list.Items.Add(native);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Reactor] JumpList item add failed for '{item.Title}': {ex.GetType().Name}: {ex.Message}");
            }
        }

        try
        {
            await list.SaveAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Reactor] JumpList.SaveAsync failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static global::Windows.UI.StartScreen.JumpListSystemGroupKind ResolveSystemGroupKind()
    {
        var recent = ShowRecent;
        var freq = ShowFrequent;
        if (recent && freq) return global::Windows.UI.StartScreen.JumpListSystemGroupKind.Frequent; // shell only honours one — pick frequent
        if (recent)         return global::Windows.UI.StartScreen.JumpListSystemGroupKind.Recent;
        if (freq)           return global::Windows.UI.StartScreen.JumpListSystemGroupKind.Frequent;
        return global::Windows.UI.StartScreen.JumpListSystemGroupKind.None;
    }

    private static void UpdateUnpackaged(IReadOnlyList<JumpListItem> items)
    {
        var aumid = AppUserModelId;
        if (string.IsNullOrEmpty(aumid))
            throw new InvalidOperationException(
                "JumpList.AppUserModelId must be set before UpdateAsync on unpackaged apps. (spec 036 §11.3)");

        // Defer to the Win32 ICustomDestinationList implementation. Throws
        // are caught by the caller; here we let them bubble so the awaited
        // Task surfaces them via the ConfigureAwait(false) Continue.
        Hosting.Shell.JumpListComInterop.UpdateUnpackaged(aumid!, items, ShowRecent, ShowFrequent);
    }

    /// <summary>Test hook — clear cached state between fixtures.</summary>
    internal static void ResetForTests()
    {
        Volatile.Write(ref _appUserModelId, null);
        Volatile.Write(ref _showRecent, 0);
        Volatile.Write(ref _showFrequent, 0);
    }
}
