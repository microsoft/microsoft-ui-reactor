using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Automation.Text;
using Windows.Foundation;

namespace Microsoft.UI.Reactor.Markdown;

internal sealed partial class MarkdownSelectionAutomationPeer(MarkdownSelectionHost host)
    : FrameworkElementAutomationPeer(host), ITextProvider
{
    protected override string GetClassNameCore() => "MarkdownTextSelection";
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Document;
    protected override object GetPatternCore(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.Text ? this : base.GetPatternCore(patternInterface);
    internal IRawElementProviderSimple Provider => ProviderFromPeer(this);
    internal IRawElementProviderSimple ChildProvider(FrameworkElement child) =>
        ProviderFromPeer(CreatePeerForElement(child));
    internal void NotifyChanged(bool text, bool selection)
    {
        if (text && ListenerExists(AutomationEvents.TextPatternOnTextChanged))
            RaiseAutomationEvent(AutomationEvents.TextPatternOnTextChanged);
        if (selection && ListenerExists(AutomationEvents.TextPatternOnTextSelectionChanged))
            RaiseAutomationEvent(AutomationEvents.TextPatternOnTextSelectionChanged);
    }

    public SupportedTextSelection SupportedTextSelection => SupportedTextSelection.Single;
    public ITextRangeProvider DocumentRange
    {
        get { host.Refresh(); return Range(0, host.Document.Text.Length); }
    }
    internal MarkdownSelectionRange Range(int start, int end) => new(host, this, start, end);

    public ITextRangeProvider[] GetSelection()
    {
        host.Refresh();
        return [Range(host.Document.Start, host.Document.End)];
    }
    public ITextRangeProvider[] GetVisibleRanges()
    {
        host.Refresh();
        var entries = host.Document.Entries;
        var spans = entries.Select((entry, index) => (entry.Start,
            End: index + 1 < entries.Count ? entries[index + 1].Start : host.Document.Text.Length,
            Bounds: host.Bounds((MarkdownSelectionMarker)entry.Identity)));
        return MarkdownSelectionHost.VisibleRanges(spans, host.Viewport(), host.CharacterBounds)
            .Select(span => (ITextRangeProvider)Range(span.Start, span.End)).ToArray();
    }
    public ITextRangeProvider RangeFromChild(IRawElementProviderSimple childElement)
    {
        host.Refresh();
        var childPeer = PeerFromProvider(childElement) as FrameworkElementAutomationPeer;
        foreach (var child in host.EmbeddedControls())
            if (childPeer is not null && Equals(childPeer.Owner, child.Control))
                return Range(child.Start, child.End);
        throw new ArgumentException("The control is not embedded in this message.", nameof(childElement));
    }
    public ITextRangeProvider RangeFromPoint(Point screenLocation)
    {
        host.Refresh();
        var screen = GetBoundingRectangle();
        double scale = host.XamlRoot?.RasterizationScale ?? 1;
        int offset = host.HitTest(new Point((screenLocation.X - screen.X) / scale,
            (screenLocation.Y - screen.Y) / scale));
        return Range(offset, offset);
    }
}

internal sealed partial class MarkdownSelectionRange(
    MarkdownSelectionHost host, MarkdownSelectionAutomationPeer peer, int start, int end) : ITextRangeProvider
{
    private int _start = start;
    private int _end = end;

    private void Normalize()
    {
        host.Refresh();
        _start = Math.Clamp(_start, 0, host.Document.Text.Length);
        _end = Math.Clamp(_end, _start, host.Document.Text.Length);
    }

    public ITextRangeProvider Clone() { Normalize(); return peer.Range(_start, _end); }
    public bool Compare(ITextRangeProvider range) =>
        range is MarkdownSelectionRange other && ReferenceEquals(host, other.Host) &&
        _start == other._start && _end == other._end;
    private MarkdownSelectionHost Host => host;
    private MarkdownSelectionRange SameDocument(ITextRangeProvider range) =>
        range is MarkdownSelectionRange other && ReferenceEquals(host, other.Host)
            ? other : throw new ArgumentException("Range belongs to another document.", nameof(range));

    public int CompareEndpoints(TextPatternRangeEndpoint endpoint, ITextRangeProvider targetRange,
        TextPatternRangeEndpoint targetEndpoint)
    {
        Normalize();
        var other = SameDocument(targetRange);
        other.Normalize();
        return Endpoint(endpoint).CompareTo(other.Endpoint(targetEndpoint));
    }
    private int Endpoint(TextPatternRangeEndpoint endpoint) =>
        endpoint == TextPatternRangeEndpoint.Start ? _start : _end;

    public void ExpandToEnclosingUnit(TextUnit unit)
    {
        Normalize();
        if (unit is TextUnit.Document or TextUnit.Page)
        { _start = 0; _end = host.Document.Text.Length; return; }
        var boundaries = Boundaries(unit);
        _start = boundaries.LastOrDefault(i => i <= _start, 0);
        _end = boundaries.FirstOrDefault(i => i > _start, host.Document.Text.Length);
    }

    public ITextRangeProvider FindAttribute(int attributeId, object value, bool backward) =>
        attributeId == 40015 && Equals(value, true) ? Clone() : null!;
    public ITextRangeProvider FindText(string text, bool backward, bool ignoreCase)
    {
        Normalize();
        ArgumentException.ThrowIfNullOrEmpty(text);
        string content = host.Document.Text[_start.._end];
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        int found = backward ? content.LastIndexOf(text, comparison) : content.IndexOf(text, comparison);
        return found < 0 ? null! : peer.Range(_start + found, _start + found + text.Length);
    }
    // WinUI translates E_NOT_SUPPORTED (NotSupportedException) into UIA's reserved sentinel.
    public object GetAttributeValue(int attributeId) => attributeId == 40015 ? true
        : throw new NotSupportedException("The text attribute is not supported.");

    public void GetBoundingRectangles(out double[] rectangles)
    {
        Normalize();
        var result = new List<double>();
        var screen = peer.GetBoundingRectangle();
        double scale = host.XamlRoot?.RasterizationScale ?? 1;
        var viewport = host.Viewport();
        foreach (var (i, next) in host.VisibleGlyphs(_start, _end))
        {
            var rect = host.GlyphBounds(i, next);
            rect = MarkdownSelectionHost.Intersect(rect, viewport);
            if (rect.Width > 0 && rect.Height > 0)
                result.AddRange([screen.X + rect.X * scale, screen.Y + rect.Y * scale,
                    rect.Width * scale, rect.Height * scale]);
        }
        rectangles = result.ToArray();
    }
    public IRawElementProviderSimple GetEnclosingElement() => peer.Provider;
    public string GetText(int maxLength)
    {
        Normalize();
        if (maxLength < -1) throw new ArgumentOutOfRangeException(nameof(maxLength));
        return host.Document.Text[_start..(maxLength < 0 ? _end : Math.Min(_end, _start + maxLength))];
    }
    public IRawElementProviderSimple[] GetChildren()
    {
        Normalize();
        // Range-scoped, not all descendants and not unconditionally empty.
        return host.EmbeddedControls()
            .Where(child => child.Start == child.End
                ? _start <= child.Start && (child.Start < _end || _start == _end && child.Start == _start)
                : child.Start < _end && child.End > _start)
            .Select(child => peer.ChildProvider(child.Control)).ToArray();
    }
    public int Move(TextUnit unit, int count)
    {
        Normalize();
        if (count == 0) return 0;
        var result = MarkdownSelectionRangeMovement.Move(_start, _end, Boundaries(unit), count);
        _start = result.Start;
        _end = result.End;
        return result.Moved;
    }

    public void MoveEndpointByRange(TextPatternRangeEndpoint endpoint, ITextRangeProvider targetRange,
        TextPatternRangeEndpoint targetEndpoint)
    {
        var other = SameDocument(targetRange);
        other.Normalize();
        SetEndpoint(endpoint, other.Endpoint(targetEndpoint));
    }
    public int MoveEndpointByUnit(TextPatternRangeEndpoint endpoint, TextUnit unit, int count)
    {
        Normalize();
        var boundaries = Boundaries(unit);
        var result = MarkdownSelectionRangeMovement.MoveEndpoint(Endpoint(endpoint), boundaries, count);
        SetEndpoint(endpoint, result.Offset);
        return result.Moved;
    }
    private void SetEndpoint(TextPatternRangeEndpoint endpoint, int value)
    {
        if (endpoint == TextPatternRangeEndpoint.Start) { _start = value; _end = Math.Max(_end, value); }
        else { _end = value; _start = Math.Min(_start, value); }
    }
    private int[] Boundaries(TextUnit unit)
    {
        string text = host.Document.Text;
        if (unit is TextUnit.Document or TextUnit.Page) return [0, text.Length];
        if (unit == TextUnit.Character)
            return global::System.Globalization.StringInfo.ParseCombiningCharacters(text).Append(text.Length).ToArray();
        if (unit is TextUnit.Paragraph or TextUnit.Format)
            return host.Document.Entries.Select(e => e.Start).Append(text.Length).Distinct().ToArray();
        if (unit == TextUnit.Line) return host.VisualLineBoundaries();
        var boundaries = new List<int> { 0 };
        for (int i = 1; i < text.Length; i++)
            if (char.IsWhiteSpace(text[i]) != char.IsWhiteSpace(text[i - 1])) boundaries.Add(i);
        boundaries.Add(text.Length);
        return boundaries.ToArray();
    }
    public void Select() { Normalize(); host.Select(_start, _end); }
    public void AddToSelection() => Select();
    public void RemoveFromSelection()
    {
        Normalize();
        if (host.Document.Start == _start && host.Document.End == _end) host.Select(_end, _end);
    }
    public void ScrollIntoView(bool alignToTop)
    {
        Normalize();
        int offset = alignToTop || _start == _end ? _start : host.Document.MoveGrapheme(_end, -1);
        host.ScrollRangeIntoView(offset, alignToTop);
    }
}
