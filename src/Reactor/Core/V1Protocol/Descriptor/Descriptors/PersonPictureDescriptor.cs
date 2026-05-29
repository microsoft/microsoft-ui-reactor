using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml.Media.Imaging;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 3) — descriptor variant of the hand-coded
/// <c>MountPersonPicture</c> / <c>UpdatePersonPicture</c> arms in
/// <see cref="Reconciler"/>.
///
/// <para><b>Coverage:</b> a display leaf with five one-way props. Two are
/// nullable strings (<c>DisplayName</c>, <c>Initials</c>), one is a
/// nullable image source (<c>ProfilePicture</c>), and two are non-nullable
/// (<c>IsGroup</c>, <c>BadgeNumber</c>).</para>
///
/// <para><b>Phase 1 parity note:</b> the legacy arm does not pool
/// PersonPicture (allocates a fresh instance every Mount). The descriptor
/// runs through the V1 pool like every other control — write-on-Mount
/// re-applies the props, so a rented PersonPicture is brought to the same
/// state as a fresh one.</para>
/// </summary>
internal static class PersonPictureDescriptor
{
    public static readonly ControlDescriptor<PersonPictureElement, WinUI.PersonPicture> Descriptor =
        new ControlDescriptor<PersonPictureElement, WinUI.PersonPicture>
        {
            GetSetters = static e => e.Setters,
        }
        .OneWayConditional(
            get:         static e => e.DisplayName,
            set:         static (c, v) => c.DisplayName = v,
            shouldWrite: static e => e.DisplayName is not null)
        .OneWayConditional(
            get:         static e => e.Initials,
            set:         static (c, v) => c.Initials = v,
            shouldWrite: static e => e.Initials is not null)
        .OneWayConditional(
            get:         static e => e.ProfilePicture,
            set:         static (c, v) => c.ProfilePicture = new BitmapImage(new Uri(v!, UriKind.RelativeOrAbsolute)),
            shouldWrite: static e => e.ProfilePicture is not null)
        .OneWay(
            get: static e => e.IsGroup,
            set: static (c, v) => c.IsGroup = v)
        .OneWay(
            get: static e => e.BadgeNumber,
            set: static (c, v) => c.BadgeNumber = v);
}
