namespace Jellyfin.Plugin.Mqtt.Integrations;

/// <summary>
/// A text setting of an integration, stored as a <see cref="Configuration.PluginConfiguration"/> property.
/// </summary>
/// <param name="Key">The name of the configuration property.</param>
/// <param name="Label">The label shown on the configuration page.</param>
/// <param name="Description">The description shown below the input, if any.</param>
public sealed record IntegrationSetting(string Key, string Label, string? Description);
