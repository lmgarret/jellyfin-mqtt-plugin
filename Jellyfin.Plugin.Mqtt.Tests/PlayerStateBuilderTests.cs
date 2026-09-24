using System;
using System.Collections.Generic;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Mqtt.Players;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.Mqtt.Tests;

public class PlayerStateBuilderTests
{
    private static readonly ExposedDevice _device = new("device", "key", "Living room", "Jellyfin Web", "12.1.0");

    [Fact]
    public void NoSession_IsOff()
    {
        var state = PlayerStateBuilder.Build(_device, null, "http://jf");

        Assert.Equal(PlayerStatus.Off, state.Status);
        Assert.Equal("Living room", state.DeviceName);
        Assert.Null(state.MediaTitle);
    }

    [Fact]
    public void SessionWithoutItem_IsIdle()
    {
        var session = CreateSession();
        session.PlayState = new PlayerStateInfo { VolumeLevel = 40, IsMuted = true };

        var state = PlayerStateBuilder.Build(_device, session, string.Empty);

        Assert.Equal(PlayerStatus.Idle, state.Status);
        Assert.Equal(40, state.Volume);
        Assert.True(state.Muted);
        Assert.Equal("alice", state.UserName);
    }

    [Fact]
    public void PausedEpisode_UsesSeriesArtworkWhenItemHasNone()
    {
        var seriesId = Guid.NewGuid();
        var session = CreateSession();
        session.PlayState = new PlayerStateInfo { IsPaused = true, PositionTicks = TimeSpan.FromSeconds(90).Ticks };
        session.NowPlayingItem = new BaseItemDto
        {
            Id = Guid.NewGuid(),
            Name = "Pilot",
            Type = BaseItemKind.Episode,
            SeriesName = "Show",
            SeriesId = seriesId,
            SeriesPrimaryImageTag = "tag",
            ParentIndexNumber = 1,
            IndexNumber = 2,
            RunTimeTicks = TimeSpan.FromMinutes(40).Ticks,
            ImageTags = new Dictionary<ImageType, string>(),
        };

        var state = PlayerStateBuilder.Build(_device, session, "http://jf/");

        Assert.Equal(PlayerStatus.Paused, state.Status);
        Assert.Equal("Pilot", state.MediaTitle);
        Assert.Equal("Show", state.MediaSeriesTitle);
        Assert.Equal(1, state.MediaSeason);
        Assert.Equal(2, state.MediaEpisode);
        Assert.Equal("episode", state.MediaContentType);
        Assert.Equal($"http://jf/Items/{seriesId:N}/Images/Primary?tag=tag&maxWidth=600", state.MediaImageUrl);
        Assert.Equal(new ImageReference(seriesId, "tag"), state.MediaImage);
        Assert.Equal(TimeSpan.FromSeconds(90), state.MediaPosition);
        Assert.Null(PlayerStateBuilder.Build(_device, session, string.Empty).MediaImageUrl);
    }

    private static SessionInfo CreateSession() =>
        new(Substitute.For<ISessionManager>(), NullLogger.Instance)
        {
            Id = "session",
            DeviceId = "device",
            Client = "Jellyfin Web",
            UserName = "alice",
        };
}
