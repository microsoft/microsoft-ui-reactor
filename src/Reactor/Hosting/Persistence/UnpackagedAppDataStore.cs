using Microsoft.UI.Reactor.Core.Diagnostics;

namespace Microsoft.UI.Reactor.Hosting.Persistence;

/// <summary>
/// Unpackaged-app persistence store backed by the Windows App SDK's first-class
/// app-data root, <c>Microsoft.Windows.Storage.ApplicationData.GetForUnpackaged()</c>
/// (Windows App SDK ≥ 2.2.0). Persists to
/// <c>&lt;ApplicationData.LocalPath&gt;/reactor-windows.json</c>. (spec 063 §4)
/// </summary>
/// <remarks>
/// <para><b>Opt-in.</b> Auto-detection still selects <see cref="JsonFileStore"/> for
/// unpackaged processes; assign an instance to
/// <see cref="ReactorApp.WindowPersistenceStore"/> before the first <c>OpenWindow</c>
/// to use this one instead. The two stores key their data differently — this one by the
/// <c>publisher</c>/<c>product</c> pair you supply, <see cref="JsonFileStore"/> by the
/// entry process's name — so switching does not migrate an existing app's saved
/// layouts. (spec 063 §6, D2 / D3)</para>
/// <para><b>Why the path surface and not <c>LocalSettings</c>.</b> This store uses
/// <c>ApplicationData.LocalPath</c> and deliberately does <b>not</b> use the sibling
/// <c>LocalSettings</c> key/value surface. On the runtime this repo pins,
/// <c>GetForUnpackaged().LocalSettings</c> opens
/// <c>HKCU\SOFTWARE\&lt;publisher&gt;\&lt;product&gt;</c> — a <b>roaming</b> hive —
/// rather than the machine-local <c>HKCU\SOFTWARE\Classes\Local Settings\Software\…</c>
/// it is contracted to use (WindowsAppSDK issue 6559, RuntimeCompatibilityChange
/// <c>ApplicationData_GetForUnpackaged_LocalSettings</c>). Window placement is
/// monitor-topology- and DPI-dependent, so roaming it between a user's machines
/// restores windows onto monitors that do not exist there. <c>LocalPath</c> resolves
/// under <c>%LOCALAPPDATA%</c>, which does not roam. (spec 063 §3.1)</para>
/// <para>That behaviour is a property of the installed 2.x Windows App Runtime, which
/// services in place, rather than of the SDK version an app pins — so it can differ
/// machine to machine for the same build. <c>LocalPath</c> has no such variance, which
/// is a second reason this store is built on it. (spec 063 §3.2)</para>
/// <para>Read/write failures follow the <see cref="IWindowPersistenceStore"/>
/// "warn-and-default" contract — they never throw into the caller. The
/// <i>constructor</i> may throw, matching <see cref="JsonFileStore"/>: an invalid
/// publisher/product is an authoring error, not a runtime condition.</para>
/// </remarks>
public sealed class UnpackagedAppDataStore : IWindowPersistenceStore
{
    /// <summary>File name written under the resolved app-data root.</summary>
    private const string FileName = "reactor-windows.json";

    // Stable, developer-authored label for the spec 044 Phase B Persistence
    // events. Distinguishes this store from JsonFileStore on the trace even
    // though both write the same JSON document shape. NEVER a path — paths
    // are PII per spec 044 §6.2.1.
    private const string StoreKind = "unpackaged-appdata";

    private readonly JsonFileStore _inner;

    /// <summary>The on-disk file path this store reads and writes.</summary>
    public string Path => _inner.Path;

    /// <summary>
    /// Resolve the Windows App SDK app-data root for
    /// <paramref name="publisher"/>/<paramref name="product"/> and persist beneath it.
    /// </summary>
    /// <param name="publisher">
    /// Publisher segment of the app-data root. The SDK validates this and rejects
    /// path-traversal, rooted, and empty values with an
    /// <see cref="ArgumentException"/>; Reactor does not sanitize it further.
    /// </param>
    /// <param name="product">Product segment of the app-data root.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="publisher"/> or <paramref name="product"/> is empty, or the SDK
    /// rejected it as invalid.
    /// </exception>
    public UnpackagedAppDataStore(string publisher, string product)
    {
        if (string.IsNullOrEmpty(publisher))
            throw new ArgumentException("Publisher must be non-empty.", nameof(publisher));
        if (string.IsNullOrEmpty(product))
            throw new ArgumentException("Product must be non-empty.", nameof(product));

        var appData = global::Microsoft.Windows.Storage.ApplicationData.GetForUnpackaged(publisher, product);
        var root = appData.LocalPath;
        if (string.IsNullOrEmpty(root))
            throw new ArgumentException(
                $"Windows App SDK returned no LocalPath for publisher '{publisher}' / product '{product}'.",
                nameof(publisher));

        // GetForUnpackaged resolves the path but does NOT create the directory
        // (measured on 2.2.0 — spec 063 §3.1). JsonFileStore.Write creates it on
        // first write, so nothing to do here.
        _inner = new JsonFileStore(global::System.IO.Path.Combine(root, FileName), StoreKind);
    }

    /// <inheritdoc />
    public bool TryRead(string id, out byte[]? data) => _inner.TryRead(id, out data);

    /// <inheritdoc />
    public void Write(string id, byte[] data) => _inner.Write(id, data);
}
