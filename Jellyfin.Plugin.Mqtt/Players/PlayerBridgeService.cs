using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Queries;
using Jellyfin.Plugin.Mqtt.Configuration;
using Jellyfin.Plugin.Mqtt.Mqtt;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Devices;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Plugins;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Mqtt.Players;

/// <summary>
/// Publishes the exposed Jellyfin devices as MQTT media players and forwards their commands.
/// </summary>
public sealed class PlayerBridgeService : IHostedService, IDisposable
{
    private static readonly TimeSpan _refreshInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _positionInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan _cleanupWindow = TimeSpan.FromSeconds(10);

    private readonly MqttConnection _connection;
    private readonly IDiscoveryPublisher _discovery;
    private readonly ArtworkLoader _artwork;
    private readonly ISessionManager _sessionManager;
    private readonly IDeviceManager _deviceManager;
    private readonly IServerApplicationHost _applicationHost;
    private readonly ILogger<PlayerBridgeService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly CancellationTokenSource _stopping = new();

    // Guarded by _lock.
    private readonly Dictionary<string, ExposedDevice> _devices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PublishedState> _published = new(StringComparer.Ordinal);
    private PluginConfiguration _config = new();
    private DiscoverySettings? _settings;
    private HashSet<Guid> _allowedUsers = [];
    private HashSet<string> _excludedDevices = [];

    private Timer? _timer;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlayerBridgeService"/> class.
    /// </summary>
    /// <param name="connection">The MQTT connection.</param>
    /// <param name="discovery">The discovery publisher.</param>
    /// <param name="artwork">The artwork loader.</param>
    /// <param name="sessionManager">The session manager.</param>
    /// <param name="deviceManager">The device manager.</param>
    /// <param name="applicationHost">The server application host.</param>
    /// <param name="logger">The logger.</param>
    public PlayerBridgeService(
        MqttConnection connection,
        IDiscoveryPublisher discovery,
        ArtworkLoader artwork,
        ISessionManager sessionManager,
        IDeviceManager deviceManager,
        IServerApplicationHost applicationHost,
        ILogger<PlayerBridgeService> logger)
    {
        _connection = connection;
        _discovery = discovery;
        _artwork = artwork;
        _sessionManager = sessionManager;
        _deviceManager = deviceManager;
        _applicationHost = applicationHost;
        _logger = logger;
    }

    private CancellationToken Token => _stopping.Token;

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var plugin = Plugin.Instance ?? throw new InvalidOperationException("Plugin is not initialized");

        _connection.Connected = OnConnectedAsync;
        _connection.MessageReceived = OnMessageAsync;

        _sessionManager.SessionStarted += OnSessionEvent;
        _sessionManager.SessionEnded += OnSessionEvent;
        _sessionManager.CapabilitiesChanged += OnSessionEvent;
        _sessionManager.PlaybackStart += OnPlaybackEvent;
        _sessionManager.PlaybackProgress += OnPlaybackEvent;
        _sessionManager.PlaybackStopped += OnPlaybackEvent;
        plugin.ConfigurationChanged += OnConfigurationChanged;

        await ApplyConfigurationAsync(plugin.Configuration).ConfigureAwait(false);
        _timer = new Timer(_ => Run(SyncAsync), null, _refreshInterval, _refreshInterval);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (Plugin.Instance is { } plugin)
        {
            plugin.ConfigurationChanged -= OnConfigurationChanged;
        }

        _sessionManager.SessionStarted -= OnSessionEvent;
        _sessionManager.SessionEnded -= OnSessionEvent;
        _sessionManager.CapabilitiesChanged -= OnSessionEvent;
        _sessionManager.PlaybackStart -= OnPlaybackEvent;
        _sessionManager.PlaybackProgress -= OnPlaybackEvent;
        _sessionManager.PlaybackStopped -= OnPlaybackEvent;

        if (_timer is not null)
        {
            await _timer.DisposeAsync().ConfigureAwait(false);
            _timer = null;
        }

