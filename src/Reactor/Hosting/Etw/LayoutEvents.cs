namespace Microsoft.UI.Reactor.Hosting.Etw;

/// <summary>
/// Distinguishes the two layout phases we consume from the XAML ETW provider.
/// </summary>
internal enum LayoutEventKind : byte
{
    Measure = 0,
    Arrange = 1,
}

/// <summary>
/// Begin/End discriminator for an ETW layout event.
/// </summary>
internal enum LayoutEventPhase : byte
{
    Begin = 0,
    End = 1,
}

/// <summary>
/// A raw, un-paired ETW layout event as produced by <see cref="LayoutEtwConsumer"/>.
/// Raised on the ETW callback thread — consumers must be threadsafe.
/// </summary>
/// <remarks>
/// Rect / Available / Desired are only meaningful on the corresponding End events;
/// they are zero-filled otherwise.
/// </remarks>
internal readonly record struct RawLayoutEvent(
    ulong ElementId,
    LayoutEventKind Kind,
    LayoutEventPhase Phase,
    long TimestampTicks,      // 100-ns DateTime ticks (TraceEvent.TimeStamp.Ticks).
                              // Originates from ETW's QPC capture but TraceEvent
                              // converts to DateTime ticks for us. Only used as
                              // an interval source — paired-event durations are
                              // (endTicks - beginTicks).
    int ThreadId,
    float RectX,
    float RectY,
    float RectW,
    float RectH);

/// <summary>
/// A begin/end-paired layout event as produced by <see cref="EventPairing"/>.
/// Ready to be attributed to a Reactor Component and rolled up.
/// </summary>
internal readonly record struct PairedLayoutEvent(
    ulong ElementId,
    LayoutEventKind Kind,
    long InclusiveTicks,
    long SelfTicks,
    float RectX,
    float RectY,
    float RectW,
    float RectH);
