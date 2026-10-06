using Palace.Helpers;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Palace.Services;

public sealed class AccessService
{
    public const string OnlineOnlyCopyWarning =
        "Skipped online-only files. Copying them would download the original. Open the file first, then copy.";

    public static bool ShouldSkipClipboardCopy(string? path) => CloudFile.IsOnlineOnly(path);

    public static bool WouldHydrateOnOpen(string? path) => CloudFile.IsOnlineOnly(path);

    public static IReadOnlyList<string> FilterCopyPaths(IEnumerable<string> paths, out int skippedOnlineOnly) =>
        CloudFile.FilterLocalPaths(paths, out skippedOnlineOnly);

    public async Task<StorageFile?> PickImageFileAsync()
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary
        };
        foreach (var ext in (string[])[".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif"])
        {
            picker.FileTypeFilter.Add(ext);
        }

        InitializeWithWindow.Initialize(picker, App.WindowHandle);
        return await picker.PickSingleFileAsync();
    }

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
