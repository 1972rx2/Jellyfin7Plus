# Jellyfin7Plus

Experimental 7plus catch-up TV integration for Jellyfin.

> Development scaffold only. No working 7plus catalogue, authentication or playback integration has been implemented or verified yet.

## Goals
- Browse on-demand programmes, seasons and episodes within Jellyfin (not live TV).
- Display 7plus branding in the Jellyfin plugin page and library/dashboard entry.
- Display artwork, programme descriptions and episode metadata.
- Identify DRM-protected streams and visibly label them `[DRM]` and/or with a thumbnail badge, following the approach used in [Jellyfin9Now](https://github.com/1972rx2/Jellyfin9Now).
- Play only streams supported by Jellyfin without circumventing DRM.

## Implementation plan
1. Review the existing Jellyfin9Now plugin architecture, compatibility target and icon handling.
2. Implement a minimal buildable Jellyfin plugin, with its own plugin identifier and branding.
3. Research 7plus catalogue and authentication interfaces; use permitted endpoints and respect service terms.
4. Add programme/season/episode browsing, metadata and images.
5. Detect protected playback variants and mark them DRM; never expose them as ordinary playable media.
6. Test supported clear streams on a Jellyfin installation, then package a release.

## Status
Repository initialised. No installable release yet.
