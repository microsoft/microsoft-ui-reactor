using Microsoft.UI.Reactor.Hosting.Shell;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal sealed class PackagedJumpListFixture(Harness h) : SelfTestFixtureBase(h)
{
    public override async Task RunAsync()
    {
        if (!PackagedIdentityFixtures.RequirePackagedTier(H, this)) return;
        H.Check("PackagedJumpList_HasIdentity", PackageRuntime.IsPackaged);
        var native = await global::Windows.UI.StartScreen.JumpList.LoadCurrentAsync();
        var previous = native.Items.ToArray();
        var previousKind = native.SystemGroupKind;
        var recent = JumpList.ShowRecent;
        var frequent = JumpList.ShowFrequent;
        string argument = "reactor-test://" + Guid.NewGuid().ToString("N");
        try
        {
            JumpList.ShowRecent = true;
            JumpList.ShowFrequent = false;
            await JumpList.GetRemovedArgumentsAsync();
            await JumpList.UpdateAsync(new[]
            {
                JumpListItem.ForUri("Packaged probe", argument, groupCategory: "Probe"),
            });
            var loaded = await global::Windows.UI.StartScreen.JumpList.LoadCurrentAsync();
            H.Check("PackagedJumpList_PublicUpdatePersists",
                loaded.Items.Count == 1 && loaded.Items[0].Arguments == argument
                && loaded.Items[0].GroupName == "Probe");
            H.Check("PackagedJumpList_RecentForwarded",
                loaded.SystemGroupKind == global::Windows.UI.StartScreen.JumpListSystemGroupKind.Recent);
            await JumpList.ClearAsync();
            loaded = await global::Windows.UI.StartScreen.JumpList.LoadCurrentAsync();
            H.Check("PackagedJumpList_PublicClearPersists", loaded.Items.Count == 0);
        }
        finally
        {
            JumpList.ShowRecent = recent;
            JumpList.ShowFrequent = frequent;
            native.Items.Clear();
            foreach (var item in previous) native.Items.Add(item);
            native.SystemGroupKind = previousKind;
            await native.SaveAsync();
        }
    }
}
