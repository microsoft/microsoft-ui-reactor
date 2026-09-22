using Microsoft.VisualStudio.TestTools.UnitTesting;

// This suite drives one interactive desktop through a single winapp workflow id, so its tests are
// not independent of each other in the way parallelization assumes. Two concrete hazards, both of
// which are silent rather than red:
//
//   * WinAppUi.InvocationCount is process-wide, and the teardown treats a delta in it as "the test
//     that just finished used winapp". Under parallelism a headless test can observe a delta that
//     a concurrent UI test caused, and then release the shared workflow's turn while that test is
//     still mid-interaction.
//   * WinAppWorkflowIdTests sends a real `winapp ui yield` under WinAppUi.WorkflowId to prove the
//     wiring reaches the child. Run alongside another E2E test, that probe releases the desktop
//     turn the other test is relying on.
//
// MSTest does not parallelize unless asked, so this changes no behaviour today. It is here because
// the whole suite depends on that default: without the attribute the invariant lives in an unwritten
// assumption that a .runsettings or a `--parallel` on the command line would silently break, and the
// resulting failures would look like UI flakiness rather than a configuration change.
[assembly: DoNotParallelize]
