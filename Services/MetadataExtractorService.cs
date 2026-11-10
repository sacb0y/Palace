using System.IO.Compression;
using System.Text;
using System.Text.Json;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Iptc;
using Palace.Helpers;

namespace Palace.Services;

public sealed class GenerationMetadata
{
    public string? Prompt { get; set; }
    public string? NegativePrompt { get; set; }
    public string? Model { get; set; }
    public string? Seed { get; set; }
    public string? Sampler { get; set; }
    public string? Steps { get; set; }

    /// <summary>EXIF DateTimeOriginal (see <c>DateTakenFormat</c>); not part of <see cref="RawJson"/>.</summary>
    public string? DateTaken { get; set; }
    public string RawJson { get; set; } = "{}";
}

public sealed class MetadataExtractorService
{
    public GenerationMetadata Extract(string path)
    {
        if (Helpers.CloudFile.IsOnlineOnly(path))
        {
            return new GenerationMetadata();
        }

        var meta = new GenerationMetadata();
        var ext = Path.GetExtension(path);
        try
        {
            if (ext.Equals(".png", StringComparison.OrdinalIgnoreCase))
            {
                ReadPngText(path, meta);
            }

            ReadExif(path, meta);
        }
        catch
        {
            // Best-effort metadata; scanning must not fail on a bad file.
        }

        meta.RawJson = JsonSerializer.Serialize(new
        {
            meta.Prompt,
            meta.NegativePrompt,
            meta.Model,
            meta.Seed,
            meta.Sampler,
            meta.Steps
        });
        return meta;
    }

