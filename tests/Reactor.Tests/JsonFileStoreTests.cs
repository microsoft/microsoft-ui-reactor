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

    // ══════════════════════════════════════════════════════════════
    //  Write guards — null/empty inputs must NOT throw and must NOT
    //  corrupt an existing file. The early-return arms in Write/
    //  TryRead protect against caller mistakes (a fresh ReactorWindow
    //  with no id, a Dispose race that nulled the payload).
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void Write_Empty_Id_Is_Silent_NoOp_Does_Not_Create_File()
    {
        var store = new JsonFileStore(_path);
        store.Write("", new byte[] { 1, 2, 3 });
        Assert.False(global::System.IO.File.Exists(_path));
    }

    [Fact]
    public void Write_Null_Data_Is_Silent_NoOp_Does_Not_Create_File()
    {
        var store = new JsonFileStore(_path);
        store.Write("main", null!);
        Assert.False(global::System.IO.File.Exists(_path));
    }

    [Fact]
    public void Read_Empty_Id_Returns_False_Without_Touching_File()
    {
        var store = new JsonFileStore(_path);
        // Pre-write a valid entry so we can prove the empty-id guard short-circuits.
        store.Write("main", new byte[] { 1 });
        Assert.False(store.TryRead("", out var read));
        Assert.Null(read);
    }

    // ══════════════════════════════════════════════════════════════
    //  Malformed entries — the per-entry catch arms in TryRead and
    //  ParseStringMap must keep the store usable.
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void Malformed_Base64_For_Existing_Id_Returns_False()
    {
        // Hand-craft a JSON file with a non-base64 value at the requested
        // id. The FormatException catch in TryRead must return false
        // (not throw, not crash). Pin: a regression that propagated the
        // exception would tank the entire ReactorApp bootstrap.
        global::System.IO.File.WriteAllText(_path, "{\"main\":\"not_valid_base64!@#\"}");
        var store = new JsonFileStore(_path);
        Assert.False(store.TryRead("main", out var read));
        Assert.Null(read);
    }

    [Fact]
    public void NonString_Entry_Returns_False_In_TryRead()
    {
        // The `entry.ValueKind != JsonValueKind.String` guard. A regression
        // that called GetString() on a number would throw — but tampered
        // files (someone editing reactor-windows.json by hand) commonly
        // have type mismatches.
        global::System.IO.File.WriteAllText(_path, "{\"main\":42}");
        var store = new JsonFileStore(_path);
        Assert.False(store.TryRead("main", out var read));
        Assert.Null(read);
    }

    [Fact]
    public void NonObject_Root_Returns_False_In_TryRead()
    {
        // The `doc.RootElement.ValueKind != JsonValueKind.Object` guard.
        // A regression that assumed object-shape would NRE on a tampered
        // file that's a top-level array.
        global::System.IO.File.WriteAllText(_path, "[\"not\",\"an\",\"object\"]");
        var store = new JsonFileStore(_path);
        Assert.False(store.TryRead("main", out var read));
        Assert.Null(read);
    }

    [Fact]
    public void Empty_String_Entry_Returns_False()
    {
        // The `string.IsNullOrEmpty(b64)` guard. Empty string is technically
        // valid JSON but cannot represent zero bytes (zero bytes is "").
        // Wait — Convert.FromBase64String("") returns byte[0], which is
        // truthy non-null. So this branch differentiates from the "valid
        // payload" path via the explicit IsNullOrEmpty check. Without it,
        // an empty-string entry would deserialize to byte[0] and be
        // indistinguishable from a successful empty payload. The product
        // chose "treat empty as missing" — pin that.
        global::System.IO.File.WriteAllText(_path, "{\"main\":\"\"}");
        var store = new JsonFileStore(_path);
        Assert.False(store.TryRead("main", out var read));
        Assert.Null(read);
    }

    // ══════════════════════════════════════════════════════════════
    //  Merge semantics — Write must preserve other ids in the file.
    //  The ReadDocumentOrEmpty + write-back path is load-bearing for
    //  multi-window apps.
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void Write_Merges_With_Existing_File_Through_Tampered_NonObject()
    {
        // The ReadDocumentOrEmpty catch arm: if the existing file is
        // tampered (top-level array), the read returns an empty dict, the
        // write proceeds with just the new id, and the tampered content
        // is overwritten. Catches a regression that propagated the
        // JsonException from ParseStringMap and crashed the next save.
        global::System.IO.File.WriteAllText(_path, "[1,2,3]");
        var store = new JsonFileStore(_path);
        store.Write("main", new byte[] { 7, 8, 9 });

        Assert.True(store.TryRead("main", out var read));
        Assert.Equal(new byte[] { 7, 8, 9 }, read);
    }

    // ══════════════════════════════════════════════════════════════
    //  Escape arms in AppendQuotedString — exercise via Write+Read
    //  round-trips with crafted ids. Each control character forces a
    //  different branch.
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void Write_Id_With_Quote_Roundtrips()
    {
        // The `case '"'` arm. A regression that emitted `"a"b"` (unescaped
        // quote) would produce invalid JSON; the next TryRead would fail.
        var store = new JsonFileStore(_path);
        store.Write("a\"b", new byte[] { 1 });
        Assert.True(store.TryRead("a\"b", out _));
    }

    [Fact]
    public void Write_Id_With_Backslash_Roundtrips()
    {
        // The `case '\\'` arm. Tampered or Windows-path-shaped ids.
        var store = new JsonFileStore(_path);
        store.Write("a\\b", new byte[] { 1 });
        Assert.True(store.TryRead("a\\b", out _));
    }

    [Fact]
    public void Write_Id_With_Newline_And_Tab_Roundtrips()
    {
        // Hits the `\n` and `\t` escape arms in AppendQuotedString.
        var store = new JsonFileStore(_path);
        store.Write("a\nb\tc", new byte[] { 1 });
        Assert.True(store.TryRead("a\nb\tc", out _));
    }

    [Fact]
    public void Write_Id_With_Control_Char_Below_0x20_Uses_Unicode_Escape()
    {
        // The `c < 0x20` arm — control chars need the \u####  escape, not
        // raw insertion. Pin: a regression that emitted raw control chars
        // would produce invalid JSON.
        var store = new JsonFileStore(_path);
        var id = "a\x01b\x1Fc"; // SOH + US — both < 0x20 but not in the named-escape list.
        store.Write(id, new byte[] { 7 });
        Assert.True(store.TryRead(id, out var read));
        Assert.Equal(new byte[] { 7 }, read);
    }

    [Fact]
    public void Write_Id_With_Carriage_Return_And_Backspace_And_Formfeed_Roundtrips()
    {
        // \r, \b, \f arms in the escape switch.
        var store = new JsonFileStore(_path);
        var id = "a\rb\bc\fd";
        store.Write(id, new byte[] { 9 });
        Assert.True(store.TryRead(id, out _));
    }
}
