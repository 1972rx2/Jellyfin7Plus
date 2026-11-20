# Jellyfin7Plus

Experimental 7plus catch-up TV integration for Jellyfin. The implementation targets the Jellyfin 12.4 API and mirrors the plugin and channel structure used by Jellyfin9Now.

## Current status

- Implemented catalogue search, topic navigation, show seasons, and episode items.
- Implemented official 7plus device pairing without account-password handling.
- Playback metadata rejects DRM-marked media and selects only clear HTTPS HLS sources.
- Version 0.1.4 requests public component shelves without a bearer token, rejects empty or unsupported shelf responses instead of silently returning an empty folder, increments the channel data version to invalidate old results, and uses a versioned catalog image URL while preserving Jellyfin's embedded image resource name.
- The Jellyfin 12.4 test package builds with no warnings; Debug and Release each pass ten local navigation, current-response parsing, and DRM/HLS smoke checks.
- The public 7plus catalogue responses are checked directly, including the live “Aussie Drama” component, which currently returns 41 show cards. Account pairing and playback still need verification through the user's Jellyfin server.

## Remaining verification

1. Install the package on Jellyfin 12.4.
2. Pair a 7plus account using the official `7plus.com.au/connect` flow.
3. Confirm search and season/episode navigation against the current 7plus catalogue.
4. Play a provider-reported clear HLS episode and check that a DRM-protected episode is visibly labelled and refused.
5. Update this status after the server-side check.

The plugin does not implement live TV or attempt DRM playback.
