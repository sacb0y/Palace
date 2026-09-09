namespace Palace.Helpers;

/// <summary>
/// Stable JPEG names for the thumbnail cache. Cloud stub hashes contain
/// <c>:</c> and other filesystem-illegal characters; sanitize before joining.
/// </summary>
public static class ThumbFileName
{
    public static string Sanitize(string hash)
    {
        var buffer = new char[hash.Length];
        for (var i = 0; i < hash.Length; i++)
        {
            var ch = hash[i];
            buffer[i] = char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_';
        }

        return new string(buffer);
    }

    public static string FileName(string hash, string? suffix = null) =>
        string.IsNullOrEmpty(suffix) ? $"{Sanitize(hash)}.jpg" : $"{Sanitize(hash)}{suffix}.jpg";
}
