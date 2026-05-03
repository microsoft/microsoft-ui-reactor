using System;

namespace DemoScriptTool.App.Services;

/// <summary>Severity buckets for status messages routed to the UI banner / toast surface.</summary>
public enum StatusSeverity { Info, Success, Warning, Error }

/// <summary>
/// Decoupling seam between long-running services (generation pipeline, runner)
/// and the UI's notification surfaces. The shell wires up a reporter; services
/// raise messages without knowing whether they show as toast or banner.
/// </summary>
public sealed class StatusReporter
{
    public event Action<string, StatusSeverity>? Toast;
    public event Action<string?>? Generating; // null clears the title-bar status text
    public event Action<string?>? Banner;     // sticky inline banner; null hides

    public void ShowToast(string message, StatusSeverity severity = StatusSeverity.Info)
        => Toast?.Invoke(message, severity);

    public void SetGeneratingStatus(string? message) => Generating?.Invoke(message);

    public void SetBanner(string? message) => Banner?.Invoke(message);
}
