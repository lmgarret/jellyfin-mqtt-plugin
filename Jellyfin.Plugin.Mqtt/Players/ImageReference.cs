using System;

namespace Jellyfin.Plugin.Mqtt.Players;

/// <summary>
/// The primary image of a library item.
/// </summary>
/// <param name="ItemId">The id of the item owning the image.</param>
/// <param name="Tag">The image tag, which changes with the image.</param>
public sealed record ImageReference(Guid ItemId, string Tag);
