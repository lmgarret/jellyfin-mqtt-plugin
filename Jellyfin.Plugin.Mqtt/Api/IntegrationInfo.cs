using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Mqtt.Integrations;

namespace Jellyfin.Plugin.Mqtt.Api;

/// <summary>
/// An integration, as listed on the configuration page.
/// </summary>
/// <param name="Id">The integration id.</param>
/// <param name="Name">The display name.</param>
/// <param name="Description">The description.</param>
/// <param name="RepositoryUrl">The repository of the consumer.</param>
/// <param name="Settings">The configuration settings used by the integration.</param>
public sealed record IntegrationInfo(string Id, string Name, string Description, Uri RepositoryUrl, IReadOnlyList<IntegrationSetting> Settings);
