# Jellyfin MQTT Plugin

Publishes Jellyfin clients as media players over MQTT, so home automation tools such as Home Assistant can follow and control them without polling.

- Each device of the selected users becomes a persistent player (keyed on its Jellyfin device id).
- State (`off`, `idle`, `playing`, `paused`), now playing metadata, artwork, volume and mute are pushed as soon as Jellyfin reports them.
- Play, pause, stop, next, previous, seek, volume and mute commands are forwarded to the client. Commands run as the server.

Requires Jellyfin 12.

## Configuration

In the plugin settings:

- **Broker**: host, port, TLS and credentials. The bridge stays idle until a host is set.
- **Topics**: base topic (default `jellyfin`), Home Assistant discovery prefix (default `homeassistant`), and the Jellyfin URL used for artwork links.
- **Exposed players**: no user is exposed by default. Select users to publish their devices, and uncheck devices to hide them.

## Home Assistant

Home Assistant's MQTT integration has no `media_player` discovery. Install the [MQTT Universal Media Player](https://github.com/grzegorz914/homeassistant-mqtt-media-player) custom integration (HACS custom repository); players then show up automatically.

## Topics

| Topic | Direction | Payload |
| --- | --- | --- |
| `<base>/status` | out, retained | `online` / `offline` (last will) |
| `<base>/players/<id>/state` | out, retained | JSON state, see below |
| `<base>/players/<id>/command` | in | JSON command, see below |
| `<prefix>/media_player/jellyfin_<server>_<id>/config` | out, retained | Home Assistant discovery |

`<id>` is a hash of the Jellyfin device id.

State:

```json
{
  "state": "playing",
  "volume": 80,
  "muted": false,
  "media_id": "…",
  "media_title": "Pilot",
  "media_artist": null,
  "media_album_name": null,
  "media_series_title": "Some Show",
  "media_season": 1,
  "media_episode": 1,
  "media_content_type": "episode",
  "media_image_url": "http://jellyfin.local:8096/Items/…/Images/Primary?tag=…",
  "media_duration": 2640,
  "app_name": "Jellyfin Android TV",
  "device_name": "Living room TV",
  "user": "alice",
  "media_position": 125
}
```

Every key is always present (`null` when not applicable). Position-only updates are sent at most every 10 seconds.

Commands, several keys may be combined:

| Key | Value |
| --- | --- |
| `play`, `pause`, `play_pause`, `stop`, `next`, `previous` | any |
| `seek` | position in seconds |
| `volume` | 0–100 |
| `mute` | `true` / `false` |

Example: `mosquitto_pub -t jellyfin/players/<id>/command -m '{"pause": true}'`

## Building

```sh
dotnet build Jellyfin.Plugin.Mqtt.slnx
```

Copy `Jellyfin.Plugin.Mqtt.dll` and `MQTTnet.dll` from `Jellyfin.Plugin.Mqtt/bin/Debug/net10.0/` into a `Mqtt` folder in the Jellyfin `plugins` directory.
