using System;
using Jellyfin.Plugin.SevenPlus.Configuration;

namespace Jellyfin.Plugin.SevenPlus.Services;

/// <summary>Describes a saved session without claiming that the provider accepted it.</summary>
public sealed record LoginSessionStatus(bool Complete, string Detail, string State)
{
    /// <summary>Inspects local configuration without transmitting account credentials.</summary>
    public static LoginSessionStatus FromSavedSession(PluginConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (string.IsNullOrWhiteSpace(configuration.AccessToken)
            && string.IsNullOrWhiteSpace(configuration.RefreshToken))
        {
            return new(false, "7plus is not signed in.", "signedOut");
        }

        if (string.IsNullOrWhiteSpace(configuration.ProviderApiKey))
        {
            return new(false, "A session is saved, but the 7plus application key is missing. Configure it under Advanced connection settings and save, then check sign-in status.", "applicationKeyMissing");
        }

        bool expired = configuration.TokenExpiresAtUnixSeconds <= DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (expired && string.IsNullOrWhiteSpace(configuration.RefreshToken))
        {
            return new(false, "The saved 7plus session has expired. Sign in again.", "expired");
        }

        return new(false, expired
            ? "The saved session needs refreshing. Select Check sign-in status to test it with 7plus."
            : "A 7plus session is saved. Select Check sign-in status to verify it with 7plus.", "unverified");
    }
}
