# Jellyfin7Plus

An early Jellyfin 12.4 channel plugin for browsing 7plus catch-up shows and playing clear HLS episodes.

## Included

- 7plus branding on the channel and dashboard configuration page.
- Open the channel to see genre folders immediately, then navigate shows, seasons, and episodes. Public catalogue requests do not require an account token; pairing is retained for playback requests.
- Pair a 7plus account through the official `7plus.com.au/connect` device-code flow. The Jellyfin server never asks for or stores the account password.
- DRM-protected episodes are labelled when the 7plus component or playback metadata reports protection. The plugin refuses those episodes and never requests a DRM licence or modifies DRM media.
- Clear HTTPS HLS playback is returned to Jellyfin with the provider user agent.

7plus says TV applications require an account connection and describes entering the displayed code at `7plus.com.au/connect`. The service also confirms that DRM protections apply to some of its catalogue and are increasing over time. See the [official 7plus TV sign-in instructions](https://support.7plus.com.au/hc/en-au/articles/360060872732-How-to-sign-into-the-7plus-app-on-my-TV) and [official DRM FAQ](https://support.7plus.com.au/hc/en-au/articles/34737241089677-What-is-DRM-protection).

The integration uses the 7plus website's public catalogue and playback services. Those endpoints are undocumented and may change without notice. Browse topics are catalogue search terms, not a provider-maintained genre list. Australian availability and market-specific rights are controlled by 7plus.

## Install from the Jellyfin catalog

In Jellyfin, open **Dashboard → Plugins → Repositories**, add a repository named `7plus`, and use:

`https://raw.githubusercontent.com/1972rx2/Jellyfin7Plus/main/manifest.json`

Then open **Catalog**, install **7plus**, and restart Jellyfin. This test build targets Jellyfin 12.4 (`targetAbi` `12.4.0.0`).

## Manual install

1. Extract `Jellyfin.Plugin.SevenPlus.dll` from [Jellyfin.Plugin.SevenPlus-0.1.4-jellyfin12.4-test.zip](Jellyfin.Plugin.SevenPlus-0.1.4-jellyfin12.4-test.zip) into a new `SevenPlus` directory under Jellyfin's plugins directory.
2. Restart Jellyfin.
3. Open **Dashboard → Plugins → 7plus**, pair an account, and browse the **7plus** channel.

## Sign in

1. In Jellyfin, open **Dashboard → Plugins → 7plus** and select **Sign in to 7plus**.
2. Open the displayed official connection page and enter the pairing code after signing in to your 7plus account.
3. Wait for the dashboard to confirm sign-in.
4. Enter a search term if desired, then open the **7plus** channel and browse by topic or search results.

## DRM and playback

The plugin checks 7plus playback metadata for episode protection, labels protected episodes `[DRM]`, and refuses to return them as a Jellyfin media source. For non-protected episodes it only returns an HTTPS HLS source with no DRM key-system metadata. Ads and other provider stream behaviour remain controlled by 7plus.

## Build

```powershell
dotnet restore Jellyfin.Plugin.SevenPlus/Jellyfin.Plugin.SevenPlus.csproj
dotnet build Jellyfin.Plugin.SevenPlus/Jellyfin.Plugin.SevenPlus.csproj --no-restore
dotnet run --project tests/Jellyfin.Plugin.SevenPlus.SmokeTests/Jellyfin.Plugin.SevenPlus.SmokeTests.csproj
```

The target framework and package versions mirror the companion [Jellyfin9Now repository](https://github.com/1972rx2/Jellyfin9Now).

See [API notes](docs/API-NOTES.md) for the app services used by the plugin and its playback limits.

## Verification status

Both Debug and Release builds pass with no warnings. Eleven local smoke checks cover direct genre navigation, current listing and episode response parsing, surfaced empty-shelf errors, DRM rejection, clear HLS selection, and unsupported media. The live 7plus “Aussie Drama” component returns 41 show cards. Account pairing and playback have not been exercised through a live Jellyfin session.
