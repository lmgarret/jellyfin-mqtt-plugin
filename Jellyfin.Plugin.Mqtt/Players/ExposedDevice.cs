namespace Jellyfin.Plugin.Mqtt.Players;

/// <summary>
/// A Jellyfin device published as a media player.
/// </summary>
/// <param name="DeviceId">The Jellyfin device id.</param>
/// <param name="Key">The stable, topic-safe key derived from the device id.</param>
/// <param name="Name">The device display name.</param>
/// <param name="AppName">The client application name.</param>
/// <param name="AppVersion">The client application version.</param>
public sealed record ExposedDevice(string DeviceId, string Key, string Name, string? AppName, string? AppVersion);
