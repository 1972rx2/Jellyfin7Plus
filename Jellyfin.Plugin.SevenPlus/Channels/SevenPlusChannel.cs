using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SevenPlus.Services;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;

namespace Jellyfin.Plugin.SevenPlus.Channels;

/// <summary>The browseable 7plus catch-up channel.</summary>
public sealed class SevenPlusChannel : IChannel, IRequiresMediaInfoCallback
{
    private const string ChannelLogoResource = "Jellyfin.Plugin.SevenPlus.Assets.SevenPlusLogo013.png";
    private static readonly (string Id, string Name, string Slug)[] BrowseTopics =
    [
        ("drama", "Drama", "drama"),
        ("reality", "Reality", "reality"),
        ("lifestyle", "Lifestyle", "lifestyle-food-travel"),
        ("news", "News", "news"),
        ("sport", "Sport", "sport"),
        ("movies", "Movies", "movies"),
        ("crime", "Crime", "mystery-crime"),
        ("kids", "Kids", "kids-family"),
    ];

    /// <inheritdoc />
    public string Name => "7plus";

    /// <inheritdoc />
    public string Description => "Australian 7plus catch-up shows. Pair a 7plus account in the plugin dashboard for playback.";

    /// <inheritdoc />
    public string HomePageUrl => "https://7plus.com.au/";

    /// <inheritdoc />
    public string DataVersion => typeof(SevenPlusChannel).Assembly.GetName().Version?.ToString() ?? "1";

    /// <inheritdoc />
    public ChannelParentalRating ParentalRating => ChannelParentalRating.GeneralAudience;

    /// <inheritdoc />
    public bool IsEnabledFor(string userId) => true;

    /// <inheritdoc />
    public InternalChannelFeatures GetChannelFeatures()
    {
        return new InternalChannelFeatures
        {
            ContentTypes = [ChannelMediaContentType.Episode, ChannelMediaContentType.Movie],
            MediaTypes = [ChannelMediaType.Video],
            DefaultSortFields = [ChannelItemSortField.Name],
        };
    }

    /// <inheritdoc />
    public async Task<ChannelItemResult> GetChannelItems(InternalChannelItemQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        string folderId = query.FolderId ?? string.Empty;
        List<ChannelItemInfo> items;

        if (folderId.Length == 0)
        {
            // Put the topic shelves directly at the channel root so browsing starts
            // with catalogue content instead of an extra navigation folder.
            items = BrowseTopics
                .Select(topic => CreateFolder("topic:" + EncodeId(topic.Id), topic.Name))
                .ToList();

            string searchTerm = SevenPlusPlugin.Instance?.Configuration.SearchTerm.Trim() ?? string.Empty;
            if (searchTerm.Length > 0)
            {
                items.Add(CreateFolder("search", "Search results: " + searchTerm));
            }
        }
        else if (string.Equals(folderId, "topics", StringComparison.Ordinal))
        {
            items = BrowseTopics.Select(topic => CreateFolder("topic:" + EncodeId(topic.Id), topic.Name)).ToList();
        }
        else if (folderId.StartsWith("topic:", StringComparison.Ordinal))
        {
            string topicId = DecodeId(folderId["topic:".Length..]);
            (string Id, string Name, string Slug) topic = BrowseTopics.FirstOrDefault(item => item.Id == topicId);
            if (string.IsNullOrWhiteSpace(topic.Id))
            {
                items = [];
            }
            else
            {
                using JsonDocument document = await SevenPlusApiClient.GetShowAsync(topic.Slug, cancellationToken).ConfigureAwait(false);
                items = ParseListingPage(document.RootElement);
            }
        }
        else if (string.Equals(folderId, "search", StringComparison.Ordinal))
        {
            string searchTerm = SevenPlusPlugin.Instance?.Configuration.SearchTerm.Trim() ?? string.Empty;
            items = searchTerm.Length == 0
                ? []
                : await GetSearchItemsAsync(searchTerm, cancellationToken).ConfigureAwait(false);
        }
        else if (folderId.StartsWith("shelf:", StringComparison.Ordinal))
        {
            string sourceUrl = DecodeId(folderId["shelf:".Length..]);
            if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out Uri? sourceUri))
            {
                throw new InvalidOperationException("This 7plus media shelf link is invalid. Reopen the genre page to refresh it.");
            }

