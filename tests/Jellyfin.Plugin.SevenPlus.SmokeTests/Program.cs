using System.Text.Json;
using Jellyfin.Plugin.SevenPlus.Services;

int passed = 0;
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

Console.WriteLine($"{passed} 7plus safety smoke checks passed.");
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

