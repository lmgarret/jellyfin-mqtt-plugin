using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Mqtt.Players;

/// <summary>
/// Announces players to a home automation tool, e.g. through MQTT discovery.
/// </summary>
public interface IDiscoveryPublisher
{
    /// <summary>
    /// Gets the topic filter matching the retained announcements, used to clean up players
    /// that are no longer exposed. Null when there is nothing to clean up.
    /// </summary>
    /// <param name="config">The discovery settings.</param>
    /// <returns>The topic filter.</returns>
    string? GetCleanupFilter(DiscoverySettings config);

    /// <summary>
    /// Announces a player.
    /// </summary>
    /// <param name="config">The discovery settings.</param>
    /// <param name="device">The device.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes once announced.</returns>
    Task PublishAsync(DiscoverySettings config, ExposedDevice device, CancellationToken cancellationToken);

    /// <summary>
    /// Withdraws a player announcement.
    /// </summary>
    /// <param name="config">The discovery settings.</param>
    /// <param name="deviceKey">The device key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes once withdrawn.</returns>
    Task RemoveAsync(DiscoverySettings config, string deviceKey, CancellationToken cancellationToken);

    /// <summary>
    /// Handles a retained announcement received on the cleanup filter, withdrawing it
    /// when it belongs to this server but its player is no longer exposed.
    /// </summary>
    /// <param name="config">The discovery settings.</param>
    /// <param name="topic">The topic.</param>
    /// <param name="payload">The payload.</param>
    /// <param name="exposedKeys">The keys of the currently exposed devices.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes once handled.</returns>
    Task CleanupAsync(DiscoverySettings config, string topic, string payload, IReadOnlySet<string> exposedKeys, CancellationToken cancellationToken);
}
