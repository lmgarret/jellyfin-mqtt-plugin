namespace Jellyfin.Plugin.Mqtt.Players;

/// <summary>
/// What a discovery publisher needs to announce players.
/// </summary>
/// <param name="Prefix">The discovery prefix.</param>
/// <param name="Topics">The player topics.</param>
public sealed record DiscoverySettings(string Prefix, PlayerTopics Topics);
