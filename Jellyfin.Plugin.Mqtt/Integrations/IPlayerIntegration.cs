using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Mqtt.Players;

namespace Jellyfin.Plugin.Mqtt.Integrations;

/// <summary>
/// Exposes players in the MQTT format of one consumer, e.g. a Home Assistant custom integration.
/// Integrations own their topics and payloads; the bridge only hands them neutral player states
/// and executes the commands they parse.
/// </summary>
public interface IPlayerIntegration
{
    /// <summary>
    /// Gets the id used to enable the integration in the configuration.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Gets the name shown on the configuration page.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the description shown on the configuration page, e.g. the consumer's requirements.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Gets the repository of the consumer.
    /// </summary>
    Uri RepositoryUrl { get; }

    /// <summary>
    /// Gets the configuration settings used by the integration, shown while it is enabled.
    /// </summary>
    IReadOnlyList<IntegrationSetting> Settings { get; }

    /// <summary>
    /// Gets the topic filters to subscribe to while connected, e.g. command topics.
    /// </summary>
    /// <param name="context">The integration context.</param>
    /// <returns>The topic filters.</returns>
    IEnumerable<string> GetSubscriptions(IntegrationContext context);

    /// <summary>
    /// Gets the topic filters matching this integration's retained messages. They are subscribed
    /// briefly after connecting, so <see cref="HandleMessageAsync"/> can clear players that are
    /// no longer exposed.
    /// </summary>
    /// <param name="context">The integration context.</param>
    /// <returns>The topic filters.</returns>
    IEnumerable<string> GetCleanupFilters(IntegrationContext context);

    /// <summary>
    /// Announces a player, or updates its announcement.
    /// </summary>
    /// <param name="context">The integration context.</param>
    /// <param name="device">The device.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes once announced.</returns>
    Task PublishDeviceAsync(IntegrationContext context, ExposedDevice device, CancellationToken cancellationToken);

    /// <summary>
    /// Publishes a player state.
    /// </summary>
    /// <param name="context">The integration context.</param>
    /// <param name="update">The update.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes once published.</returns>
    Task PublishStateAsync(IntegrationContext context, PlayerUpdate update, CancellationToken cancellationToken);

    /// <summary>
    /// Withdraws a player and clears its retained messages.
    /// </summary>
    /// <param name="context">The integration context.</param>
    /// <param name="deviceKey">The device key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes once withdrawn.</returns>
    Task RemoveDeviceAsync(IntegrationContext context, string deviceKey, CancellationToken cancellationToken);

    /// <summary>
    /// Handles a message received on a subscription or cleanup filter.
    /// </summary>
    /// <param name="context">The integration context.</param>
    /// <param name="topic">The topic.</param>
    /// <param name="payload">The payload.</param>
    /// <param name="exposedKeys">The keys of the currently exposed devices.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The commands addressed to a player, or null when the message is not a command for this integration.</returns>
    /// <exception cref="System.FormatException">The message is a malformed command.</exception>
    Task<PlayerCommands?> HandleMessageAsync(IntegrationContext context, string topic, string payload, IReadOnlySet<string> exposedKeys, CancellationToken cancellationToken);
}
