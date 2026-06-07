#nullable enable

using Microsoft.UI.Reactor.VsExtension.Embed;
using Xunit;

namespace Reactor.VsExtension.Tests
{
    public sealed class ElevationCheckTests
    {
        [Fact(Skip = "Requires running the test process as administrator to assert an elevated token.")]
        public void ElevationCheck_DetectsAdminToken()
        {
            Assert.True(ElevationCheck.IsCurrentProcessElevated());
        }
    }
}
