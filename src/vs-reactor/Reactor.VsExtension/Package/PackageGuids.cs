#nullable enable

using System;

namespace Microsoft.UI.Reactor.VsExtension.Package
{
    internal static class PackageGuids
    {
        public const string PackageGuidString = "36b8ec71-7e24-402c-8a72-37c983cb91f8";
        public const string ToolWindowGuidString = "1389e9cd-f8b0-4fb3-b07c-3fe01e2020c2";
        public const string CommandSetGuidString = "afeb5fd1-7552-45b2-b01a-65d3f75adefc";
        public const string OutputPaneGuidString = "2f0af69d-1d07-4b4c-9d8f-bf30a8ef67f5";

        public static readonly Guid PackageGuid = new Guid(PackageGuidString);
        public static readonly Guid ToolWindowGuid = new Guid(ToolWindowGuidString);
        public static readonly Guid CommandSetGuid = new Guid(CommandSetGuidString);
        public static readonly Guid OutputPaneGuid = new Guid(OutputPaneGuidString);
    }
}
