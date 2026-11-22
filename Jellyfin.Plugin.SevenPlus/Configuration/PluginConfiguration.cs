using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.SevenPlus.Configuration;

/// <summary>Stores the local 7plus account session and Australian market.</summary>
public sealed class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Gets or sets an optional locally configured provider application key.</summary>
    public string ProviderApiKey { get; set; } = string.Empty;

    /// <summary>Gets or sets the cache revision advanced by a successful catalogue check.</summary>
    public string CatalogueCacheRevision { get; set; } = string.Empty;

    /// <summary>Gets or sets the access token issued after account pairing.</summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>Gets or sets the refresh token issued after account pairing.</summary>
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>Gets or sets the access-token expiry as Unix time in seconds.</summary>
    public long TokenExpiresAtUnixSeconds { get; set; }

    /// <summary>Gets or sets the provider device identifier tied to the account session.</summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>Gets or sets the market identifier used for catalogue access.</summary>
    public int MarketId { get; set; }

    /// <summary>Gets or sets the catalogue search term shown in the channel.</summary>
    public string SearchTerm { get; set; } = string.Empty;
}
