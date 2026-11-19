using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SevenPlus.Services;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.SevenPlus.Controllers;

/// <summary>Provides elevated dashboard actions for official 7plus device pairing.</summary>
[ApiController]
[Route("SevenPlus/Auth")]
[Authorize(Policy = Policies.RequiresElevation)]
public sealed class SevenPlusAuthController : ControllerBase
{
    /// <summary>Gets the local sign-in state without exposing account tokens.</summary>
    [HttpGet("status")]
    public ActionResult<LoginStatus> Status()
    {
        SevenPlusPlugin? plugin = SevenPlusPlugin.Instance;
        if (plugin is null)
        {
            return Problem("The 7plus plugin is not initialized.", statusCode: 503);
        }

        return Ok(new LoginStatus(!string.IsNullOrWhiteSpace(plugin.Configuration.AccessToken)));
    }

    /// <summary>Starts an official 7plus device-code sign-in.</summary>
    [HttpPost("start")]
    public async Task<ActionResult<DeviceLoginStart>> Start(CancellationToken cancellationToken)
    {
        if (SevenPlusPlugin.Instance is null)
        {
            return Problem("The 7plus plugin is not initialized.", statusCode: 503);
        }

        return Ok(await SevenPlusApiClient.StartDeviceLoginAsync(cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Polls 7plus until the user approves the pairing code.</summary>
    [HttpPost("complete")]
    public async Task<ActionResult<LoginStatus>> Complete(CancellationToken cancellationToken)
    {
        SevenPlusPlugin? plugin = SevenPlusPlugin.Instance;
        if (plugin is null)
        {
            return Problem("The 7plus plugin is not initialized.", statusCode: 503);
        }

        DeviceLoginCompletion completion = await SevenPlusApiClient.CompleteDeviceLoginAsync(cancellationToken).ConfigureAwait(false);
        return Ok(new LoginStatus(completion.Complete || !string.IsNullOrWhiteSpace(plugin.Configuration.AccessToken), completion.Detail));
    }

    /// <summary>Removes the saved 7plus account session.</summary>
    [HttpPost("logout")]
    public ActionResult<LoginStatus> Logout()
    {
        if (SevenPlusPlugin.Instance is null)
        {
            return Problem("The 7plus plugin is not initialized.", statusCode: 503);
        }

        SevenPlusApiClient.SignOut();
        return Ok(new LoginStatus(false));
    }
}

/// <summary>Whether a 7plus account session is saved.</summary>
public sealed record LoginStatus(bool Complete, string? Detail = null);

