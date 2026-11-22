using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SevenPlus.Configuration;

namespace Jellyfin.Plugin.SevenPlus.Services;

/// <summary>HTTP client for 7plus catalogue, account pairing, and clear-stream metadata.</summary>
public static class SevenPlusApiClient
{
    private const string SearchUrl = "https://searchapi.swm.digital/3.0/api/Search";
    private const string ContentUrl = "https://component-cdn.swm.digital/content";
    private const string ComponentUrl = "https://component.swm.digital/v2/component";
    private const string PlaybackUrl = "https://videoservice.swm.digital/playback";
    private const string MarketUrl = "https://market-cdn.swm.digital/v1/market/ip/";
    private const string AuthorizationUrl = "https://auth2.swm.digital/account/device/authorize";
    private const string TokenUrl = "https://auth2.swm.digital/connect/token";
    private const string ActivationUrl = "https://7plus.com.au/connect";
    private const string PlatformId = "androidtv";
    private const string PlatformVersion = "8.0";
    private const string ApiVersion = "4.11";
    private const string ComponentPlatformId = "AndroidTv";
    private const string ComponentPlatformVersion = "8.0.0.0";
    private const string ComponentApiVersion = "4.11.0.0";
    private const string AuthApiVersion = "1.1";
    private const string VodAccountId = "5303576322001";
    private const string VodOzid = "dc6095c7-e895-41d3-6609-79f673fc7f63";
    private const string UserAgent = "com.swm.live/8.0.1 (Android TV)";

    /// <summary>The user agent attached to provider HLS requests.</summary>
    public const string StreamUserAgent = UserAgent;

    private static readonly HttpClient Client = CreateClient();
    private static readonly SemaphoreSlim TokenLock = new(1, 1);
    private static readonly SemaphoreSlim DevicePollLock = new(1, 1);
    private static readonly object PendingLoginLock = new();
    private static PendingDeviceLogin? _pendingLogin;

