namespace Microsoft.UI.Reactor.Navigation;

/// <summary>Both independent motions must finish before navigation releases its pages.</summary>
internal struct ConnectedNavigationCompletion
{
    private bool _pageCompleted;
    private bool _connectedCompleted;

    internal bool CompletePage()
    {
        _pageCompleted = true;
        return _connectedCompleted;
    }

    internal bool CompleteConnected()
    {
        _connectedCompleted = true;
        return _pageCompleted;
    }
}
