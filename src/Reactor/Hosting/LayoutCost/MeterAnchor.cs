namespace Microsoft.UI.Reactor.Hosting.LayoutCost;

/// <summary>
/// Pure geometry for placing a meter badge relative to a Component's subtree
/// bounds. Kept pure so the placement rules (§Anchor placement) are unit-
/// testable without a live dispatcher / Composition layer.
/// </summary>
/// <remarks>
/// Spec §Anchor placement:
///   • Anchor at <c>SubtreeBounds.TopRight</c> with a small inward offset
///     so the badge sits just inside the subtree's top-right corner.
///   • Suppress the badge when the subtree is too small (width or height
///     below <see cref="MinSubtreeDimension"/>).
///   • Clip to the overlay canvas bounds — don't push onto the canvas,
///     just truncate so we don't paint off-screen.
/// </remarks>
internal static class MeterAnchor
{
    /// <summary>Minimum subtree width OR height in DIPs to render a meter.</summary>
    public const float MinSubtreeDimension = 40f;

    /// <summary>Badge width (matches <c>MeterVisual</c> box chrome — must stay in sync).</summary>
    public const float BadgeWidth = MeterVisual.BoxWidth;

    /// <summary>Badge height.</summary>
    public const float BadgeHeight = MeterVisual.BoxHeight;

    /// <summary>Inward offset from the subtree's top-right corner.</summary>
    public const float InwardOffsetX = 4f;
    public const float InwardOffsetY = 0f;

    /// <summary>
    /// Compute the badge's top-left position in the overlay canvas's coord
    /// space. Returns false when the subtree is too small to warrant a badge.
    /// </summary>
    /// <param name="subtreeX">Subtree top-left X in overlay-canvas coords.</param>
    /// <param name="subtreeY">Subtree top-left Y in overlay-canvas coords.</param>
    /// <param name="subtreeW">Subtree width in DIPs.</param>
    /// <param name="subtreeH">Subtree height in DIPs.</param>
    /// <param name="canvasW">Overlay canvas width.</param>
    /// <param name="canvasH">Overlay canvas height.</param>
    /// <param name="badgeX">Output: badge top-left X, clipped into canvas.</param>
    /// <param name="badgeY">Output: badge top-left Y, clipped into canvas.</param>
    public static bool TryComputePosition(
        float subtreeX, float subtreeY, float subtreeW, float subtreeH,
        float canvasW, float canvasH,
        out float badgeX, out float badgeY)
    {
        badgeX = 0;
        badgeY = 0;

        if (subtreeW < MinSubtreeDimension || subtreeH < MinSubtreeDimension)
            return false;

        // Subtree fully off-canvas → suppress (there is no meaningful place
        // for the badge to land).
        if (subtreeX + subtreeW <= 0 || subtreeY + subtreeH <= 0) return false;
        if (subtreeX >= canvasW || subtreeY >= canvasH) return false;

        // Top-right of the subtree, pulled inward by (BadgeWidth + InwardOffsetX).
        float x = subtreeX + subtreeW - BadgeWidth - InwardOffsetX;
        float y = subtreeY + InwardOffsetY;

        // Clamp so the badge stays fully on-screen — "anchor, don't push".
        if (x + BadgeWidth > canvasW) x = canvasW - BadgeWidth;
        if (y + BadgeHeight > canvasH) y = canvasH - BadgeHeight;
        if (x < 0) x = 0;
        if (y < 0) y = 0;

        badgeX = x;
        badgeY = y;
        return true;
    }
}
