# Jellyfin MQTT Plugin

Publishes Jellyfin clients as media players over MQTT, so home automation tools such as Home Assistant can follow and control them without polling.

- Each device of the selected users becomes a persistent player (keyed on its Jellyfin device id).
- State (`off`, `idle`, `playing`, `paused`), now playing metadata, artwork (as image bytes), volume and mute are pushed as soon as Jellyfin reports them.
- Play, pause, stop, next, previous, seek, volume and mute commands are forwarded to the client. Commands run as the server.

Requires Jellyfin 12.

## Installation

In Jellyfin, open **Dashboard → Plugins → Repositories**, add one of these repositories, then install **MQTT** from the catalog:

- Stable releases: `https://lmgarret.github.io/jellyfin-mqtt-plugin/manifest.json`
- Stable releases plus the latest `master` build: `https://lmgarret.github.io/jellyfin-mqtt-plugin/manifest-unstable.json`

## Configuration

In the plugin settings:

- **Broker**: host, port, TLS and credentials. The bridge stays idle until a host is set.
- **Topics**: base topic (default `jellyfin`), and optionally a Jellyfin URL to also publish artwork as a link.
- **Integrations**: the MQTT formats players are published in, see below, each with a link to its consumer's repository and its own settings. Only `mqtt_universal_media_player` exists for now, enabled by default.
- **Exposed players**: no user is exposed by default. Select users to publish their devices, and uncheck devices to hide them. Each user's devices are listed in a sortable table with their client, when they were last seen and what they are playing; the header checkbox selects or clears every shown device.

## Architecture

The core tracks the exposed devices and their sessions, and builds a format-neutral player state (status, metadata, artwork, volume). It hands every change to the enabled **integrations**, and executes the neutral commands (play, pause, seek, volume, …) they receive.

Each integration owns its topics and payloads, so supporting another consumer means adding an `IPlayerIntegration` (see `Jellyfin.Plugin.Mqtt/Integrations/`) without touching the core. Its name, description, repository and settings are listed on the configuration page automatically. Several integrations can be enabled at once, as long as their topics do not overlap.

Shared by every integration:

| Topic | Direction | Payload |
| --- | --- | --- |
| `<base>/status` | out, retained | `online` / `offline` (last will) |

## Integration: `mqtt_universal_media_player`

Home Assistant's MQTT integration has no `media_player` discovery. Install the [MQTT Universal Media Player](https://github.com/grzegorz914/homeassistant-mqtt-media-player) custom integration (HACS custom repository, v0.3.0 or later); players then show up automatically. Its discovery prefix is configurable (default `homeassistant`).

| Topic | Direction | Payload |
| --- | --- | --- |
| `<base>/players/<id>/state` | out, retained | JSON state, see below |
| `<base>/players/<id>/image` | out, retained | Artwork bytes (JPEG/PNG, max 600px), empty when nothing plays |
| `<base>/players/<id>/command` | in | JSON command, see below |
| `<prefix>/media_player/jellyfin_<server>_<id>/config` | out, retained | Discovery |

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

## Releasing

CI (`.github/workflows/ci.yml`) runs tests, `dotnet format`, CodeQL and a `jprm` package build on every pull request and on `master`. Each `master` build is also published as the rolling `edge` prerelease.

To cut a release, push a `vX.Y.Z` tag or run the **Release** workflow. If you run the workflow with no version, the next version is derived from the commits. Either way the workflow builds the plugin, writes the changelog, creates the GitHub release and regenerates the plugin repository on GitHub Pages (`pages.yml`).

Use [Conventional Commits](https://www.conventionalcommits.org/) (`feat:`, `fix:`, …) so version bumps and changelogs come out right.
