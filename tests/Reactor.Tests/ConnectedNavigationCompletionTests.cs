using Microsoft.UI.Reactor.Navigation;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

public class ConnectedNavigationCompletionTests
{
    [Fact]
    public void PageCompletionWaitsForNativeMotion()
    {
        var completion = new ConnectedNavigationCompletion();
        Assert.False(completion.CompletePage());
        Assert.False(completion.CompletePage());
        Assert.True(completion.CompleteConnected());
    }

    [Fact]
    public void NativeCompletionWaitsForPageMotion()
    {
        var completion = new ConnectedNavigationCompletion();
        Assert.False(completion.CompleteConnected());
        Assert.False(completion.CompleteConnected());
        Assert.True(completion.CompletePage());
    }

    [Fact]
    public void SeparateNavigationsDoNotShareCompletion()
    {
        var first = new ConnectedNavigationCompletion();
        var second = new ConnectedNavigationCompletion();
        Assert.False(first.CompletePage());
        Assert.False(second.CompleteConnected());
        Assert.True(first.CompleteConnected());
        Assert.True(second.CompletePage());
    }
}
