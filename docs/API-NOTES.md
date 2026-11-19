# 7plus API notes

The 7plus website's public app uses separate services for catalogue search, show pages, episode components, account pairing, and playback. These are app endpoints rather than a published developer API, so response shapes and required parameters can change.

| Purpose | Service | Plugin use |
| --- | --- | --- |
| Market lookup | `market-cdn.swm.digital/v1/market/ip/` | Resolve the local 7plus market; use market 4 as the NSW fallback. |
| Search | `searchapi.swm.digital/3.0/api/Search` | Search configured terms and the built-in topic terms. |
| Show metadata | `component-cdn.swm.digital/content/{slug}` | Read show details and season component identifiers. |
| Season episodes | `component.swm.digital/component/{slug}` | Read episode IDs, descriptions, images, and any catalogue DRM flags. |
| Episode playback | `videoservice.swm.digital/playback` | Inspect playback metadata and select only clear HTTPS HLS sources. |
| Device pairing | `auth2.swm.digital/account/device/authorize` and `/connect/token` | Issue a pairing code, then poll for tokens after approval at the official 7plus connect page. |

The plugin stores access and refresh tokens in Jellyfin's local plugin configuration. Treat that configuration and its backups as sensitive. Pairing secrets stay in server memory until approved or expired; the plugin does not ask for or store an account password.

DRM status is taken from episode metadata when present or from the playback service's DRM flags and source key-system metadata. The plugin does not send a DRM licence request, extract keys, alter manifests, or forward encrypted media to Jellyfin. It discards playback responses for protected items and only returns clear HLS sources.

The service also enforces market-specific availability and may require account or precise-location verification for some content. The plugin does not alter the network location or those checks. It does not implement live TV.

