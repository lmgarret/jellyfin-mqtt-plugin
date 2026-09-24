using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Mqtt.Players;

/// <summary>
/// Loads resized artwork through the Jellyfin image processor, so consumers get the image bytes
/// without having to reach the server.
/// </summary>
public class ArtworkLoader
{
    private const int MaxSize = 600;

    private readonly ILibraryManager _libraryManager;
    private readonly IImageProcessor _imageProcessor;
    private readonly ILogger<ArtworkLoader> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArtworkLoader"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="imageProcessor">The image processor.</param>
    /// <param name="logger">The logger.</param>
    public ArtworkLoader(ILibraryManager libraryManager, IImageProcessor imageProcessor, ILogger<ArtworkLoader> logger)
    {
        _libraryManager = libraryManager;
        _imageProcessor = imageProcessor;
        _logger = logger;
    }

    /// <summary>
    /// Loads an image, resized to fit 600x600.
    /// </summary>
    /// <param name="image">The image.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The image bytes, or null when the image is unavailable.</returns>
    public async Task<byte[]?> LoadAsync(ImageReference image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);

        var item = _libraryManager.GetItemById(image.ItemId);
        var info = item?.GetImageInfo(ImageType.Primary, 0);
        if (item is null || info is null || !info.IsLocalFile)
        {
            return null;
        }

        try
        {
            var options = new ImageProcessingOptions
            {
                Item = item,
                ItemId = item.Id,
                Image = info,
                MaxWidth = MaxSize,
                MaxHeight = MaxSize,
                Quality = 90,
                SupportedOutputFormats = [ImageFormat.Jpg, ImageFormat.Png],
            };
            var (path, _, _) = await _imageProcessor.ProcessImage(options).ConfigureAwait(false);
            return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Could not load artwork of {ItemId}", image.ItemId);
            return null;
        }
    }
}
