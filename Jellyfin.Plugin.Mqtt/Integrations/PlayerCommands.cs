using System.Collections.Generic;
using Jellyfin.Plugin.Mqtt.Players;

namespace Jellyfin.Plugin.Mqtt.Integrations;

/// <summary>
/// Commands an integration received for a player.
/// </summary>
/// <param name="DeviceKey">The key of the target device.</param>
/// <param name="Commands">The commands, in order.</param>
public sealed record PlayerCommands(string DeviceKey, IReadOnlyList<PlayerCommand> Commands);
