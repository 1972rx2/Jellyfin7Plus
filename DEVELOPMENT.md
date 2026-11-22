# Jellyfin7Plus

Experimental 7plus catch-up TV integration for Jellyfin. The implementation targets the Jellyfin 12.4 API and mirrors the plugin and channel structure used by Jellyfin9Now.

## Current status

- Implemented catalogue search, topic navigation, show seasons, and episode items.
- Implemented official 7plus device pairing without account-password handling.
- Playback metadata rejects DRM-marked media and selects only clear HTTPS HLS sources.
- Version 0.1.5 makes every public catalogue and market lookup independent of saved account tokens; only playback metadata uses the paired account. This prevents stale or account-filtered credentials from blanking browse results.
- Version 0.1.4 rejects empty or unsupported component shelves instead of silently returning an empty folder, increments the channel data version to invalidate old results, and uses a versioned catalog image URL while preserving Jellyfin's embedded image resource name.
- The Jellyfin 12.4 test package builds with no warnings; Debug and Release each pass local navigation, current-response parsing, empty-shelf reporting, and DRM/HLS smoke checks.
- The public 7plus catalogue responses are checked directly, including the live “Aussie Drama” component, which currently returns 41 show cards. Account pairing and playback still need verification through the user's Jellyfin server.

## Remaining verification

1. Install the package on Jellyfin 12.4.
2. Pair a 7plus account using the official `7plus.com.au/connect` flow.
3. Confirm search and season/episode navigation against the current 7plus catalogue.
4. Play a provider-reported clear HLS episode and check that a DRM-protected episode is visibly labelled and refused.
5. Update this status after the server-side check.

The plugin does not implement live TV or attempt DRM playback.

Version 0.1.6: public captured Drama catalogue parses into shelves, 41 shows, two Home and Away years and 212 episodes. Browse no longer calls authenticated playback for every episode; unknown protection is stated in episode details and enforced at playback. Release smoke checks: 15 passed. Live Jellyfin playback remains unverified.

Version 0.1.7 adds an elevated Check and refresh catalogue dashboard action, uncached navigation checks and a 7plus-only cache revision. Browse successes and exceptions are logged with folder kind and count; tokens are not included. Server-side cause of empty shelves remains unconfirmed until the diagnostic is run on the user's server.

Version 0.1.8 fixes root-relative catalogue links being parsed as implicit file URIs on Unix. Resolve links against the 7plus HTTPS origin before validating scheme and host. Includes a reduced 41-card market 26 fixture and Windows/Linux CI. Server browsing and playback still need post-update confirmation.

0.1.8 packaging: remove the embedded provider application key. An optional per-server ProviderApiKey configuration supplies that header only when configured. Catalogue checks run without an embedded key.

## Installation troubleshooting

An empty version folder without a plugin manifest can interfere with Jellyfin's duplicate-plugin discovery. Unmanifested folders receive generated identities, while duplicate cleanup groups folders by plugin name. A leftover empty directory can therefore cause a valid newer installation to be removed during startup.

When diagnosing a repeated restart requirement, check whether the new DLL survives startup and whether older version directories are empty or missing their manifests. Move confirmed empty orphan directories to a backup outside the plugins directory before reinstalling. Preserve plugin configuration files. Verify the loaded version in the startup log and the dashboard after restart.

## Authentication diagnostics

The official device authorization endpoint rejects a request without the provider application key (HTTP 401); the same request with the previously used key returns a pairing code. Version 0.1.8 removed the embedded key and therefore requires existing installations to configure it locally for authentication. Public catalogue access remains independent of account tokens. No application key or account credential is included in this change.

The dashboard now reports saved credentials as unverified, identifies missing application configuration and expired sessions, and verifies a session through the official refresh endpoint only when Check sign-in status is selected. A pairing poll cannot report success just because an older access token exists. Release build and 20 local smoke checks pass. These source changes are pending a packaged release; live clear-stream playback remains unverified.
