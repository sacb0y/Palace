using System.Collections.Concurrent;
using Palace.Data;
using Palace.Models;
using Palace.Services.Cloud;
using Windows.Storage;

namespace Palace.Services;

public sealed class CloudAccountService
{
    public const string OneDriveClientIdKey = "OneDriveClientId";
    public const string DropboxAppKeyKey = "DropboxAppKey";

    private static readonly Uri OneDriveAuthorize = new("https://login.microsoftonline.com/common/oauth2/v2.0/authorize?scope=offline_access%20Files.Read%20User.Read");
    private static readonly Uri OneDriveToken = new("https://login.microsoftonline.com/common/oauth2/v2.0/token");
    private static readonly Uri DropboxAuthorize = new("https://www.dropbox.com/oauth2/authorize?token_access_type=offline&scope=files.metadata.read%20files.content.read%20account_info.read");
    private static readonly Uri DropboxToken = new("https://api.dropboxapi.com/oauth2/token");

    private readonly CatalogService _catalog;
    private readonly CloudTokenStore _vault = new();
    private readonly ConcurrentDictionary<string, CloudAuthTokens> _access = new(StringComparer.Ordinal);

    public CloudAccountService(CatalogService catalog)
    {
        _catalog = catalog;
    }

    public string OneDriveClientId
    {
        get => ReadSetting(OneDriveClientIdKey);
        set => WriteSetting(OneDriveClientIdKey, value);
    }

    public string DropboxAppKey
    {
        get => ReadSetting(DropboxAppKeyKey);
        set => WriteSetting(DropboxAppKeyKey, value);
    }

    public string RedirectUriDisplay
    {
        get
        {
            try
            {
                return CloudOAuth.CallbackUri.ToString();
            }
            catch
            {
                return "";
            }
        }
    }

    public Task<CloudAccount?> GetAccountAsync(CloudProvider provider) =>
        _catalog.GetCloudAccountByProviderAsync(provider);

    public async Task<string> StatusTextAsync(CloudProvider provider)
    {
        var account = await GetAccountAsync(provider).ConfigureAwait(false);
        return account is null
            ? "Not connected"
            : "Connected as " + (string.IsNullOrWhiteSpace(account.DisplayName) ? account.AccountId : account.DisplayName);
    }

    public async Task<CloudConnectResult> ConnectOneDriveAsync(CancellationToken ct = default)
    {
        var clientId = OneDriveClientId.Trim();
        if (string.IsNullOrEmpty(clientId))
        {
            throw new CloudAuthException("Enter a OneDrive application (client) ID first.");
        }

        var tokens = await CloudOAuth.AuthenticateAsync(OneDriveAuthorize, OneDriveToken, clientId, "", ct)
            .ConfigureAwait(false);
        using var me = await CloudOAuth.GetJsonAsync(
            "https://graph.microsoft.com/v1.0/me?$select=id,displayName,userPrincipalName,mail",
            tokens.AccessToken,
            ct).ConfigureAwait(false);
        var id = me.RootElement.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
        var mail = FirstString(me.RootElement, "userPrincipalName", "mail", "displayName") ?? id;
        var name = FirstString(me.RootElement, "displayName", "userPrincipalName", "mail") ?? mail;
        return await PersistAsync(CloudProvider.OneDrive, id, name, mail, tokens).ConfigureAwait(false);
    }

    public async Task<CloudConnectResult> ConnectDropboxAsync(CancellationToken ct = default)
    {
        var appKey = DropboxAppKey.Trim();
        if (string.IsNullOrEmpty(appKey))
        {
            throw new CloudAuthException("Enter a Dropbox app key first.");
        }

        var tokens = await CloudOAuth.AuthenticateAsync(DropboxAuthorize, DropboxToken, appKey, "", ct)
            .ConfigureAwait(false);
        using var me = await CloudOAuth.PostJsonAsync(
            "https://api.dropboxapi.com/2/users/get_current_account",
            tokens.AccessToken,
            "null",
            ct).ConfigureAwait(false);
        var id = me.RootElement.TryGetProperty("account_id", out var idEl) ? idEl.GetString() ?? "" : "";
        var email = me.RootElement.TryGetProperty("email", out var emailEl) ? emailEl.GetString() : null;
        var name = me.RootElement.TryGetProperty("name", out var nameEl) && nameEl.TryGetProperty("display_name", out var dn)
            ? dn.GetString()
            : email;
        return await PersistAsync(CloudProvider.Dropbox, id, name ?? email ?? id, email ?? id, tokens)
            .ConfigureAwait(false);
    }

