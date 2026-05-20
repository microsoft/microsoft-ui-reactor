#nullable enable

using Microsoft.UI.Reactor.Cli.Find;
using Xunit;

namespace Reactor.Tests.Find;

public class NotesTests
{
    [Fact]
    public void GetNotes_KnownKey_ReturnsNotes()
    {
        var notes = Notes.GetNotes("UseState");

        Assert.NotNull(notes);
        Assert.NotEmpty(notes!);
    }

    [Fact]
    public void GetNotes_UnknownKey_ReturnsNull()
    {
        Assert.Null(Notes.GetNotes("UnknownKey"));
    }

    [Fact]
    public void GetNotes_Null_ReturnsNull()
    {
        Assert.Null(Notes.GetNotes(null));
    }
}
