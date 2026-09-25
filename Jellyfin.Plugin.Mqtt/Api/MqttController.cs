using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Mqtt.Integrations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Mqtt.Api;

/// <summary>
/// Serves the configuration page.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("Mqtt")]
public class MqttController : ControllerBase
{
    private readonly IEnumerable<IPlayerIntegration> _integrations;

    /// <summary>
    /// Initializes a new instance of the <see cref="MqttController"/> class.
    /// </summary>
    /// <param name="integrations">The available integrations.</param>
    public MqttController(IEnumerable<IPlayerIntegration> integrations)
    {
        _integrations = integrations;
    }

    /// <summary>
    /// Gets the available integrations.
    /// </summary>
    /// <returns>The integrations, by name.</returns>
    [HttpGet("Integrations")]
    public ActionResult<IEnumerable<IntegrationInfo>> GetIntegrations()
    {
        return Ok(_integrations
            .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .Select(i => new IntegrationInfo(i.Id, i.Name, i.Description, i.RepositoryUrl, i.Settings))
            .ToList());
    }
}
