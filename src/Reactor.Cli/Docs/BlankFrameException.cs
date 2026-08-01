namespace Microsoft.UI.Reactor.Cli.Docs;

/// <summary>
/// Thrown when a captured frame contains no content — either every pixel sits
/// at or above <see cref="ImageProcessor.ContentThreshold"/>, or the frame is
/// one flat colour whatever that colour is.
/// </summary>
/// <remarks>
/// <para>
/// This is the shape a doc-app capture takes when the window never painted:
/// no interactive desktop, the capture server polled before first paint, or a
/// component switch that failed silently. The frame is a solid-white surface,
/// which survives content cropping (there is nothing to crop <em>to</em>),
/// picks up the border and drop shadow like any other screenshot, and encodes
/// to a few kilobytes.
/// </para>
/// <para>
/// The uniform-fill clause covers the same failure on a themed window, which
/// comes back solid <em>dark</em> rather than solid white. Under the near-white
/// test alone every pixel of that frame counts as content, so it is worth
/// keeping the two causes distinguishable in the message: the near-white
/// wording ("no pixel below the threshold") is not merely incomplete for a dark
/// frame, it is the exact opposite of what happened, and an author who reads it
/// against a visibly dark stub has been told to look in the wrong place.
/// </para>
/// <para>
/// Historically that stub was written straight over the committed asset, so a
/// full compile in a headless session replaced the entire screenshot corpus
/// with white rectangles and still exited 0. Callers must treat this as a
/// failed capture and leave the existing file alone.
/// </para>
/// </remarks>
internal sealed class BlankFrameException : Exception
{
    /// <summary>Diagnostic code surfaced to the console and to CI logs.</summary>
    public const string DiagnosticCode = "REACTOR_DOC_SHOT_001";

    public BlankFrameException(string message) : base(message) { }

    /// <summary>No pixel in the frame is darker than the content threshold.</summary>
    public static BlankFrameException ForFrame(int width, int height) =>
        new($"{DiagnosticCode}: captured frame is blank ({width}×{height}, no pixel below " +
            $"{ImageProcessor.ContentThreshold}). The doc app window most likely never " +
            "painted — screenshot capture needs an interactive desktop.");

    /// <summary>
    /// The frame is a single flat colour. Distinct from <see cref="ForFrame"/>
    /// because for a dark fill every pixel is <em>below</em> the threshold, so
    /// that message would describe the opposite of the frame in hand.
    /// </summary>
    public static BlankFrameException ForUniformFrame(int width, int height) =>
        new($"{DiagnosticCode}: captured frame is blank ({width}×{height}, one flat colour). " +
            "The doc app window most likely painted its background but never its content — " +
            "screenshot capture needs an interactive desktop.");
}