    public async Task DisconnectAsync(CloudProvider provider)
    {
        var account = await GetAccountAsync(provider).ConfigureAwait(false);
        if (account is null)
        {
            return;
        }

        _vault.Delete(account.VaultKey);
        _access.TryRemove(account.Id, out _);
        await _catalog.DeleteCloudAccountAsync(account.Id).ConfigureAwait(false);
    }

    public async Task<string> GetValidAccessTokenAsync(CloudAccount account, CancellationToken ct = default)
    {
        if (_access.TryGetValue(account.Id, out var cached) &&
            !string.IsNullOrEmpty(cached.AccessToken) &&
            cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return cached.AccessToken;
        }

        var refresh = _vault.LoadRefreshToken(account.VaultKey);
        if (string.IsNullOrEmpty(refresh))
        {
            throw new CloudAuthException("This cloud account is not signed in. Connect it again in Settings.");
        }

        var clientId = account.Provider == CloudProvider.OneDrive ? OneDriveClientId.Trim() : DropboxAppKey.Trim();
        if (string.IsNullOrEmpty(clientId))
        {
            throw new CloudAuthException("The application ID for this provider is missing.");
        }

        var tokenUri = account.Provider == CloudProvider.OneDrive ? OneDriveToken : DropboxToken;
        var tokens = await CloudOAuth.RefreshAsync(tokenUri, clientId, refresh, "", ct).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(tokens.RefreshToken))
        {
            _vault.SaveRefreshToken(account.VaultKey, tokens.RefreshToken);
        }

        _access[account.Id] = tokens;
        return tokens.AccessToken;
    }

    public static string BuildSourcePath(CloudProvider provider, string accountLabel, string folderDisplay)
    {
        var safeAccount = new string(accountLabel.Select(ch =>
            char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_').ToArray());
        var folder = folderDisplay.Replace('/', Path.DirectorySeparatorChar).Trim(Path.DirectorySeparatorChar);
        return Path.Combine("cloud", provider.ToString().ToLowerInvariant(), safeAccount, folder);
    }

    private async Task<CloudConnectResult> PersistAsync(
        CloudProvider provider,
        string accountId,
        string displayName,
        string accountLabel,
        CloudAuthTokens tokens)
    {
        if (string.IsNullOrEmpty(accountId))
        {
            throw new CloudAuthException("The provider did not return an account id.");
        }

        var existing = await GetAccountAsync(provider).ConfigureAwait(false);
        var account = existing ?? new CloudAccount
        {
            Id = PalaceDb.NewId(),
            Provider = provider,
            VaultKey = PalaceDb.NewId()
        };
        account.AccountId = accountId;
        account.DisplayName = string.IsNullOrWhiteSpace(displayName) ? accountLabel : displayName;
        if (string.IsNullOrEmpty(account.VaultKey))
        {
            account.VaultKey = PalaceDb.NewId();
        }

        if (!string.IsNullOrEmpty(tokens.RefreshToken))
        {
            _vault.SaveRefreshToken(account.VaultKey, tokens.RefreshToken);
        }

        _access[account.Id] = tokens;
        await _catalog.UpsertCloudAccountAsync(account).ConfigureAwait(false);
        return new CloudConnectResult { Account = account, DisplayName = account.DisplayName ?? accountLabel };
    }

    private static string? FirstString(System.Text.Json.JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        return null;
    }

    private static string ReadSetting(string key) =>
        ApplicationData.Current.LocalSettings.Values[key] as string ?? "";

    private static void WriteSetting(string key, string value) =>
        ApplicationData.Current.LocalSettings.Values[key] = value ?? "";
}
