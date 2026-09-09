using Palace.Models;

namespace Palace.Services.Cloud;

public sealed class CloudLibraryFactory : ICloudLibraryFactory
{
    private readonly CatalogService _catalog;
    private readonly CloudAccountService _accounts;

    public CloudLibraryFactory(CatalogService catalog, CloudAccountService accounts)
    {
        _catalog = catalog;
        _accounts = accounts;
    }

    public async Task<ICloudLibrary?> ForSourceAsync(SourceFolder source, CancellationToken ct = default)
    {
        if (source.Kind == SourceKind.Local || string.IsNullOrEmpty(source.CloudAccountId))
        {
            return null;
        }

        var account = await _catalog.GetCloudAccountAsync(source.CloudAccountId).ConfigureAwait(false);
        return account is null ? null : await ForAccountAsync(account, ct).ConfigureAwait(false);
    }

    public Task<ICloudLibrary?> ForAccountAsync(CloudAccount account, CancellationToken ct = default)
    {
        ICloudLibrary library = account.Provider switch
        {
            CloudProvider.Dropbox => new DropboxLibrary(_accounts, account),
            _ => new OneDriveLibrary(_accounts, account)
        };
        return Task.FromResult<ICloudLibrary?>(library);
    }
}
