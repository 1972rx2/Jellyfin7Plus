using Jellyfin.Plugin.SevenPlus.Channels;
using System.Reflection;
using System.Text.Json;
using Jellyfin.Plugin.SevenPlus.Services;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Model.Channels;

int passed = 0;
Run("channel root lists genres directly", () =>
{
    var channel = new SevenPlusChannel();
    ChannelItemResult root = channel.GetChannelItems(new InternalChannelItemQuery(), CancellationToken.None).GetAwaiter().GetResult();
    string[] names = root.Items.Select(item => item.Name).ToArray();
    Assert(names.Contains("Drama") && names.Contains("Reality") && names.Contains("Kids"), "Expected genre shelves at the channel root.");
    Assert(!names.Contains("Browse shows by topic"), "The extra topic navigation folder should not be present.");
});

Run("7plus listing pages expose featured shows and shelf folders", () =>
{
    using JsonDocument page = JsonDocument.Parse("""
        {"type":"listingPage","items":[
          {"type":"ctaTop","title":"The Rookie","contentLink":"/the-rookie","description":"A police drama.","images":{"primaryImage":"https://images.example.test/rookie.jpg"}},
          {"type":"mediaShelf","title":"Recently Added","source":{"url":"https://component.swm.digital/v2/component/drama?component-id=123"}},
          {"type":"featureShelf","items":[{"type":"featureCTA","title":"The Reunion","description":"A drama.","primaryImage":{"url":"https://images.example.test/reunion.jpg"},"buttonPanel":{"items":[{"text":"Play","contentLink":{"url":"/the-reunion?autoplay=true"}},{"text":"Info","contentLink":{"url":"/the-reunion"}}]}}]}
        ]}
        """);
    List<ChannelItemInfo> items = InvokeListParser("ParseListingPage", page.RootElement);
    Assert(items.Any(item => item.Name == "The Rookie" && item.Id.StartsWith("show:", StringComparison.Ordinal)), "Expected the featured show folder.");
    Assert(items.Any(item => item.Name == "Recently Added" && item.Id.StartsWith("shelf:", StringComparison.Ordinal)), "Expected a fetchable shelf folder.");
    Assert(items.Any(item => item.Name == "The Reunion" && item.Id.StartsWith("show:", StringComparison.Ordinal)), "Expected the featured show card.");
});

Run("v2 media shelves expose show cards", () =>
{
    using JsonDocument shelf = JsonDocument.Parse("""
        {"type":"mediaShelf","mediaItems":[{"type":"SeriesCard","title":"Winter","description":"Mystery drama.","contentLink":{"url":"/winter"},"image":{"url":"https://images.example.test/winter.jpg"}}]}
        """);
    List<ChannelItemInfo> items = InvokeListParser("ParseShowCards", shelf.RootElement.GetProperty("mediaItems"));
    Assert(items.Count == 1 && items[0].Name == "Winter" && items[0].Id.StartsWith("show:", StringComparison.Ordinal), "Expected one show folder from the v2 shelf.");
});

Run("v2 episode cards use the episode title", () =>
{
    using JsonDocument shelf = JsonDocument.Parse("""
        {"type":"mediaShelf","mediaItems":[{"type":"EpisodeContainer","playerData":{"title":"S8 E18 - The Bandit","episodePlayerId":"ROKE08-018"},"infoPanelData":{"title":"The Rookie","subtitle":"S8 E18 - The Bandit","shortSynopsis":"A high-stakes search."},"cardData":{"seriesTitle":"The Rookie","image":{"url":"https://images.example.test/episode.jpg"}}}]}
        """);
    List<ChannelItemInfo> items = InvokeEpisodeParser(shelf.RootElement);
    Assert(items.Count == 1 && items[0].Name == "S8 E18 - The Bandit" && items[0].Id.StartsWith("video:", StringComparison.Ordinal), "Expected a named episode media item.");
});

