using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentDL.Core.Contracts.Services;
using FluentDL.Core.Sources;

namespace FluentDL.Services.Playlists;

// Only the refresh token, granted scopes and account/client binding survive an app restart.
internal sealed class SpotifyUserSessionStore(ILocalSettingsService settings)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("FluentDL.SpotifyUserSession.v1");
    internal sealed record Session(string ClientId, string CredentialsHash, string UserId, string RefreshToken, string Scope);

    internal static string CredentialsHash(string clientId, string secret) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new[] { clientId, secret })));

    internal async Task<Session?> ReadAsync(string clientId, string secret, Func<bool> isCurrent)
    {
        await gate.WaitAsync();
        try
        {
            if (!isCurrent()) return null;
            var saved = await settings.ReadSettingAsync<string>(SettingKeys.SpotifyUserSession);
            if (!isCurrent() || string.IsNullOrEmpty(saved)) return null;
            Session? session;
            try
            {
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
                var bytes = ProtectedData.Unprotect(Convert.FromBase64String(saved), Entropy, DataProtectionScope.CurrentUser);
                try { session = JsonSerializer.Deserialize<Session>(bytes); }
                finally { CryptographicOperations.ZeroMemory(bytes); }
            }
            catch (Exception error) when (error is CryptographicException or FormatException or JsonException)
            {
                await settings.SaveSettingAsync(SettingKeys.SpotifyUserSession, "");
                return null;
            }
            if (session == null || session.ClientId != clientId || session.CredentialsHash != CredentialsHash(clientId, secret)
                || string.IsNullOrWhiteSpace(session.UserId) || string.IsNullOrWhiteSpace(session.RefreshToken)
                || !SpotifyUserAuthorization.HasPlaylistScopes(session.Scope))
            {
                await settings.SaveSettingAsync(SettingKeys.SpotifyUserSession, "");
                return null;
            }
            return session;
        }
        finally { gate.Release(); }
    }

    internal async Task SaveAsync(Session session, Func<bool> isCurrent)
    {
        await gate.WaitAsync();
        try
        {
            if (!isCurrent()) return;
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            var bytes = JsonSerializer.SerializeToUtf8Bytes(session);
            try
            {
                var encrypted = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
                await settings.SaveSettingAsync(SettingKeys.SpotifyUserSession, Convert.ToBase64String(encrypted));
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        finally { gate.Release(); }
    }

    internal async Task ClearAsync()
    {
        await gate.WaitAsync();
        try { await settings.SaveSettingAsync(SettingKeys.SpotifyUserSession, ""); }
        finally { gate.Release(); }
    }
}