            using JsonDocument document = await SevenPlusApiClient.GetComponentAsync(sourceUri, cancellationToken).ConfigureAwait(false);
            items = ParseShelf(document.RootElement);
        }
        else if (folderId.StartsWith("show:", StringComparison.Ordinal))
        {
            string showSlug = DecodeId(folderId["show:".Length..]);
            using JsonDocument document = await SevenPlusApiClient.GetShowAsync(showSlug, cancellationToken).ConfigureAwait(false);
            JsonElement pageItems = GetArray(document.RootElement, "items");
            bool hasSeasonShelf = pageItems.ValueKind == JsonValueKind.Array
                && pageItems.EnumerateArray().Any(item => GetString(item, "type") == "shelfContainer");
            items = hasSeasonShelf
                ? ParseSeasons(showSlug, document.RootElement)
                : ParseListingPage(document.RootElement);
        }
        else if (folderId.StartsWith("season:", StringComparison.Ordinal))
        {
            string[] parts = folderId.Split(':');
            if (parts.Length != 3)
            {
                throw new InvalidOperationException("This 7plus season link is invalid. Reopen the show to refresh it.");
            }

            string showSlug = DecodeId(parts[1]);
            string componentId = DecodeId(parts[2]);
            using JsonDocument document = await SevenPlusApiClient.GetSeasonAsync(showSlug, componentId, cancellationToken).ConfigureAwait(false);
            items = await ParseEpisodesAsync(document.RootElement, cancellationToken).ConfigureAwait(false);
        }
        else if (folderId.StartsWith("video:", StringComparison.Ordinal))
        {
            items = [];
        }
        else
        {
            throw new InvalidOperationException($"Jellyfin requested an unknown 7plus folder identifier. Reopen the 7plus channel after updating the plugin.");
        }

        return new ChannelItemResult { Items = items, TotalRecordCount = items.Count };
    }

    /// <inheritdoc />
    public IEnumerable<ImageType> GetSupportedChannelImages() => [ImageType.Primary];

    /// <inheritdoc />
    public Task<DynamicImageResponse> GetChannelImage(ImageType type, CancellationToken cancellationToken)
    {
        if (type != ImageType.Primary)
        {
            return Task.FromResult(new DynamicImageResponse { HasImage = false, Stream = Stream.Null, Format = ImageFormat.Png });
        }

        Stream? logo = typeof(SevenPlusChannel).Assembly.GetManifestResourceStream(ChannelLogoResource);
        return Task.FromResult(new DynamicImageResponse
        {
            HasImage = logo is not null,
            Stream = logo ?? Stream.Null,
            Format = ImageFormat.Png,
        });
    }

    /// <inheritdoc />
    public async Task<IEnumerable<MediaSourceInfo>> GetChannelItemMediaInfo(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        if (!id.StartsWith("video:", StringComparison.Ordinal))
        {
            throw new ArgumentException("Unsupported 7plus item.", nameof(id));
        }

        PlaybackResult playback = await SevenPlusApiClient.GetPlaybackAsync(DecodeId(id["video:".Length..]), cancellationToken).ConfigureAwait(false);
        if (playback.IsDrmProtected)
        {
            throw new InvalidOperationException("7plus marks this episode as DRM-protected. This plugin does not attempt DRM playback.");
        }

        if (playback.ClearHlsUrl is null)
        {
            throw new InvalidOperationException("7plus did not return a clear HLS stream supported by Jellyfin.");
        }

        return
        [
            new MediaSourceInfo
            {
                Id = id,
                Path = playback.ClearHlsUrl.ToString(),
                Protocol = MediaProtocol.Http,
                Container = "m3u8",
                IsRemote = true,
                SupportsDirectPlay = false,
                SupportsDirectStream = true,
                SupportsTranscoding = true,
                RequiredHttpHeaders = new Dictionary<string, string> { ["User-Agent"] = SevenPlusApiClient.StreamUserAgent },
            },
        ];
    }

    private static async Task<List<ChannelItemInfo>> GetSearchItemsAsync(string searchTerm, CancellationToken cancellationToken)
    {
        using JsonDocument document = await SevenPlusApiClient.SearchAsync(searchTerm, cancellationToken).ConfigureAwait(false);
        JsonElement rows = GetSearchRows(document.RootElement);
        var items = new List<ChannelItemInfo>();
        if (rows.ValueKind != JsonValueKind.Array)
        {
            return items;
        }

        foreach (JsonElement row in rows.EnumerateArray())
        {
            string? title = GetString(row, "title") ?? GetString(GetObject(row, "image"), "altTag");
            string? link = GetString(GetObject(row, "contentLink"), "url");
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(link))
            {
                continue;
            }

            if (!TryParseContentLink(link, out string slug, out string? episodeId))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(episodeId))
            {
                string? overview = GetString(row, "shortSynopsis") ?? GetString(row, "description");
                items.Add(CreateEpisode(episodeId, title, overview, GetImage(row)));
            }
            else
            {
                items.Add(CreateFolder("show:" + EncodeId(slug), title, GetImage(row), GetString(row, "description")));
            }
        }

        return items.GroupBy(item => item.Id, StringComparer.Ordinal).Select(group => group.First()).ToList();
    }

    private static List<ChannelItemInfo> ParseListingPage(JsonElement page)
    {
        JsonElement rows = GetArray(page, "items");
        if (rows.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var items = new List<ChannelItemInfo>();
        foreach (JsonElement row in rows.EnumerateArray())
        {
            string? type = GetString(row, "type");
            if (string.Equals(type, "ctaTop", StringComparison.Ordinal))
            {
                AddContentItem(
                    items,
                    GetString(row, "contentLink"),
                    GetString(row, "title"),
                    GetString(row, "description"),
                    GetImage(GetObject(row, "images"), "primaryImage"));
            }
            else if (string.Equals(type, "mediaShelf", StringComparison.Ordinal))
            {
                string? sourceUrl = GetString(GetObject(row, "source"), "url");
                if (IsSupportedComponentUrl(sourceUrl))
                {
                    string name = GetString(row, "title") ?? "Shows";
                    items.Add(CreateFolder("shelf:" + EncodeId(sourceUrl!), name, GetImage(row), GetString(row, "description")));
                }
                else
                {
                    items.AddRange(ParseShowCards(GetArray(row, "mediaItems")));
                }
            }
            else if (string.Equals(type, "featureShelf", StringComparison.Ordinal))
            {
                JsonElement features = GetArray(row, "items");
                if (features.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (JsonElement feature in features.EnumerateArray())
                {
                    AddContentItem(
                        items,
                        GetFeatureContentLink(feature),
                        GetString(feature, "title"),
                        GetString(feature, "description"),
                        GetImage(feature, "primaryImage"));
                }
            }
        }

        return items.GroupBy(item => item.Id, StringComparer.Ordinal).Select(group => group.First()).ToList();
    }

    private static List<ChannelItemInfo> ParseShowCards(JsonElement cards)
    {
        if (cards.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var items = new List<ChannelItemInfo>();
        foreach (JsonElement card in cards.EnumerateArray())
        {
            string? link = GetString(GetObject(card, "contentLink"), "url");
            string? title = GetString(card, "title") ?? GetString(GetObject(card, "image"), "altTag");
            AddContentItem(items, link, title, GetString(card, "description"), GetImage(card));
        }

        return items.GroupBy(item => item.Id, StringComparer.Ordinal).Select(group => group.First()).ToList();
    }

    private static List<ChannelItemInfo> ParseShelf(JsonElement shelf)
    {
        JsonElement mediaItems = GetArray(shelf, "mediaItems");
        List<ChannelItemInfo> items = ParseShowCards(mediaItems);
        if (items.Count == 0)
        {
            string responseType = GetString(shelf, "type") ?? "unknown";
            string shelfName = GetString(shelf, "title") ?? "unknown";
            throw new InvalidOperationException($"7plus returned no supported shows for the '{shelfName}' shelf (response type: {responseType}, mediaItems: {GetArrayLengthSafe(mediaItems)}). Check the Jellyfin server log and update the plugin cache.");
        }

        return items;
    }

    private static void AddContentItem(List<ChannelItemInfo> items, string? link, string? title, string? overview, string? image)
    {
        if (string.IsNullOrWhiteSpace(link)
            || string.IsNullOrWhiteSpace(title)
            || !TryParseContentLink(link, out string slug, out string? episodeId))
        {
            return;
        }

        items.Add(string.IsNullOrWhiteSpace(episodeId)
            ? CreateFolder("show:" + EncodeId(slug), title, image, overview)
            : CreateEpisode(episodeId, title, overview, image));
    }

    private static string? GetFeatureContentLink(JsonElement feature)
    {
        JsonElement buttons = GetArray(GetObject(feature, "buttonPanel"), "items");
        if (buttons.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        JsonElement[] candidates = buttons.EnumerateArray().ToArray();
        JsonElement infoButton = candidates.FirstOrDefault(button => GetString(button, "text") == "Info");
        string? link = GetString(GetObject(infoButton, "contentLink"), "url");
        if (!string.IsNullOrWhiteSpace(link))
        {
            return link;
        }

        return candidates
            .Select(button => GetString(GetObject(button, "contentLink"), "url"))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value) && !value.StartsWith("app://", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSupportedComponentUrl(string? sourceUrl)
    {
        return Uri.TryCreate(sourceUrl, UriKind.Absolute, out Uri? uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && string.Equals(uri.Host, "component.swm.digital", StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.StartsWith("/v2/component/", StringComparison.Ordinal)
            && string.IsNullOrEmpty(uri.UserInfo)
            && uri.Port == 443;
    }

    private static List<ChannelItemInfo> ParseSeasons(string showSlug, JsonElement page)
    {
        JsonElement items = GetArray(page, "items");
        if (items.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        JsonElement shelf = items.EnumerateArray().FirstOrDefault(item => GetString(item, "type") == "shelfContainer");
        JsonElement shelfItems = GetArray(shelf, "items");
        if (shelfItems.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        JsonElement episodesShelf = shelfItems.EnumerateArray().FirstOrDefault(item => GetString(item, "title") == "Episodes");
        JsonElement episodeShelfItems = GetArray(episodesShelf, "items");
        if (episodeShelfItems.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        JsonElement seasonsContainer = episodeShelfItems.EnumerateArray().FirstOrDefault(item =>
        {
            string? title = GetString(item, "title");
            return title is "Season" or "Year" or "Bulletin";
        });

        var seasons = new List<ChannelItemInfo>();
        JsonElement seasonGroups = GetArray(seasonsContainer, "items");
        if (seasonGroups.ValueKind != JsonValueKind.Array)
        {
            return seasons;
        }

        foreach (JsonElement seasonGroup in seasonGroups.EnumerateArray())
        {
            string? componentId = GetString(GetFirstElement(GetArray(seasonGroup, "items")), "id")
                ?? GetString(seasonGroup, "componentId");
            if (string.IsNullOrWhiteSpace(componentId))
            {
                continue;
            }

            string name = GetString(seasonGroup, "title") ?? "Episodes";
            seasons.Add(CreateFolder("season:" + EncodeId(showSlug) + ":" + EncodeId(componentId), name));
        }

        return seasons;
    }

    private static async Task<List<ChannelItemInfo>> ParseEpisodesAsync(JsonElement response, CancellationToken cancellationToken)
    {
        JsonElement rows = GetArray(response, "mediaItems");
        if (rows.ValueKind != JsonValueKind.Array)
        {
            rows = GetArray(response, "items");
        }

        if (rows.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var episodes = new List<(JsonElement Row, ChannelItemInfo Item, bool HasDrmMetadata)>();
        foreach (JsonElement row in rows.EnumerateArray())
        {
            JsonElement playerData = GetObject(row, "playerData");
            string? videoId = GetString(playerData, "episodePlayerId") ?? GetString(playerData, "videoId");
            JsonElement card = GetObject(row, "cardData");
            JsonElement infoPanel = GetObject(row, "infoPanelData");
            string? title = GetString(playerData, "title")
                ?? GetString(card, "title")
                ?? GetString(infoPanel, "subtitle")
                ?? GetString(infoPanel, "title");
            if (string.IsNullOrWhiteSpace(videoId) || string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            string? overview = GetString(infoPanel, "shortSynopsis") ?? GetString(infoPanel, "description");
            bool isDrm = SevenPlusApiClient.IsDrmMarked(row);
            ChannelItemInfo item = CreateEpisode(videoId, title, overview, GetImage(card));
            if (isDrm)
            {
                MarkDrmProtected(item);
            }

            episodes.Add((row, item, isDrm));
        }

        // Season cards do not consistently expose protection metadata. Ask 7plus's playback
        // metadata endpoint for a yes/no status and discard all returned sources at this stage.
        using var throttle = new SemaphoreSlim(4, 4);
        Task[] checks = episodes.Where(episode => !episode.HasDrmMetadata).Select(async episode =>
        {
            await throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                PlaybackResult playback = await SevenPlusApiClient.GetPlaybackAsync(DecodeId(episode.Item.Id["video:".Length..]), cancellationToken).ConfigureAwait(false);
                if (playback.IsDrmProtected)
                {
                    MarkDrmProtected(episode.Item);
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested
                && ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
            {
                // If 7plus does not provide an assessment, the episode remains available for
                // playback-time checking instead of being incorrectly labelled as DRM.
            }
            finally
            {
                throttle.Release();
            }
        }).ToArray();
        await Task.WhenAll(checks).ConfigureAwait(false);
        return episodes.Select(episode => episode.Item).ToList();
    }

    private static void MarkDrmProtected(ChannelItemInfo item)
    {
        if (!item.Name.StartsWith("[DRM] ", StringComparison.Ordinal))
        {
            item.Name = "[DRM] " + item.Name;
        }

        const string notice = "DRM-protected by 7plus. Jellyfin playback is unavailable; this plugin does not attempt DRM playback.";
        item.Overview = string.IsNullOrWhiteSpace(item.Overview) ? notice : item.Overview + Environment.NewLine + Environment.NewLine + notice;
    }

    private static ChannelItemInfo CreateEpisode(string videoId, string title, string? overview, string? image)
    {
        return new ChannelItemInfo
        {
            Id = "video:" + EncodeId(videoId),
            Name = title,
            Type = ChannelItemType.Media,
            MediaType = ChannelMediaType.Video,
            ContentType = ChannelMediaContentType.Episode,
            ImageUrl = image,
            Overview = overview,
        };
    }

    private static ChannelItemInfo CreateFolder(string id, string name, string? image = null, string? overview = null)
    {
        return new ChannelItemInfo
        {
            Id = id,
            Name = name,
            Type = ChannelItemType.Folder,
            FolderType = ChannelFolderType.Container,
            ImageUrl = image,
            Overview = overview,
        };
    }

    private static JsonElement GetSearchRows(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root;
        }

        JsonElement items = GetArray(root, "items");
        if (items.ValueKind == JsonValueKind.Array)
        {
            return items;
        }

        JsonElement results = GetArray(root, "results");
        if (results.ValueKind == JsonValueKind.Array)
        {
            return results;
        }

        return GetArray(root, "data");
    }

    private static bool TryParseContentLink(string link, out string slug, out string? episodeId)
    {
        slug = string.Empty;
        episodeId = null;
        if (!Uri.TryCreate(link, UriKind.Absolute, out Uri? uri))
        {
            if (!Uri.TryCreate(new Uri("https://7plus.com.au/"), link, out uri))
            {
                return false;
            }
        }

        if (!string.Equals(uri.Host, "7plus.com.au", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Host, "www.7plus.com.au", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        slug = uri.AbsolutePath.Trim('/').Split('/').FirstOrDefault() ?? string.Empty;
        if (slug.Length == 0)
        {
            return false;
        }

        foreach (string parameter in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] pair = parameter.Split('=', 2);
            if (pair.Length == 2 && string.Equals(Uri.UnescapeDataString(pair[0]), "episode-id", StringComparison.OrdinalIgnoreCase))
            {
                episodeId = Uri.UnescapeDataString(pair[1]);
                break;
            }
        }

        return true;
    }

    private static JsonElement GetArray(JsonElement element, string property)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(property, out JsonElement value)
            && value.ValueKind == JsonValueKind.Array
                ? value
                : default;
    }

    private static JsonElement GetFirstElement(JsonElement array)
    {
        return array.ValueKind == JsonValueKind.Array && array.GetArrayLength() > 0 ? array[0] : default;
    }

    private static int GetArrayLengthSafe(JsonElement element)
    {
        return element.ValueKind == JsonValueKind.Array ? element.GetArrayLength() : 0;
    }

    private static JsonElement GetObject(JsonElement element, string property)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(property, out JsonElement value)
            && value.ValueKind == JsonValueKind.Object
                ? value
                : default;
    }

    private static string? GetString(JsonElement element, string property)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(property, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static string? GetImage(JsonElement item, string property = "image")
    {
        JsonElement image = GetObject(item, property);
        string? imageUrl = GetString(image, "url");
        if (item.ValueKind == JsonValueKind.Object
            && item.TryGetProperty(property, out JsonElement imageValue)
            && imageValue.ValueKind == JsonValueKind.String)
        {
            imageUrl = imageValue.GetString();
        }

        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            imageUrl = GetString(item, property + "Url");
        }

        if (imageUrl?.StartsWith("//", StringComparison.Ordinal) == true)
        {
            return "https:" + imageUrl;
        }

        return Uri.TryCreate(imageUrl, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps
            ? uri.ToString()
            : null;
    }

    private static string EncodeId(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string DecodeId(string value)
    {
        string base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + ((4 - base64.Length % 4) % 4), '=');
        return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
    }
}
