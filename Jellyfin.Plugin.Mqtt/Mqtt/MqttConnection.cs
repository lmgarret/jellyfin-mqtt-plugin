using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Mqtt.Configuration;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Protocol;

namespace Jellyfin.Plugin.Mqtt.Mqtt;

/// <summary>
/// Keeps a connection to the MQTT broker alive and announces the bridge status on it.
/// </summary>
public sealed class MqttConnection : IAsyncDisposable
{
    /// <summary>
    /// Payload published on the status topic while the bridge is connected.
    /// </summary>
    public const string OnlinePayload = "online";

    /// <summary>
    /// Payload published on the status topic when the bridge goes away.
    /// </summary>
    public const string OfflinePayload = "offline";

    private static readonly TimeSpan _minReconnectDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan _maxReconnectDelay = TimeSpan.FromMinutes(1);

    private readonly ILogger<MqttConnection> _logger;
    private readonly IMqttClient _client;
    private CancellationTokenSource? _loopCts;
    private Task? _loop;
    private string _statusTopic = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="MqttConnection"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public MqttConnection(ILogger<MqttConnection> logger)
    {
        _logger = logger;
        _client = new MqttClientFactory().CreateMqttClient();
        _client.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
    }

    /// <summary>
    /// Gets or sets the handler called after every (re)connection, once the status topic reads online.
    /// </summary>
    public Func<Task>? Connected { get; set; }

    /// <summary>
    /// Gets or sets the handler called for every message received on a subscribed topic, with its topic and payload.
    /// </summary>
    public Func<string, string, Task>? MessageReceived { get; set; }

    /// <summary>
    /// Gets a value indicating whether the client is currently connected.
    /// </summary>
    public bool IsConnected => _client.IsConnected;

    /// <summary>
    /// Connects to the broker described by the configuration, and keeps reconnecting until stopped.
    /// </summary>
    /// <param name="config">The plugin configuration.</param>
    /// <param name="statusTopic">The topic that carries the bridge availability.</param>
    /// <returns>A task that completes once the connection loop is started.</returns>
    public async Task StartAsync(PluginConfiguration config, string statusTopic)
    {
        await StopAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(config.BrokerHost))
        {
            _logger.LogInformation("No MQTT broker configured, the bridge stays idle");
            return;
        }

        _statusTopic = statusTopic;
        var options = BuildOptions(config, statusTopic);
        _loopCts = new CancellationTokenSource();
        _loop = RunAsync(options, _loopCts.Token);
    }

    /// <summary>
    /// Marks the bridge offline and disconnects.
    /// </summary>
    /// <returns>A task that completes once disconnected.</returns>
    public async Task StopAsync()
    {
        if (_loopCts is null)
        {
            return;
        }

        await _loopCts.CancelAsync().ConfigureAwait(false);
        try
        {
            await _loop!.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        _loopCts.Dispose();
        _loopCts = null;
        _loop = null;

        if (_client.IsConnected)
        {
            try
            {
                await PublishAsync(_statusTopic, OfflinePayload, true, CancellationToken.None).ConfigureAwait(false);
                await _client.DisconnectAsync().ConfigureAwait(false);
            }
#pragma warning disable CA1031 // Best effort, the last will covers a failed goodbye.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                _logger.LogDebug(ex, "Error while disconnecting from the MQTT broker");
            }
        }
    }

    /// <summary>
    /// Publishes a message with QoS 1. Does nothing while disconnected.
    /// </summary>
    /// <param name="topic">The topic.</param>
    /// <param name="payload">The payload, empty to clear a retained message.</param>
    /// <param name="retain">Whether the broker retains the message.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes once the message is published.</returns>
    public Task PublishAsync(string topic, string payload, bool retain, CancellationToken cancellationToken)
        => PublishAsync(topic, System.Text.Encoding.UTF8.GetBytes(payload), retain, cancellationToken);

    /// <summary>
    /// Publishes a binary message with QoS 1. Does nothing while disconnected.
    /// </summary>
    /// <param name="topic">The topic.</param>
    /// <param name="payload">The payload, empty to clear a retained message.</param>
    /// <param name="retain">Whether the broker retains the message.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes once the message is published.</returns>
    public async Task PublishAsync(string topic, byte[] payload, bool retain, CancellationToken cancellationToken)
    {
        if (!_client.IsConnected)
        {
            return;
        }

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .WithRetainFlag(retain)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();
        await _client.PublishAsync(message, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Subscribes to a topic filter with QoS 1.
    /// </summary>
    /// <param name="topicFilter">The topic filter.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes once subscribed.</returns>
    public async Task SubscribeAsync(string topicFilter, CancellationToken cancellationToken)
    {
        var options = new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(topicFilter, MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();
        await _client.SubscribeAsync(options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Unsubscribes from a topic filter.
    /// </summary>
    /// <param name="topicFilter">The topic filter.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes once unsubscribed.</returns>
    public async Task UnsubscribeAsync(string topicFilter, CancellationToken cancellationToken)
    {
        var options = new MqttClientUnsubscribeOptionsBuilder()
            .WithTopicFilter(topicFilter)
            .Build();
        await _client.UnsubscribeAsync(options, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _client.Dispose();
    }

    private static MqttClientOptions BuildOptions(PluginConfiguration config, string statusTopic)
    {
        var builder = new MqttClientOptionsBuilder()
            .WithTcpServer(config.BrokerHost, config.BrokerPort)
            .WithClientId(string.IsNullOrWhiteSpace(config.ClientId) ? "jellyfin" : config.ClientId)
            .WithCleanSession()
            .WithWillTopic(statusTopic)
            .WithWillPayload(OfflinePayload)
            .WithWillRetain()
            .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce);

        if (!string.IsNullOrEmpty(config.Username))
        {
            builder = builder.WithCredentials(config.Username, config.Password);
        }

        if (config.UseTls)
        {
            var allowUntrusted = config.AllowUntrustedCertificates;
            builder = builder.WithTlsOptions(tls =>
            {
                tls.UseTls();
                if (allowUntrusted)
                {
                    tls.WithCertificateValidationHandler(_ => true);
                }
            });
        }

        return builder.Build();
    }

    private async Task RunAsync(MqttClientOptions options, CancellationToken cancellationToken)
    {
        var delay = _minReconnectDelay;
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!_client.IsConnected)
            {
                try
                {
                    await _client.ConnectAsync(options, cancellationToken).ConfigureAwait(false);
                    _logger.LogInformation("Connected to MQTT broker");
                    delay = _minReconnectDelay;
                    await PublishAsync(_statusTopic, OnlinePayload, true, cancellationToken).ConfigureAwait(false);
                    if (Connected is { } connected)
                    {
                        await connected.Invoke().ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
#pragma warning disable CA1031 // The loop must survive any connection failure.
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    _logger.LogWarning("Could not connect to MQTT broker, retrying in {Delay}: {Message}", delay, ex.Message);
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, _maxReconnectDelay.Ticks));
                    continue;
                }
            }

            await Task.Delay(_minReconnectDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    private Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        var handler = MessageReceived;
        if (handler is null)
        {
            return Task.CompletedTask;
        }

        var topic = e.ApplicationMessage.Topic;
        var payload = e.ApplicationMessage.ConvertPayloadToString() ?? string.Empty;

        // Handled off the client's receive loop, so handlers can publish without waiting on themselves.
        _ = Task.Run(async () =>
        {
            try
            {
                await handler.Invoke(topic, payload).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // A faulty handler must not break the MQTT client.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                _logger.LogError(ex, "Error while handling MQTT message on {Topic}", topic);
            }
        });
        return Task.CompletedTask;
    }
}
