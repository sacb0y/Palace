using Windows.Security.Credentials;

namespace Palace.Services.Cloud;

/// <summary>
/// OAuth refresh tokens live in PasswordVault, never SQLite.
/// Access tokens stay in memory. Long secrets are split across vault entries.
/// </summary>
public sealed class CloudTokenStore
{
    private const string ResourcePrefix = "Palace.Cloud.";
    private const int ChunkSize = 480;

    public void SaveRefreshToken(string vaultKey, string refreshToken)
    {
        Delete(vaultKey);
        var vault = new PasswordVault();
        var chunks = Split(refreshToken);
        for (var i = 0; i < chunks.Count; i++)
        {
            vault.Add(new PasswordCredential(ResourceName(vaultKey, i), vaultKey, chunks[i]));
        }
    }

    public string? LoadRefreshToken(string vaultKey)
    {
        var vault = new PasswordVault();
        var parts = new List<string>();
        for (var i = 0; i < 8; i++)
        {
            try
            {
                var credential = vault.Retrieve(ResourceName(vaultKey, i), vaultKey);
                credential.RetrievePassword();
                parts.Add(credential.Password);
            }
            catch
            {
                break;
            }
        }

        return parts.Count == 0 ? null : string.Concat(parts);
    }

    public void Delete(string vaultKey)
    {
        var vault = new PasswordVault();
        for (var i = 0; i < 8; i++)
        {
            try
            {
                var credential = vault.Retrieve(ResourceName(vaultKey, i), vaultKey);
                vault.Remove(credential);
            }
            catch
            {
                break;
            }
        }
    }

    private static string ResourceName(string vaultKey, int index) =>
        index == 0 ? ResourcePrefix + vaultKey : ResourcePrefix + vaultKey + "." + index;

    private static List<string> Split(string value)
    {
        var chunks = new List<string>();
        for (var i = 0; i < value.Length; i += ChunkSize)
        {
            chunks.Add(value.Substring(i, Math.Min(ChunkSize, value.Length - i)));
        }

        return chunks.Count == 0 ? [""] : chunks;
    }
}