Run("clear HLS source is selected", () =>
{
    using JsonDocument media = JsonDocument.Parse("""
        {"sources":[{"type":"application/vnd.apple.mpegurl","src":"https://cdn.example.test/show/master.m3u8"}]}
        """);
    PlaybackResult result = SevenPlusApiClient.EvaluatePlaybackMedia(media.RootElement);
    Assert(!result.IsDrmProtected && result.ClearHlsUrl?.AbsoluteUri == "https://cdn.example.test/show/master.m3u8", "Expected the clear HLS URL.");
});

Run("DRM marker blocks playback", () =>
{
    using JsonDocument media = JsonDocument.Parse("""
        {"stream_type_drm":"widevine","sources":[{"type":"application/vnd.apple.mpegurl","src":"https://cdn.example.test/show/master.m3u8"}]}
        """);
    PlaybackResult result = SevenPlusApiClient.EvaluatePlaybackMedia(media.RootElement);
    Assert(result.IsDrmProtected && result.ClearHlsUrl is null, "Expected a DRM-only result.");
});

Run("key-system source is rejected", () =>
{
    using JsonDocument media = JsonDocument.Parse("""
        {"sources":[{"type":"application/dash+xml","src":"https://cdn.example.test/show/manifest.mpd","key_systems":{"com.widevine.alpha":{"license_url":"https://license.example.test"}}}]}
        """);
    PlaybackResult result = SevenPlusApiClient.EvaluatePlaybackMedia(media.RootElement);
    Assert(result.IsDrmProtected && result.ClearHlsUrl is null, "Expected key-system metadata to be DRM-protected.");
});

Run("clear HLS wins over a separate protected source", () =>
{
    using JsonDocument media = JsonDocument.Parse("""
        {"sources":[
          {"type":"application/dash+xml","src":"https://cdn.example.test/show/manifest.mpd","key_systems":{"com.widevine.alpha":{}}},
          {"type":"application/vnd.apple.mpegurl","src":"https://cdn.example.test/show/clear.m3u8"}
        ]}
        """);
    PlaybackResult result = SevenPlusApiClient.EvaluatePlaybackMedia(media.RootElement);
    Assert(!result.IsDrmProtected && result.ClearHlsUrl?.AbsoluteUri == "https://cdn.example.test/show/clear.m3u8", "Expected selection of only the clear HLS source.");
});

Run("non-HTTPS and non-HLS sources are not playable", () =>
{
    using JsonDocument media = JsonDocument.Parse("""
        {"sources":[{"type":"video/mp4","src":"http://cdn.example.test/show.mp4"}]}
        """);
    bool rejected = false;
    try
    {
        _ = SevenPlusApiClient.EvaluatePlaybackMedia(media.RootElement);
    }
    catch (InvalidOperationException)
    {
        rejected = true;
    }

    Assert(rejected, "Expected unsupported clear media to be rejected.");
});

Run("episode card DRM metadata is detected", () =>
{
    using JsonDocument row = JsonDocument.Parse("""
        {"playerData":{"drm":true}}
        """);
    Assert(SevenPlusApiClient.IsDrmMarked(row.RootElement), "Expected the episode card to be marked DRM-protected.");
});

Console.WriteLine($"{passed} 7plus catalogue and playback smoke checks passed.");
return 0;

void Run(string name, Action test)
{
    test();
    passed++;
    Console.WriteLine($"PASS {name}");
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static List<ChannelItemInfo> InvokeListParser(string name, JsonElement value)
{
    MethodInfo method = typeof(SevenPlusChannel).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException($"Parser {name} was not found.");
    return (List<ChannelItemInfo>?)method.Invoke(null, [value])
        ?? throw new InvalidOperationException($"Parser {name} returned no result.");
}

static List<ChannelItemInfo> InvokeEpisodeParser(JsonElement value)
{
    MethodInfo method = typeof(SevenPlusChannel).GetMethod("ParseEpisodesAsync", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Episode parser was not found.");
    var task = (Task<List<ChannelItemInfo>>?)method.Invoke(null, [value, CancellationToken.None])
        ?? throw new InvalidOperationException("Episode parser returned no task.");
    return task.GetAwaiter().GetResult();
}
