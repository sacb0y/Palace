using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Palace.Services;

public sealed class AccessService
{
    public async Task<StorageFolder?> PickFolderAsync()
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary
        };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, App.WindowHandle);
        return await picker.PickSingleFolderAsync();
    }

    public string Remember(StorageFolder folder)
    {
        var token = $"palace_{Guid.NewGuid():N}";
        StorageApplicationPermissions.FutureAccessList.AddOrReplace(token, folder);
        return token;
    }

    public async Task<StorageFolder?> GetFolderAsync(string? token, string path)
    {
        if (!string.IsNullOrEmpty(token) && StorageApplicationPermissions.FutureAccessList.ContainsItem(token))
        {
            try
            {
                return await StorageApplicationPermissions.FutureAccessList.GetFolderAsync(token);
            }
            catch
            {
                // Fall through to path.
            }
        }

        try
        {
            return await StorageFolder.GetFolderFromPathAsync(path);
        }
        catch
        {
            return null;
        }
    }

    public void Forget(string? token)
    {
        if (!string.IsNullOrEmpty(token) && StorageApplicationPermissions.FutureAccessList.ContainsItem(token))
        {
            StorageApplicationPermissions.FutureAccessList.Remove(token);
        }
    }
}
