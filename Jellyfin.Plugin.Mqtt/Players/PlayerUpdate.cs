using System;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Mqtt.Players;

/// <summary>
/// A change of a player's state, handed to every enabled integration.
/// </summary>
public sealed class PlayerUpdate
{
    private readonly Func<CancellationToken, Task<byte[]?>> _loadImage;
    private Task<byte[]?>? _image;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlayerUpdate"/> class.
    /// </summary>
    /// <param name="device">The device.</param>
    /// <param name="state">The new state.</param>
    /// <param name="imageChanged">Whether the artwork changed since the previous update.</param>
    /// <param name="loadImage">Loads the artwork bytes.</param>
    public PlayerUpdate(ExposedDevice device, PlayerState state, bool imageChanged, Func<CancellationToken, Task<byte[]?>> loadImage)
    {
        Device = device;
        State = state;
        ImageChanged = imageChanged;
        _loadImage = loadImage;
    }

    /// <summary>
    /// Gets the device.
    /// </summary>
    public ExposedDevice Device { get; }

    /// <summary>
    /// Gets the new state.
    /// </summary>
    public PlayerState State { get; }

    /// <summary>
    /// Gets a value indicating whether the artwork changed since the previous update.
    /// Always true for the first update after (re)connecting.
    /// </summary>
    public bool ImageChanged { get; }

    /// <summary>
    /// Gets the artwork bytes, loaded once on first request and shared between integrations.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The image bytes, or null when there is no artwork.</returns>
    public Task<byte[]?> GetImageAsync(CancellationToken cancellationToken) => _image ??= _loadImage(cancellationToken);
}
