using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SevenPlus.Services;
using Jellyfin.Plugin.SevenPlus.Channels;
using MediaBrowser.Controller.Channels;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<SevenPlusChannel> _logger;

    /// <summary>Initializes dashboard actions with the channel logger.</summary>
    public SevenPlusAuthController(ILogger<SevenPlusChannel> logger)
    {
        _logger = logger;
    }

    /// <summary>Tests live catalogue navigation and refreshes only this plugin's cache revision.</summary>
    [HttpPost("catalogue-check")]
    public async Task<ActionResult<CatalogueCheckResult>> CheckCatalogue(CancellationToken cancellationToken)
    {
        SevenPlusPlugin? plugin = SevenPlusPlugin.Instance;
        if (plugin is null)
        {
            return Problem("The 7plus plugin is not initialized.", statusCode: 503);
        }

        var channel = new SevenPlusChannel(_logger);
        var steps = new List<string>();
        string stage = "Drama genre";
        try
        {
            var root = await channel.GetChannelItems(new InternalChannelItemQuery(), cancellationToken).ConfigureAwait(false);
            var drama = root.Items.First(item => item.Name == "Drama");
            var shelves = await channel.GetChannelItems(new InternalChannelItemQuery { FolderId = drama.Id }, cancellationToken).ConfigureAwait(false);
            steps.Add($"Drama: {shelves.Items.Count} entries");
            stage = "Aussie Drama shelf";
            var shelf = shelves.Items.First(item => item.Name == "Aussie Drama");
            var shows = await channel.GetChannelItems(new InternalChannelItemQuery { FolderId = shelf.Id }, cancellationToken).ConfigureAwait(false);
            steps.Add($"Aussie Drama: {shows.Items.Count} shows");
            var show = shows.Items[0];
            stage = "Show seasons";
            var seasons = await channel.GetChannelItems(new InternalChannelItemQuery { FolderId = show.Id }, cancellationToken).ConfigureAwait(false);
            if (seasons.Items.Count == 0)
            {
                throw new InvalidOperationException("7plus returned no seasons for " + show.Name + ".");
            }

            steps.Add($"{show.Name}: {seasons.Items.Count} seasons");
            stage = "Season episodes";
            var episodes = await channel.GetChannelItems(new InternalChannelItemQuery { FolderId = seasons.Items[0].Id }, cancellationToken).ConfigureAwait(false);
            steps.Add($"{seasons.Items[0].Name}: {episodes.Items.Count} episodes");
            plugin.Configuration.CatalogueCacheRevision = Guid.NewGuid().ToString("N");
            plugin.SaveConfiguration(plugin.Configuration);
            return Ok(new CatalogueCheckResult(true, plugin.Version.ToString(), plugin.Configuration.MarketId, steps, "Catalogue loaded. Reopen 7plus from its root to use the refreshed cache."));
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested
            && ex is HttpRequestException or InvalidOperationException or JsonException or FormatException or TaskCanceledException)
        {
            string error = stage + ": " + ex.GetType().Name + ": " + ex.Message;
            if (ex.InnerException is not null)
            {
                error += " " + ex.GetBaseException().Message;
            }

            return Ok(new CatalogueCheckResult(false, plugin.Version.ToString(), plugin.Configuration.MarketId, steps, error));
        }
    }

    /// <summary>Gets the local sign-in state without exposing account tokens.</summary>
    [HttpGet("status")]
    public ActionResult<LoginSessionStatus> Status()
    {
        SevenPlusPlugin? plugin = SevenPlusPlugin.Instance;
        if (plugin is null)
        {
            return Problem("The 7plus plugin is not initialized.", statusCode: 503);
        }

        return Ok(LoginSessionStatus.FromSavedSession(plugin.Configuration));
    }

    /// <summary>Asks 7plus to refresh the saved session before reporting it as verified.</summary>
    [HttpPost("validate")]
    public async Task<ActionResult<LoginSessionStatus>> Validate(CancellationToken cancellationToken)
    {
        SevenPlusPlugin? plugin = SevenPlusPlugin.Instance;
        if (plugin is null)
        {
            return Problem("The 7plus plugin is not initialized.", statusCode: 503);
        }

        return Ok(await SevenPlusApiClient.ValidateLoginAsync(plugin.Configuration, cancellationToken).ConfigureAwait(false));
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
        return Ok(new LoginStatus(completion.Complete, completion.Detail));
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

/// <summary>Safe catalogue diagnostics without account tokens or media URLs.</summary>
public sealed record CatalogueCheckResult(bool Success, string Version, int MarketId, IReadOnlyList<string> Steps, string Detail);
