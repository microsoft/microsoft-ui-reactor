namespace DemoScriptTool.App.Models;

/// <summary>Build pipeline state for a single step (spec §Build state indicators).</summary>
public enum BuildState
{
    NotBuilt,
    Building,
    Succeeded,
    Fixing,
    Failed,
}