        await _stopping.CancelAsync().ConfigureAwait(false);
        await _connection.StopAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _timer?.Dispose();
        _lock.Dispose();
        _stopping.Dispose();
    }

    private static bool ConnectionChanged(PluginConfiguration a, PluginConfiguration b) =>
        !string.Equals(a.BrokerHost, b.BrokerHost, StringComparison.Ordinal)
        || a.BrokerPort != b.BrokerPort
        || a.UseTls != b.UseTls
        || a.AllowUntrustedCertificates != b.AllowUntrustedCertificates
        || !string.Equals(a.Username, b.Username, StringComparison.Ordinal)
        || !string.Equals(a.Password, b.Password, StringComparison.Ordinal)
        || !string.Equals(a.ClientId, b.ClientId, StringComparison.Ordinal)
        || !string.Equals(a.BaseTopic, b.BaseTopic, StringComparison.Ordinal)
        || !string.Equals(a.DiscoveryPrefix, b.DiscoveryPrefix, StringComparison.Ordinal);

    private void OnConfigurationChanged(object? sender, BasePluginConfiguration e)
    {
        if (e is PluginConfiguration config)
        {
            Run(() => ApplyConfigurationAsync(config));
        }
    }

    private void OnSessionEvent(object? sender, SessionEventArgs e) => QueueRefresh(e.SessionInfo?.DeviceId);

    private void OnPlaybackEvent(object? sender, PlaybackProgressEventArgs e) => QueueRefresh(e.Session?.DeviceId);

    private void QueueRefresh(string? deviceId)
    {
        if (!string.IsNullOrEmpty(deviceId))
        {
            Run(() => RefreshDeviceAsync(deviceId));
        }
    }

    private void Run(Func<Task> action)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (Token.IsCancellationRequested)
            {
            }
            catch (ObjectDisposedException) when (Token.IsCancellationRequested)
            {
            }