    /// <summary>Searches the 7plus catalogue for shows.</summary>
    public static async Task<JsonDocument> SearchAsync(string searchTerm, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(searchTerm);
        PluginConfiguration configuration = GetConfiguration();
        int marketId = await GetMarketIdAsync(configuration, cancellationToken).ConfigureAwait(false);
        string url = BuildUrl(SearchUrl,
            ("searchTerm", searchTerm.Trim()),
            ("platform-id", PlatformId),
            ("market-id", marketId.ToString(CultureInfo.InvariantCulture)),
            ("platform-version", PlatformVersion),
            ("api-version", ApiVersion));
        return await GetCatalogJsonAsync(url, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Gets the 7plus content page for a show slug.</summary>
    public static async Task<JsonDocument> GetShowAsync(string slug, CancellationToken cancellationToken)
    {
        PluginConfiguration configuration = GetConfiguration();
        int marketId = await GetMarketIdAsync(configuration, cancellationToken).ConfigureAwait(false);
        string url = BuildUrl(ContentUrl + "/" + Uri.EscapeDataString(slug),
            ("platform-id", PlatformId),
            ("market-id", marketId.ToString(CultureInfo.InvariantCulture)),
            ("platform-version", PlatformVersion),
            ("api-version", ApiVersion));
        return await GetCatalogJsonAsync(url, cancellationToken, ("market-id", marketId.ToString(CultureInfo.InvariantCulture))).ConfigureAwait(false);
    }

    /// <summary>Gets the episodes attached to a show season component.</summary>
    public static async Task<JsonDocument> GetSeasonAsync(string showSlug, string componentId, CancellationToken cancellationToken)
    {
        PluginConfiguration configuration = GetConfiguration();
        int marketId = await GetMarketIdAsync(configuration, cancellationToken).ConfigureAwait(false);
        string url = BuildUrl(ComponentUrl + "/" + Uri.EscapeDataString(showSlug),
            ("component-id", componentId),
            ("platform-id", ComponentPlatformId),
            ("market-id", marketId.ToString(CultureInfo.InvariantCulture)),
            ("platform-version", ComponentPlatformVersion),
            ("api-version", ComponentApiVersion),
            ("signedUp", string.IsNullOrWhiteSpace(configuration.AccessToken) ? "False" : "True"));
        return await GetCatalogJsonAsync(url, cancellationToken, ("market-id", marketId.ToString(CultureInfo.InvariantCulture))).ConfigureAwait(false);
    }

    /// <summary>Gets a 7plus media shelf using its provider-supplied component URL.</summary>
    public static async Task<JsonDocument> GetComponentAsync(Uri uri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.Host, "component.swm.digital", StringComparison.OrdinalIgnoreCase)
            || !uri.AbsolutePath.StartsWith("/v2/component/", StringComparison.Ordinal)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || uri.Port != 443)
        {
            throw new InvalidOperationException("7plus returned an unsupported media shelf URL.");
        }

        PluginConfiguration configuration = GetConfiguration();
        int marketId = await GetMarketIdAsync(configuration, cancellationToken).ConfigureAwait(false);
        // Catalogue components are public and the provider marks these URLs signedUp=False.
        // Sending the playback account token here can cause account-specific shelf responses
        // and is not needed to browse the catalogue.
        return await GetJsonCoreAsync(
            uri.AbsoluteUri,
            includeAccountToken: false,
            [("market-id", marketId.ToString(CultureInfo.InvariantCulture))],
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Requests playback metadata and returns only a clear HTTPS HLS source.</summary>
    public static async Task<PlaybackResult> GetPlaybackAsync(string videoId, CancellationToken cancellationToken)
    {
        PluginConfiguration configuration = GetConfiguration();
        int marketId = await GetMarketIdAsync(configuration, cancellationToken).ConfigureAwait(false);
        await EnsureFreshTokenAsync(configuration, cancellationToken).ConfigureAwait(false);
        string deviceId = string.IsNullOrWhiteSpace(configuration.DeviceId) ? Guid.NewGuid().ToString("D") : configuration.DeviceId;
        configuration.DeviceId = deviceId;
        string url = BuildUrl(PlaybackUrl,
            ("appId", "7plus"),
            ("deviceType", PlatformId),
            ("platformType", "tv"),
            ("deviceId", deviceId),
            ("pc", "3181"),
            ("advertid", "null"),
            ("accountId", VodAccountId),
            ("referenceId", "ref:" + videoId),
            ("deliveryId", "csai"),
            ("videoType", "vod"),
            ("marketId", marketId.ToString(CultureInfo.InvariantCulture)),
            ("ozid", VodOzid),
            ("sdkverification", "true"));

        using JsonDocument document = await GetPlaybackJsonAsync(url, cancellationToken).ConfigureAwait(false);
        JsonElement media = document.RootElement.TryGetProperty("media", out JsonElement foundMedia) ? foundMedia : default;
        return EvaluatePlaybackMedia(media);
    }

    /// <summary>Classifies a playback metadata object and selects only a clear HTTPS HLS source.</summary>
    internal static PlaybackResult EvaluatePlaybackMedia(JsonElement media)
    {
        if (media.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("7plus did not return playback metadata for this episode.");
        }

        if (IsTruthyDrmFlag(media, "stream_type_drm") || IsTruthyDrmFlag(media, "drm") || IsTruthyDrmFlag(media, "isDrmProtected"))
        {
            return new PlaybackResult(true, null);
        }

        bool sawDrmSource = false;
        if (media.TryGetProperty("sources", out JsonElement sources) && sources.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement source in sources.EnumerateArray())
            {
                bool isDrmSource = HasDrmSystems(source);
                sawDrmSource |= isDrmSource;
                if (isDrmSource)
                {
                    continue;
                }

                string? sourceUrl = GetString(source, "src");
                string? sourceType = GetString(source, "type");
                bool isHls = string.Equals(sourceType, "application/vnd.apple.mpegurl", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(sourceType, "application/x-mpegURL", StringComparison.OrdinalIgnoreCase)
                    || (sourceUrl?.Contains(".m3u8", StringComparison.OrdinalIgnoreCase) ?? false);
                if (isHls && Uri.TryCreate(sourceUrl, UriKind.Absolute, out Uri? sourceUri) && sourceUri.Scheme == Uri.UriSchemeHttps)
                {
                    return new PlaybackResult(false, sourceUri);
                }
            }
        }

        if (sawDrmSource)
        {
            return new PlaybackResult(true, null);
        }

        throw new InvalidOperationException("7plus did not return a clear HLS stream supported by Jellyfin.");
    }

    /// <summary>Starts 7plus account pairing without requesting the user's password.</summary>
    public static async Task<DeviceLoginStart> StartDeviceLoginAsync(CancellationToken cancellationToken)
    {
        PluginConfiguration configuration = GetConfiguration();
        string deviceId = Guid.NewGuid().ToString("D");
        configuration.DeviceId = deviceId;
        var form = new Dictionary<string, string>
        {
            ["platformId"] = PlatformId,
            ["regSource"] = "7plus",
            ["deviceId"] = deviceId,
            ["locationVerificationRequired"] = "true",
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, AuthorizationUrl)
        {
            Content = new FormUrlEncodedContent(form),
        };
        AddProviderHeaders(request, null);
        using HttpResponseMessage response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using JsonDocument document = await ParseJsonAsync(response, cancellationToken).ConfigureAwait(false);
        JsonElement root = document.RootElement;
        string? userCode = GetString(root, "user_code");
        string? deviceCode = GetString(root, "device_code");
        if (string.IsNullOrWhiteSpace(userCode) || string.IsNullOrWhiteSpace(deviceCode))
        {
            throw new InvalidOperationException("7plus did not return an account-pairing code.");
        }

        int expirySeconds = GetInt32(root, "expires_in", 600);
        int pollIntervalSeconds = Math.Clamp(GetInt32(root, "interval", 5), 3, 30);
        long expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(expirySeconds, 60)).ToUnixTimeSeconds();
        lock (PendingLoginLock)
        {
            _pendingLogin = new PendingDeviceLogin(deviceCode, deviceId, expiresAt, pollIntervalSeconds);
        }

        return new DeviceLoginStart(userCode, new Uri(ActivationUrl), expiresAt, pollIntervalSeconds);
    }

    /// <summary>Polls 7plus after the user enters the pairing code at the official connect page.</summary>
    public static async Task<DeviceLoginCompletion> CompleteDeviceLoginAsync(CancellationToken cancellationToken)
    {
        await DevicePollLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            PendingDeviceLogin? pending;
            lock (PendingLoginLock)
            {
                pending = _pendingLogin;
            }

            if (pending is null)
            {
                return new DeviceLoginCompletion(false, "Start a new 7plus sign-in to get a pairing code.");
            }

            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= pending.ExpiresAtUnixSeconds)
            {
                lock (PendingLoginLock)
                {
                    _pendingLogin = null;
                }

                return new DeviceLoginCompletion(false, "The pairing code expired. Start a new sign-in.");
            }

            var form = new Dictionary<string, string>
            {
                ["platformId"] = PlatformId,
                ["regSource"] = "7plus",
                ["deviceCode"] = pending.DeviceCode,
                ["deviceId"] = pending.DeviceId,
            };
            string url = BuildUrl(TokenUrl, ("api-version", AuthApiVersion));
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new FormUrlEncodedContent(form),
            };
            AddProviderHeaders(request, null);
            using HttpResponseMessage response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            using JsonDocument document = await ParseJsonAsync(response, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.BadRequest && IsPendingAuthorization(document.RootElement))
            {
                return new DeviceLoginCompletion(false, null);
            }

