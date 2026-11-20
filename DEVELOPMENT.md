# Jellyfin7Plus

Experimental 7plus catch-up TV integration for Jellyfin. The implementation targets the Jellyfin 12.4 API and mirrors the plugin and channel structure used by Jellyfin9Now.

## Current status

- Implemented catalogue search, topic navigation, show seasons, and episode items.
- Implemented official 7plus device pairing without account-password handling.
- Playback metadata rejects DRM-marked media and selects only clear HTTPS HLS sources.
- Version 0.1.1 shows genre folders directly at the channel root, fixes the versioned dashboard image URL, and uses the supplied 7+ logo in the manifest and channel.
- The Jellyfin 12.4 test package builds with no warnings; Debug and Release each pass seven local navigation and DRM/HLS smoke checks.
- Live requests to 7plus could not be exercised in this sandbox. A Jellyfin server and an Australian 7plus account are still needed to verify pairing, catalogue response shapes, and clear-stream playback end to end.

## Remaining verification

1. Install the package on Jellyfin 12.4.
2. Pair a 7plus account using the official `7plus.com.au/connect` flow.
3. Confirm search and season/episode navigation against the current 7plus catalogue.
4. Play a provider-reported clear HLS episode and check that a DRM-protected episode is visibly labelled and refused.
5. Update this status after the server-side check.

The plugin does not implement live TV or attempt DRM playback.
