using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>The selftest host must use the same STA startup contract as ReactorApp.Run.</summary>
internal sealed partial class SelfTestStaThreadFixture(Harness h) : SelfTestFixtureBase(h)
{
    public override async Task RunAsync()
    {
        CheckApartment("Initial");
        await Harness.Render();
        CheckApartment("AfterRender");
        await Task.Delay(1);
        CheckApartment("AfterDelay");
    }

    private void CheckApartment(string phase)
    {
        var managed = Thread.CurrentThread.GetApartmentState();
        int hr = CoGetApartmentType(out int native, out int qualifier);
        H.Check($"SelfTestSTA_{phase}_Managed", managed == ApartmentState.STA, $"apartment={managed}");
        // APTTYPE_STA (0) and APTTYPE_MAINSTA (3) are the two single-threaded apartments.
        H.Check($"SelfTestSTA_{phase}_Native", hr == 0 && native is 0 or 3,
            $"hr=0x{hr:X8},apartment={native},qualifier={qualifier}");
        H.Check($"SelfTestSTA_{phase}_Dispatcher", DispatcherQueue.GetForCurrentThread()?.HasThreadAccess == true);
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoGetApartmentType(out int apartment, out int qualifier);
}