#pragma warning disable CA1031 // Background work must never crash the server.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                _logger.LogError(ex, "MQTT bridge update failed");
            }
        });
    }

    private async Task ApplyConfigurationAsync(PluginConfiguration config)
    {
        bool reconnect;
        await _lock.WaitAsync(Token).ConfigureAwait(false);
        try
        {
            var previous = _settings is null ? null : _config;
            reconnect = previous is null || ConnectionChanged(previous, config);

            if (reconnect && previous is not null && _connection.IsConnected)
            {
                // Topics may move, so withdraw everything published under the previous settings.
                foreach (var device in _devices.Values)
                {
                    await WithdrawAsync(device.Key).ConfigureAwait(false);
                }
            }

            _config = config;
            _settings = new DiscoverySettings(config.DiscoveryPrefix, new PlayerTopics(config.BaseTopic, _applicationHost.SystemId));
            _allowedUsers = config.AllowedUserIds
                .Select(id => Guid.TryParse(id, out var guid) ? guid : Guid.Empty)
                .Where(id => id != Guid.Empty)
                .ToHashSet();
            _excludedDevices = config.ExcludedDeviceIds.ToHashSet(StringComparer.Ordinal);

            if (reconnect)
            {
                _devices.Clear();
                _published.Clear();
            }
        }
        finally
        {
            _lock.Release();
        }

        if (reconnect)
        {
            // Outside the lock: stopping waits on the connection loop, which may be waiting on the lock.
            await _connection.StartAsync(config, _settings.Topics.StatusTopic).ConfigureAwait(false);
        }
        else
        {
            await SyncAsync().ConfigureAwait(false);
        }
    }

    private async Task OnConnectedAsync()
    {
        string? discoveryFilter;
        string stateFilter;
        await _lock.WaitAsync(Token).ConfigureAwait(false);
        try
        {
            var settings = _settings!;
            await _connection.SubscribeAsync(settings.Topics.CommandFilter, Token).ConfigureAwait(false);

            _published.Clear();
            await SyncLockedAsync().ConfigureAwait(false);

            // Retained messages of players that are no longer exposed come back on these
            // subscriptions, and get cleared while the window is open.
            discoveryFilter = _discovery.GetCleanupFilter(settings);
            stateFilter = settings.Topics.StateFilter;
            if (discoveryFilter is not null)
            {
                await _connection.SubscribeAsync(discoveryFilter, Token).ConfigureAwait(false);
            }

            await _connection.SubscribeAsync(stateFilter, Token).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }

        Run(async () =>
        {
            await Task.Delay(_cleanupWindow, Token).ConfigureAwait(false);
            if (discoveryFilter is not null)
            {
                await _connection.UnsubscribeAsync(discoveryFilter, Token).ConfigureAwait(false);
            }

            await _connection.UnsubscribeAsync(stateFilter, Token).ConfigureAwait(false);
        });
    }

    private async Task OnMessageAsync(string topic, string payload)
    {
        await _lock.WaitAsync(Token).ConfigureAwait(false);
        try
        {
            var settings = _settings!;
            if (settings.Topics.ParseKey(topic, "command") is { } commandKey)
            {
                await HandleCommandLockedAsync(commandKey, payload).ConfigureAwait(false);
                return;
            }

            var exposedKeys = _devices.Values.Select(d => d.Key).ToHashSet(StringComparer.Ordinal);
            if (settings.Topics.ParseKey(topic, "state") is { } stateKey)
            {
                if (payload.Length > 0 && !exposedKeys.Contains(stateKey))
                {
                    await _connection.PublishAsync(topic, string.Empty, true, Token).ConfigureAwait(false);
                    await _connection.PublishAsync(settings.Topics.ImageTopic(stateKey), string.Empty, true, Token).ConfigureAwait(false);
                }

                return;
            }

            await _discovery.CleanupAsync(settings, topic, payload, exposedKeys, Token).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task HandleCommandLockedAsync(string key, string payload)
    {
        var device = _devices.Values.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.Ordinal));
        if (device is null)
        {
            _logger.LogDebug("Ignoring command for unknown player {Key}", key);
            return;
        }

        var session = FindSession(device.DeviceId);
        if (session is null)
        {
            _logger.LogInformation("Ignoring command for {Device}: no active session", device.Name);
            return;
        }

        try
        {
            await PlayerCommands.ExecuteAsync(_sessionManager, session.Id, payload, Token).ConfigureAwait(false);
        }
        catch (FormatException ex)
        {
            _logger.LogWarning("Invalid command for {Device}: {Message}", device.Name, ex.Message);
        }
#pragma warning disable CA1031 // A client refusing a command must not break the bridge.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            _logger.LogWarning(ex, "Command for {Device} failed", device.Name);
        }
    }

    private async Task SyncAsync()
    {
        await _lock.WaitAsync(Token).ConfigureAwait(false);
        try
        {
            await SyncLockedAsync().ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task RefreshDeviceAsync(string deviceId)
    {
        await _lock.WaitAsync(Token).ConfigureAwait(false);
        try
        {
            if (_devices.TryGetValue(deviceId, out var device))
            {
                await PublishStateLockedAsync(device).ConfigureAwait(false);
            }
            else if (IsExposed(deviceId, _sessionManager.Sessions.Where(s => s.DeviceId == deviceId).Select(s => s.UserId)))
            {
                // A new device of an allowed user.
                await SyncLockedAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task SyncLockedAsync()
    {
        if (!_connection.IsConnected)
        {
            return;
        }

        var settings = _settings!;
        var current = ComputeDevices();

        foreach (var removed in _devices.Values.Where(d => !current.ContainsKey(d.DeviceId)).ToList())
        {
            _logger.LogInformation("No longer exposing {Device}", removed.Name);
            await WithdrawAsync(removed.Key).ConfigureAwait(false);
            _devices.Remove(removed.DeviceId);
            _published.Remove(removed.DeviceId);
        }

        foreach (var device in current.Values)
        {
            if (!_devices.TryGetValue(device.DeviceId, out var known) || known != device)
            {
                if (known is null)
                {
                    _logger.LogInformation("Exposing {Device} ({App})", device.Name, device.AppName);
                }

                await _discovery.PublishAsync(settings, device, Token).ConfigureAwait(false);
                _devices[device.DeviceId] = device;
            }

            await PublishStateLockedAsync(device).ConfigureAwait(false);
        }
    }

    private async Task WithdrawAsync(string key)
    {
        var settings = _settings!;
        await _discovery.RemoveAsync(settings, key, Token).ConfigureAwait(false);
        await _connection.PublishAsync(settings.Topics.StateTopic(key), string.Empty, true, Token).ConfigureAwait(false);
        await _connection.PublishAsync(settings.Topics.ImageTopic(key), string.Empty, true, Token).ConfigureAwait(false);
    }

    private async Task PublishStateLockedAsync(ExposedDevice device)
    {
        var state = PlayerStateBuilder.Build(device, FindSession(device.DeviceId), _config.ServerUrl);
        var stable = state.ToPayload(includePosition: false);
        var full = state.ToPayload();
        var now = DateTime.UtcNow;
        var topics = _settings!.Topics;

        _published.TryGetValue(device.DeviceId, out var previous);
        if (previous is not null)
        {
            // Position-only changes are throttled; anything else goes out immediately.
            var positionOnly = string.Equals(previous.Stable, stable, StringComparison.Ordinal) && previous.Image == state.MediaImage;
            if (positionOnly && (string.Equals(previous.Full, full, StringComparison.Ordinal) || now - previous.At < _positionInterval))
            {
                return;
            }
        }

        // The image goes out before the state, so consumers never show new metadata with old artwork.
        if (previous is null || previous.Image != state.MediaImage)
        {
            var bytes = state.MediaImage is null ? null : await _artwork.LoadAsync(state.MediaImage, Token).ConfigureAwait(false);
            await _connection.PublishAsync(topics.ImageTopic(device.Key), bytes ?? [], true, Token).ConfigureAwait(false);
        }

        await _connection.PublishAsync(topics.StateTopic(device.Key), full, true, Token).ConfigureAwait(false);
        _published[device.DeviceId] = new PublishedState(stable, full, state.MediaImage, now);
    }

    private Dictionary<string, ExposedDevice> ComputeDevices()
    {
        var devices = new Dictionary<string, ExposedDevice>(StringComparer.Ordinal);
        foreach (var userId in _allowedUsers)
        {
            foreach (var info in _deviceManager.GetDeviceInfos(new DeviceQuery { UserId = userId }).Items)
            {
                if (string.IsNullOrEmpty(info.Id) || _excludedDevices.Contains(info.Id) || devices.ContainsKey(info.Id))
                {
                    continue;
                }

                var name = string.IsNullOrWhiteSpace(info.CustomName) ? info.Name : info.CustomName;
                devices[info.Id] = new ExposedDevice(info.Id, PlayerTopics.DeviceKey(info.Id), name ?? info.Id, info.AppName, info.AppVersion);
            }
        }

        // Sessions not backed by a stored device, if any.
        foreach (var session in _sessionManager.Sessions)
        {
            if (!string.IsNullOrEmpty(session.DeviceId)
                && !devices.ContainsKey(session.DeviceId)
                && IsExposed(session.DeviceId, [session.UserId]))
            {
                devices[session.DeviceId] = new ExposedDevice(
                    session.DeviceId,
                    PlayerTopics.DeviceKey(session.DeviceId),
                    session.DeviceName ?? session.DeviceId,
                    session.Client,
                    session.ApplicationVersion);
            }
        }

        return devices;
    }

    private bool IsExposed(string deviceId, IEnumerable<Guid> userIds) =>
        !_excludedDevices.Contains(deviceId) && userIds.Any(_allowedUsers.Contains);

    private SessionInfo? FindSession(string deviceId) =>
        _sessionManager.Sessions
            .Where(s => s.DeviceId == deviceId && _allowedUsers.Contains(s.UserId))
            .OrderByDescending(s => s.NowPlayingItem is not null)
            .ThenByDescending(s => s.LastActivityDate)
            .FirstOrDefault();

    private sealed record PublishedState(string Stable, string Full, ImageReference? Image, DateTime At);
}
