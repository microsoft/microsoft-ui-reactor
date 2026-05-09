using Microsoft.UI.Reactor.Hosting.Persistence;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Spec 036 §8 / §0.5 — <see cref="JsonFileStore"/> round-trip, corruption
/// handling, and 1 MB cap. Pure-IO tests over a temp directory; no XAML
/// Application context required.
/// </summary>
public class JsonFileStoreTests : IDisposable
{
    private readonly string _path;

    public JsonFileStoreTests()
    {
        _path = global::System.IO.Path.Combine(
            global::System.IO.Path.GetTempPath(),
            $"reactor-windows-test-{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        try { if (global::System.IO.File.Exists(_path)) global::System.IO.File.Delete(_path); } catch { }
    }

    [Fact]
    public void Write_Then_Read_RoundTrips_Bytes()
    {
        var store = new JsonFileStore(_path);
        var data = new byte[] { 1, 2, 3, 4, 5, 250, 251, 252 };
        store.Write("main", data);

        Assert.True(store.TryRead("main", out var read));
        Assert.NotNull(read);
        Assert.Equal(data, read);
    }

    [Fact]
    public void Multiple_Ids_Coexist_In_One_File()
    {
        var store = new JsonFileStore(_path);
        store.Write("main", new byte[] { 1, 2, 3 });
        store.Write("settings", new byte[] { 4, 5, 6 });

        Assert.True(store.TryRead("main", out var a));
        Assert.True(store.TryRead("settings", out var b));
        Assert.Equal(new byte[] { 1, 2, 3 }, a);
        Assert.Equal(new byte[] { 4, 5, 6 }, b);
    }

    [Fact]
    public void Overwrite_Replaces_Existing_Entry()
    {
        var store = new JsonFileStore(_path);
        store.Write("main", new byte[] { 1 });
        store.Write("main", new byte[] { 99 });

        Assert.True(store.TryRead("main", out var read));
        Assert.Equal(new byte[] { 99 }, read);
    }

    [Fact]
    public void Read_Missing_Id_Returns_False()
    {
        var store = new JsonFileStore(_path);
        store.Write("main", new byte[] { 1 });

        Assert.False(store.TryRead("absent", out var read));
        Assert.Null(read);
    }

    [Fact]
    public void Read_Missing_File_Returns_False_Without_Throwing()
    {
        var store = new JsonFileStore(_path);
        Assert.False(store.TryRead("main", out var read));
        Assert.Null(read);
    }

    [Fact]
    public void Malformed_Json_Returns_False_Without_Throwing()
    {
        global::System.IO.File.WriteAllText(_path, "this is not json{{{");
        var store = new JsonFileStore(_path);
        Assert.False(store.TryRead("main", out var read));
        Assert.Null(read);
    }

    [Fact]
    public void Oversize_File_Is_Rejected_On_Read()
    {
        // Write a file > 1 MB. Spec §0.5: reading an oversize file must
        // return null and NOT crash on subsequent operations.
        var oversize = new byte[(int)(JsonFileStore.MaxFileSizeBytes + 64)];
        global::System.IO.File.WriteAllBytes(_path, oversize);

        var store = new JsonFileStore(_path);
        Assert.False(store.TryRead("main", out var read));
        Assert.Null(read);
    }

    [Fact]
    public void Constructor_Rejects_Empty_Path()
    {
        Assert.Throws<ArgumentException>(() => new JsonFileStore(""));
    }

    [Fact]
    public void DefaultPath_Is_Under_LocalAppData()
    {
        var path = JsonFileStore.DefaultPath();
        var lad = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Assert.StartsWith(lad, path, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("reactor-windows.json", path, StringComparison.Ordinal);
    }
}
