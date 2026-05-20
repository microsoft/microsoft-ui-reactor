internal static class ScenarioWalker
{
    public static string[] FindScenarios(string scenariosRoot)
    {
        return Directory.GetFiles(scenariosRoot, "scenario.json", SearchOption.AllDirectories)
            .Where(p => !p.Contains("_generated"))
            .OrderBy(p => p)
            .ToArray();
    }
}