    /// <summary>
    /// Camera EXIF for the details rows (exposure, aperture, ISO, focal
    /// length, make / model / lens / date). Local files only — never
    /// recalls an online-only placeholder. Best-effort: missing → None.
    /// </summary>
    /// <summary>
    /// Only EXIF DateTimeOriginal, for the one-time Date taken backfill of unchanged files.
    /// Local files only; never recalls an online-only placeholder.
    /// </summary>
    public string? ReadDateTaken(string path)
    {
        if (string.IsNullOrEmpty(path) || Helpers.CloudFile.IsOnlineOnly(path) || !Helpers.DateTakenFormat.MayHaveExif(path))
        {
            return null;
        }

        try
        {
            var sub = ImageMetadataReader.ReadMetadata(path).OfType<ExifSubIfdDirectory>().FirstOrDefault();
            return sub is not null && sub.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var taken)
                ? Helpers.DateTakenFormat.Format(taken)
                : null;
        }
        catch
        {
            return null;
        }
    }

    public Helpers.CameraExif ReadCamera(string path)
    {
        if (string.IsNullOrEmpty(path) || Helpers.CloudFile.IsOnlineOnly(path) || !File.Exists(path))
        {
            return Helpers.CameraExif.None;
        }

        try
        {
            IReadOnlyList<MetadataExtractor.Directory> dirs = ImageMetadataReader.ReadMetadata(path);
            var sub = dirs.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            var ifd0 = dirs.OfType<ExifIfd0Directory>().FirstOrDefault();
            if (sub is null && ifd0 is null)
            {
                return Helpers.CameraExif.None;
            }

            double? exposure = null;
            double? fnumber = null;
            int? iso = null;
            double? focal = null;
            DateTime? taken = null;
            string? lens = null;
            if (sub is not null)
            {
                if (sub.TryGetRational(ExifDirectoryBase.TagExposureTime, out var et) && et.Denominator != 0)
                {
                    exposure = et.ToDouble();
                }

                if (sub.TryGetRational(ExifDirectoryBase.TagFNumber, out var fn) && fn.Denominator != 0)
                {
                    fnumber = fn.ToDouble();
                }

                if (sub.TryGetInt32(ExifDirectoryBase.TagIsoEquivalent, out var isoValue))
                {
                    iso = isoValue;
                }

                if (sub.TryGetRational(ExifDirectoryBase.TagFocalLength, out var fl) && fl.Denominator != 0)
                {
                    focal = fl.ToDouble();
                }

                if (sub.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dt))
                {
                    taken = dt;
                }

                lens = sub.GetString(ExifDirectoryBase.TagLensModel);
            }

            return new Helpers.CameraExif(
                exposure,
                fnumber,
                iso,
                focal,
                ifd0?.GetString(ExifDirectoryBase.TagMake)?.Trim(),
                ifd0?.GetString(ExifDirectoryBase.TagModel)?.Trim(),
                string.IsNullOrWhiteSpace(lens) ? null : lens.Trim(),
                taken);
        }
        catch
        {
            return Helpers.CameraExif.None;
        }
    }

    private static void ReadPngText(string path, GenerationMetadata meta)
    {
        using var stream = File.OpenRead(path);
        var sig = new byte[8];
        if (stream.Read(sig, 0, 8) != 8)
        {
            return;
        }

        while (stream.Position + 12 <= stream.Length)
        {
            var lenBytes = new byte[4];
            if (stream.Read(lenBytes, 0, 4) != 4)
            {
                break;
            }

            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(lenBytes);
            }

            var length = BitConverter.ToInt32(lenBytes, 0);
            if (length < 0 || stream.Position + 4 + length + 4 > stream.Length)
            {
                break;
            }

            var typeBytes = new byte[4];
            if (stream.Read(typeBytes, 0, 4) != 4)
            {
                break;
            }

            var type = Encoding.ASCII.GetString(typeBytes);
            var data = new byte[length];
            if (length > 0 && stream.Read(data, 0, length) != length)
            {
                break;
            }

            stream.Position += 4; // CRC
            if (type is "tEXt" or "iTXt" or "zTXt")
            {
                ApplyPngChunk(type, data, meta);
            }

            if (type == "IEND")
            {
                break;
            }
        }
    }

    private static void ApplyPngChunk(string type, byte[] data, GenerationMetadata meta)
    {
        string? keyword = null;
        string? text = null;
        if (type == "tEXt")
        {
            var zero = Array.IndexOf(data, (byte)0);
            if (zero <= 0)
            {
                return;
            }

            keyword = Encoding.Latin1.GetString(data, 0, zero);
            text = Encoding.Latin1.GetString(data, zero + 1, data.Length - zero - 1);
        }
        else if (type == "zTXt")
        {
            var zero = Array.IndexOf(data, (byte)0);
            if (zero <= 0 || zero + 2 >= data.Length)
            {
                return;
            }

            keyword = Encoding.Latin1.GetString(data, 0, zero);
            try
            {
                using var compressed = new MemoryStream(data, zero + 2, data.Length - zero - 2);
                using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
                using var reader = new StreamReader(zlib, Encoding.Latin1);
                text = reader.ReadToEnd();
            }
            catch
            {
                return;
            }
        }
        else if (type == "iTXt")
        {
            var zero = Array.IndexOf(data, (byte)0);
            if (zero <= 0 || zero + 3 >= data.Length)
            {
                return;
            }

            keyword = Encoding.Latin1.GetString(data, 0, zero);
            var compressedFlag = data[zero + 1];
            var cursor = zero + 3;
            cursor = SkipCString(data, cursor);
            cursor = SkipCString(data, cursor);
            if (cursor < 0 || cursor > data.Length)
            {
                return;
            }

            try
            {
                if (compressedFlag == 1)
                {
                    using var compressed = new MemoryStream(data, cursor, data.Length - cursor);
                    using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
                    using var reader = new StreamReader(zlib, Encoding.UTF8);
                    text = reader.ReadToEnd();
                }
                else
                {
                    text = Encoding.UTF8.GetString(data, cursor, data.Length - cursor);
                }
            }
            catch
            {
                return;
            }
        }

        if (keyword is null || text is null)
        {
            return;
        }

        if (keyword.Equals("parameters", StringComparison.OrdinalIgnoreCase))
        {
            ApplyA1111(text, meta);
        }
        else if (keyword.Equals("prompt", StringComparison.OrdinalIgnoreCase))
        {
            ApplyComfyPrompt(text, meta);
        }
        else if (keyword.Equals("workflow", StringComparison.OrdinalIgnoreCase))
        {
            ApplyComfyWorkflow(text, meta);
        }
        else if (keyword.Equals("Comment", StringComparison.OrdinalIgnoreCase) ||
                 keyword.Equals("Software", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(meta.Prompt) && A1111Parameters.LooksLike(text))
            {
                ApplyA1111(text, meta);
            }
            else if (string.IsNullOrEmpty(meta.Prompt) && text.Length < 4000)
            {
                meta.Prompt ??= text;
            }
        }
    }

    private static int SkipCString(byte[] data, int start)
    {
        var zero = Array.IndexOf(data, (byte)0, start);
        return zero < 0 ? -1 : zero + 1;
    }

    private static void ApplyA1111(string text, GenerationMetadata meta)
    {
        var parsed = A1111Parameters.Parse(text);
        // Prompt may be empty (A1111 text starting at Negative/Steps). Always
        // assign so a bad prior GetString decimal dump cannot stick.
        meta.Prompt = parsed.Prompt;
        if (parsed.NegativePrompt is not null)
        {
            meta.NegativePrompt = parsed.NegativePrompt;
        }

        meta.Seed ??= parsed.Seed;
        meta.Model ??= parsed.Model;
        meta.Sampler ??= parsed.Sampler;
        meta.Steps ??= parsed.Steps;
    }

    private static void ApplyComfyPrompt(string json, GenerationMetadata meta)
    {
        var parsed = ComfyParameters.ParsePromptJson(json);
        meta.Prompt ??= parsed.Prompt;
        meta.NegativePrompt ??= parsed.NegativePrompt;
        meta.Model ??= parsed.Model;
        meta.Seed ??= parsed.Seed;
        meta.Steps ??= parsed.Steps;
    }

    private static void ApplyComfyWorkflow(string json, GenerationMetadata meta)
    {
        if (!string.IsNullOrEmpty(meta.Model) && !string.IsNullOrEmpty(meta.Prompt))
        {
            return;
        }

        var parsed = ComfyParameters.ParseWorkflowJson(json);
        meta.Prompt ??= parsed.Prompt;
        meta.Model ??= parsed.Model;
    }

    private static void ReadExif(string path, GenerationMetadata meta)
    {
        try
        {
            var tiffLe = !ExifTiffEndian.TryReadLittleEndian(path, out var le) || le;
            IReadOnlyList<MetadataExtractor.Directory> dirs = ImageMetadataReader.ReadMetadata(path);
            foreach (var dir in dirs)
            {
                if (dir is ExifIfd0Directory or ExifSubIfdDirectory)
                {
                    if (dir.ContainsTag(ExifDirectoryBase.TagUserComment))
                    {
                        // GetString returns "85 78 73…" for UNICODE bytes — decode
                        // the raw UNDEFINEDINED payload per the EXIF character code.
                        var raw = dir.GetByteArray(ExifDirectoryBase.TagUserComment);
                        var comment = ExifUserComment.Decode(raw, tiffLittleEndian: tiffLe);
                        if (!string.IsNullOrWhiteSpace(comment))
                        {
                            if (A1111Parameters.LooksLike(comment))
                            {
                                ApplyA1111(comment, meta);
                            }
                            else
                            {
                                meta.Prompt ??= comment.Trim();
                            }
                        }
                    }

                    if (dir.ContainsTag(ExifDirectoryBase.TagImageDescription))
                    {
                        meta.Prompt ??= dir.GetString(ExifDirectoryBase.TagImageDescription)?.Trim();
                    }
                }

                if (dir is ExifSubIfdDirectory sub && meta.DateTaken is null
                    && sub.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var taken))
                {
                    meta.DateTaken = Helpers.DateTakenFormat.Format(taken);
                }

                if (dir is IptcDirectory iptc && iptc.ContainsTag(IptcDirectory.TagCaption))
                {
                    meta.Prompt ??= iptc.GetString(IptcDirectory.TagCaption)?.Trim();
                }
            }
        }
        catch
        {
            // File may not contain EXIF.
        }
    }
}