            response.EnsureSuccessStatusCode();
            PluginConfiguration configuration = GetConfiguration();
            SaveTokens(configuration, pending.DeviceId, document.RootElement);
            lock (PendingLoginLock)
            {
                _pendingLogin = null;
            }

            SevenPlusPlugin.Instance!.SaveConfiguration(configuration);
            return new DeviceLoginCompletion(true, null);
        }
        finally
        {
            DevicePollLock.Release();
        }
    }

    /// <summary>Removes saved 7plus tokens from local plugin configuration.</summary>
    public static void SignOut()
    {
        PluginConfiguration configuration = GetConfiguration();
        configuration.AccessToken = string.Empty;
        configuration.RefreshToken = string.Empty;
        configuration.TokenExpiresAtUnixSeconds = 0;
        SevenPlusPlugin.Instance!.SaveConfiguration(configuration);
        lock (PendingLoginLock)
        {
            _pendingLogin = null;
        }
    }

    private static async Task EnsureFreshTokenAsync(PluginConfiguration configuration, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(configuration.AccessToken)
            && string.IsNullOrWhiteSpace(configuration.RefreshToken))
        {
            return;
        }

        if (configuration.TokenExpiresAtUnixSeconds > DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds())
        {
            return;
        }

        await TokenLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (configuration.TokenExpiresAtUnixSeconds > DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds())
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(configuration.RefreshToken))
            {
                throw new InvalidOperationException("The saved 7plus sign-in has expired. Sign in again from the Jellyfin dashboard.");
            }

            string url = BuildUrl(TokenUrl, ("api-version", AuthApiVersion));
            var form = new Dictionary<string, string>
            {
                ["platformId"] = PlatformId,
                ["regSource"] = "7plus",
                ["refreshToken"] = configuration.RefreshToken,
                ["deviceId"] = configuration.DeviceId,
            };
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new FormUrlEncodedContent(form),
            };
            AddProviderHeaders(request, null);
            using HttpResponseMessage response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using JsonDocument document = await ParseJsonAsync(response, cancellationToken).ConfigureAwait(false);
            SaveTokens(configuration, configuration.DeviceId, document.RootElement, configuration.RefreshToken);
            SevenPlusPlugin.Instance!.SaveConfiguration(configuration);
        }
        finally
        {
            TokenLock.Release();
        }
    }

    private static void SaveTokens(PluginConfiguration configuration, string deviceId, JsonElement token, string? previousRefreshToken = null)
    {
        string? accessToken = GetString(token, "access_token");
        string? refreshToken = GetString(token, "refresh_token");
        int expiresIn = GetInt32(token, "expires_in", 0);
        if (string.IsNullOrWhiteSpace(accessToken) || expiresIn <= 0)
        {
            throw new InvalidOperationException("7plus returned an invalid account token response.");
        }

        configuration.AccessToken = accessToken;
        configuration.RefreshToken = string.IsNullOrWhiteSpace(refreshToken) ? previousRefreshToken ?? string.Empty : refreshToken;
        configuration.TokenExpiresAtUnixSeconds = DateTimeOffset.UtcNow.AddSeconds(Math.Max(expiresIn, 60) - 30).ToUnixTimeSeconds();
        configuration.DeviceId = deviceId;
    }

    private static async Task<int> GetMarketIdAsync(PluginConfiguration configuration, CancellationToken cancellationToken)
    {
        if (configuration.MarketId > 0)
        {
            return configuration.MarketId;
        }

        string url = BuildUrl(MarketUrl, ("apikey", "web"));
        try
        {
            using JsonDocument document = await GetCatalogJsonAsync(url, cancellationToken).ConfigureAwait(false);
            int marketId = GetInt32(document.RootElement, "_id", 4);
            configuration.MarketId = marketId > 0 ? marketId : 4;
            SevenPlusPlugin.Instance?.SaveConfiguration(configuration);
        }
        catch (HttpRequestException)
        {
            configuration.MarketId = 4;
        }

        return configuration.MarketId;
    }

    private static Task<JsonDocument> GetCatalogJsonAsync(string url, CancellationToken cancellationToken, params (string Name, string Value)[] extraHeaders)
    {
        return GetJsonCoreAsync(url, includeAccountToken: false, extraHeaders, cancellationToken);
    }

    private static Task<JsonDocument> GetPlaybackJsonAsync(string url, CancellationToken cancellationToken, params (string Name, string Value)[] extraHeaders)
    {
        return GetJsonCoreAsync(url, includeAccountToken: true, extraHeaders, cancellationToken);
    }

    private static async Task<JsonDocument> GetJsonCoreAsync(
        string url,
        bool includeAccountToken,
        (string Name, string Value)[] extraHeaders,
        CancellationToken cancellationToken)
    {
        PluginConfiguration configuration = GetConfiguration();
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        AddProviderHeaders(request, includeAccountToken ? configuration : null);
        foreach ((string name, string value) in extraHeaders)
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        using HttpResponseMessage response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        JsonDocument document = await ParseJsonAsync(response, cancellationToken).ConfigureAwait(false);
        ThrowIfProviderError(document.RootElement);
        return document;
    }

    private static async Task<JsonDocument> ParseJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static void AddProviderHeaders(HttpRequestMessage request, PluginConfiguration? configuration)
    {
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        string? applicationKey = (configuration ?? SevenPlusPlugin.Instance?.Configuration)?.ProviderApiKey;
        if (!string.IsNullOrWhiteSpace(applicationKey))
        {
            request.Headers.TryAddWithoutValidation("x-swm-apikey", applicationKey);
        }
        if (!string.IsNullOrWhiteSpace(configuration?.AccessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuration.AccessToken);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "HttpClient owns and disposes its handler for the plugin lifetime.")]
    private static HttpClient CreateClient()
    {
        return new HttpClient(new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            UseCookies = true,
            CheckCertificateRevocationList = true,
        }, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(15),
        };
    }

    private static PluginConfiguration GetConfiguration()
    {
        return SevenPlusPlugin.Instance?.Configuration
            ?? throw new InvalidOperationException("The 7plus plugin has not been initialized.");
    }

    private static string BuildUrl(string baseUrl, params (string Name, string Value)[] query)
    {
        string encoded = string.Join("&", query.Select(pair => Uri.EscapeDataString(pair.Name) + "=" + Uri.EscapeDataString(pair.Value)));
        return baseUrl + (baseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?") + encoded;
    }

    private static bool IsTruthyDrmFlag(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out JsonElement value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.Number => value.TryGetInt32(out int number) && number != 0,
            JsonValueKind.String => !string.IsNullOrWhiteSpace(value.GetString())
                && !string.Equals(value.GetString(), "false", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(value.GetString(), "clear", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(value.GetString(), "none", StringComparison.OrdinalIgnoreCase),
            JsonValueKind.Object => value.EnumerateObject().Any(),
            JsonValueKind.Array => value.GetArrayLength() > 0,
            _ => false,
        };
    }

    /// <summary>Reads DRM flags exposed on a catalogue episode or its nested player metadata.</summary>
    internal static bool IsDrmMarked(JsonElement item)
    {
        return HasDrmFlag(item)
            || HasDrmFlag(GetObject(item, "playerData"))
            || HasDrmFlag(GetObject(item, "videoMetadata"))
            || HasDrmFlag(GetObject(item, "video"));
    }

    private static bool HasDrmFlag(JsonElement element)
    {
        return IsTruthyDrmFlag(element, "drm")
            || IsTruthyDrmFlag(element, "isDrmProtected")
            || IsTruthyDrmFlag(element, "drmProtected")
            || IsTruthyDrmFlag(element, "stream_type_drm");
    }

    private static JsonElement GetObject(JsonElement element, string property)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(property, out JsonElement value)
            && value.ValueKind == JsonValueKind.Object
                ? value
                : default;
    }

    private static bool HasDrmSystems(JsonElement source)
    {
        if (source.ValueKind != JsonValueKind.Object || !source.TryGetProperty("key_systems", out JsonElement systems))
        {
            return false;
        }

        return systems.ValueKind switch
        {
            JsonValueKind.Object => systems.EnumerateObject().Any(),
            JsonValueKind.Array => systems.GetArrayLength() > 0,
            JsonValueKind.True => true,
            JsonValueKind.String => !string.IsNullOrWhiteSpace(systems.GetString()),
            _ => false,
        };
    }

    private static bool IsPendingAuthorization(JsonElement root)
    {
        string detail = GetString(root, "error") ?? GetString(root, "error_description") ?? GetString(root, "message") ?? string.Empty;
        return detail.Contains("pending", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("slow_down", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("authorization_pending", StringComparison.OrdinalIgnoreCase);
    }

    private static void ThrowIfProviderError(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("errors", out JsonElement errors)
            && errors.ValueKind == JsonValueKind.Array
            && errors.GetArrayLength() > 0)
        {
            string? message = GetString(errors[0], "message");
            throw new InvalidOperationException(message ?? "7plus returned an API error.");
        }
    }

    private static string? GetString(JsonElement element, string property)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(property, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static int GetInt32(JsonElement element, string property, int fallback)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out JsonElement value))
        {
            return fallback;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String
            && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
                ? number
                : fallback;
    }

    private sealed record PendingDeviceLogin(string DeviceCode, string DeviceId, long ExpiresAtUnixSeconds, int PollIntervalSeconds);
}

/// <summary>A provider playback assessment, containing no DRM licence data.</summary>
public sealed record PlaybackResult(bool IsDrmProtected, Uri? ClearHlsUrl);

/// <summary>A device pairing request shown in the Jellyfin dashboard.</summary>
public sealed record DeviceLoginStart(string UserCode, Uri ActivationUrl, long ExpiresAtUnixSeconds, int PollIntervalSeconds);

/// <summary>The outcome of a device-code sign-in poll.</summary>
public sealed record DeviceLoginCompletion(bool Complete, string? Detail);
