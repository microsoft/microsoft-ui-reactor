#nullable enable

using System.Windows.Media;

namespace Microsoft.UI.Reactor.VsExtension.UI
{
    public enum EmbedStatus
    {
        Idle,
        Launching,
        WaitingForHandshake,
        Embedded,
        Building,
        Respawning,
        BuildFailed,
        Crashed,
        ProjectSwitching,
    }

    public static class EmbedStatusInfo
    {
        public static string GetText(EmbedStatus status)
        {
            switch (status)
            {
                case EmbedStatus.Idle:
                    return "Idle";
                case EmbedStatus.Launching:
                    return "Launching…";
                case EmbedStatus.WaitingForHandshake:
                    return "Waiting for handshake…";
                case EmbedStatus.Embedded:
                    return "Live";
                case EmbedStatus.Building:
                    return "Building…";
                case EmbedStatus.Respawning:
                    return "Respawning…";
                case EmbedStatus.BuildFailed:
                    return "Build failed";
                case EmbedStatus.Crashed:
                    return "Preview crashed";
                case EmbedStatus.ProjectSwitching:
                    return "Switching project…";
                default:
                    return status.ToString();
            }
        }

        public static Brush GetBrush(EmbedStatus status)
        {
            switch (status)
            {
                case EmbedStatus.Embedded:
                    return Brushes.Green;
                case EmbedStatus.Building:
                case EmbedStatus.Respawning:
                case EmbedStatus.ProjectSwitching:
                    return Brushes.Goldenrod;
                case EmbedStatus.BuildFailed:
                case EmbedStatus.Crashed:
                    return Brushes.Red;
                default:
                    return Brushes.Gray;
            }
        }
    }
}
