namespace Microsoft.UI.Reactor.Cli.Loc;

/// <summary>
/// A batch of source strings to translate, with context.
/// </summary>
internal sealed class TranslationBatch
{
    /// <summary>Target locale (e.g., "fr-FR", "ar-SA").</summary>
    public required string TargetLocale { get; init; }

    /// <summary>Source locale (e.g., "en-US").</summary>
    public required string SourceLocale { get; init; }

    /// <summary>Keys and their source-locale values to translate.</summary>
    public required List<TranslationEntry> Entries { get; init; }

    /// <summary>Existing translations in the target locale for consistency context.</summary>
    public Dictionary<string, string> ExistingTranslations { get; init; } = new();
}

/// <summary>
/// A single string to translate.
/// </summary>
internal sealed class TranslationEntry
{
    /// <summary>Fully qualified key (e.g., "Common.Save").</summary>
    public required string Key { get; init; }

    /// <summary>Source-locale value to translate.</summary>
    public required string Value { get; init; }

    /// <summary>Optional comment from the .resw file.</summary>
    public string? Comment { get; init; }
}

/// <summary>
/// Result of translating a batch.
/// </summary>
internal sealed class TranslationResult
{
    /// <summary>Key -> translated value.</summary>
    public required Dictionary<string, string> Translations { get; init; }

    /// <summary>Keys that failed to translate, with error messages.</summary>
    public Dictionary<string, string> Errors { get; init; } = new();
}

/// <summary>
/// Abstraction for AI-powered translation. Implementations call a specific LLM API.
/// </summary>
internal interface ITranslationProvider
{
    /// <summary>Display name (e.g., "GitHub Copilot").</summary>
    string Name { get; }

    /// <summary>Translates a batch of strings to the target locale.</summary>
    Task<TranslationResult> TranslateAsync(TranslationBatch batch, CancellationToken cancellationToken = default);
}
