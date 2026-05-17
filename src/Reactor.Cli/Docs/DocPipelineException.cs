namespace Microsoft.UI.Reactor.Cli.Docs;

/// <summary>
/// Thrown when the doc pipeline detects a malformed template, manifest, or
/// snippet reference that cannot be recovered. The compiler converts this
/// into a non-zero exit code with the message routed to stderr.
/// </summary>
internal sealed class DocPipelineException : Exception
{
    /// <summary>
    /// Optional diagnostic code (e.g. <c>REACTOR_DOC_TIER_001</c>) for
    /// pipelines that surface the failure as a build error.
    /// </summary>
    public string? Code { get; }

    public DocPipelineException(string message) : base(message) { }

    public DocPipelineException(string code, string message) : base(message)
    {
        Code = code;
    }
}
