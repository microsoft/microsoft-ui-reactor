using System.Diagnostics;

namespace Microsoft.UI.Reactor.Hosting.Persistence;

/// <summary>
/// Default packaged-app persistence store. Routes through
/// <c>ApplicationData.Current.LocalSettings</c> using a "Reactor" container,
/// matching <c>WinUIEx.WindowManager</c>'s key shape so existing apps that
/// migrate keep their saved layouts. (spec 036 §8)
/// </summary>
/// <remarks>
/// <para>Constructing this store from an unpackaged process throws on first
/// access (the WinRT API requires package identity). Callers must check
/// <c>Windows.ApplicationModel.Package.Current</c> before instantiating, or
/// fall back to <see cref="JsonFileStore"/>.</para>
/// <para>All read failures (missing key, type mismatch) return <c>false</c>;
/// write failures are swallowed with a stderr diagnostic.</para>
/// </remarks>
public sealed class PackagedSettingsStore : IWindowPersistenceStore
{
    private const string ContainerName = "Reactor";
    private const string KeyPrefix = "WindowPersistence_";

    /// <inheritdoc />
    public bool TryRead(string id, out byte[]? data)
    {
        data = null;
        if (string.IsNullOrEmpty(id)) return false;
        try
        {
            var container = global::Windows.Storage.ApplicationData.Current?.LocalSettings;
            if (container is null) return false;
            if (!container.Containers.TryGetValue(ContainerName, out var c)) return false;
            if (!c.Values.TryGetValue(KeyPrefix + id, out var value)) return false;
            if (value is not string b64 || string.IsNullOrEmpty(b64)) return false;
            data = Convert.FromBase64String(b64);
            return data is not null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Reactor] PackagedSettingsStore.TryRead failed: {ex.GetType().Name}: {ex.Message}");
            data = null;
            return false;
        }
    }

    /// <inheritdoc />
    public void Write(string id, byte[] data)
    {
        if (string.IsNullOrEmpty(id) || data is null) return;
        try
        {
            var settings = global::Windows.Storage.ApplicationData.Current?.LocalSettings;
            if (settings is null) return;
            var c = settings.CreateContainer(ContainerName, global::Windows.Storage.ApplicationDataCreateDisposition.Always);
            c.Values[KeyPrefix + id] = Convert.ToBase64String(data);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Reactor] PackagedSettingsStore.Write failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Detect whether the current process has WinRT package identity.
    /// Constructing <see cref="PackagedSettingsStore"/> in an unpackaged
    /// context still works at construction time; this static check lets
    /// auto-detection logic pick the right default.
    /// </summary>
    public static bool IsAvailable()
    {
        try
        {
            // Touching Package.Current throws InvalidOperationException
            // (HRESULT 0x80073D54) under unpackaged contexts.
            _ = global::Windows.ApplicationModel.Package.Current;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
