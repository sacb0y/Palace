using Microsoft.Data.Sqlite;

namespace Palace.Helpers;

/// <summary>
/// Clip Studio Paint <c>.clip</c>: a <c>CSFCHUNK</c> container whose last
/// chunk is a SQLite database. Its <c>CanvasPreview.ImageData</c> column
/// holds a PNG of the flattened canvas, which is what we thumbnail.
/// </summary>
public static class ClipFile
{
    public static ReadOnlySpan<byte> ChunkMagic => "CSFCHUNK"u8;

    public static ReadOnlySpan<byte> SqliteMagic => "SQLite format 3\0"u8;

    public static bool IsClip(ReadOnlySpan<byte> data) => data.StartsWith(ChunkMagic);

    /// <summary>PNG bytes of the canvas preview, or null.</summary>
    public static byte[]? TryExtractPreview(string path)
    {
        try
        {
            var data = File.ReadAllBytes(path);
            return TryExtractPreview(data);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static byte[]? TryExtractPreview(byte[] data)
    {
        if (!IsClip(data))
        {
            return null;
        }

        var start = data.AsSpan().IndexOf(SqliteMagic);
        if (start < 0)
        {
            return null;
        }

        var tmp = Path.Combine(Path.GetTempPath(), "palace-clip-" + Guid.NewGuid().ToString("N") + ".sqlite");
        try
        {
            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write))
            {
                fs.Write(data, start, data.Length - start);
            }

            using var conn = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = tmp,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT ImageData FROM CanvasPreview WHERE ImageData IS NOT NULL ORDER BY length(ImageData) DESC LIMIT 1";
            var blob = cmd.ExecuteScalar() as byte[];
            return blob is { Length: > 8 } ? blob : null;
        }
        catch (SqliteException)
        {
            return null;
        }
        finally
        {
            try { File.Delete(tmp); } catch { /* best-effort */ }
        }
    }
}
