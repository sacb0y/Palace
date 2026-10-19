using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Iptc;

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
            ParseA1111(text, meta);
        }
        else if (keyword.Equals("prompt", StringComparison.OrdinalIgnoreCase))
        {
            ParseComfyPrompt(text, meta);
        }
        else if (keyword.Equals("workflow", StringComparison.OrdinalIgnoreCase))
        {
            ParseComfyWorkflow(text, meta);
        }
        else if (keyword.Equals("Comment", StringComparison.OrdinalIgnoreCase) ||
                 keyword.Equals("Software", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(meta.Prompt) && text.Contains("Negative prompt", StringComparison.OrdinalIgnoreCase))
            {
                ParseA1111(text, meta);
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

    private static void ParseA1111(string text, GenerationMetadata meta)
    {
        meta.Prompt ??= "";
        var negIdx = text.IndexOf("Negative prompt:", StringComparison.OrdinalIgnoreCase);
        var stepsIdx = text.IndexOf("\nSteps:", StringComparison.OrdinalIgnoreCase);
        if (stepsIdx < 0)
        {
            stepsIdx = text.IndexOf("\nsteps:", StringComparison.OrdinalIgnoreCase);
        }

        if (negIdx >= 0)
        {
            meta.Prompt = text[..negIdx].Trim();
            var negEnd = stepsIdx > negIdx ? stepsIdx : text.Length;
            meta.NegativePrompt = text[(negIdx + "Negative prompt:".Length)..negEnd].Trim();
        }
        else if (stepsIdx >= 0)
        {
            meta.Prompt = text[..stepsIdx].Trim();
        }
        else
        {
            meta.Prompt = text.Trim();
        }

        var footer = stepsIdx >= 0 ? text[stepsIdx..] : text;
        meta.Seed ??= MatchField(footer, "Seed");
        meta.Model ??= MatchField(footer, "Model");
        meta.Sampler ??= MatchField(footer, "Sampler");
        meta.Steps ??= MatchField(footer, "Steps");
    }

    private static string? MatchField(string text, string name)
    {
        var match = Regex.Match(text, $@"\b{Regex.Escape(name)}:\s*([^,\n]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static void ParseComfyPrompt(string json, GenerationMetadata meta)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var prompts = new List<string>();
            foreach (var node in doc.RootElement.EnumerateObject())
            {
                if (node.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var classType = node.Value.TryGetProperty("class_type", out var ct) ? ct.GetString() : "";
                if (!node.Value.TryGetProperty("inputs", out var inputs))
                {
                    continue;
                }

                if (classType is "CLIPTextEncode" or "CLIPTextEncodeSDXL" or "CLIPTextEncodeFlux")
                {
                    if (inputs.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    {
                        var value = text.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            prompts.Add(value);
                        }
                    }
                }

                if (classType is "CheckpointLoaderSimple" or "CheckpointLoader")
                {
                    if (inputs.TryGetProperty("ckpt_name", out var ckpt) && ckpt.ValueKind == JsonValueKind.String)
                    {
                        meta.Model ??= Path.GetFileNameWithoutExtension(ckpt.GetString());
                    }
                }

                if (classType is "UNETLoader")
                {
                    if (inputs.TryGetProperty("unet_name", out var unet) && unet.ValueKind == JsonValueKind.String)
                    {
                        meta.Model ??= Path.GetFileNameWithoutExtension(unet.GetString());
                    }
                }

                if (classType is "KSampler" or "KSamplerAdvanced")
                {
                    if (inputs.TryGetProperty("seed", out var seed))
                    {
                        meta.Seed ??= seed.ToString();
                    }

                    if (inputs.TryGetProperty("steps", out var steps))
                    {
                        meta.Steps ??= steps.ToString();
                    }
                }
            }

            if (prompts.Count > 0)
            {
                meta.Prompt ??= prompts[0];
                if (prompts.Count > 1)
                {
                    meta.NegativePrompt ??= prompts[^1];
                }
            }
        }
        catch (JsonException)
        {
            // ignore
        }
    }

    private static void ParseComfyWorkflow(string json, GenerationMetadata meta)
    {
        if (!string.IsNullOrEmpty(meta.Model) && !string.IsNullOrEmpty(meta.Prompt))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var node in nodes.EnumerateArray())
            {
                var type = node.TryGetProperty("type", out var t) ? t.GetString() : "";
                if (type is "CheckpointLoaderSimple" && node.TryGetProperty("widgets_values", out var widgets)
                    && widgets.ValueKind == JsonValueKind.Array && widgets.GetArrayLength() > 0)
                {
                    meta.Model ??= Path.GetFileNameWithoutExtension(widgets[0].GetString());
                }

                if (type is "CLIPTextEncode" && node.TryGetProperty("widgets_values", out var texts)
                    && texts.ValueKind == JsonValueKind.Array && texts.GetArrayLength() > 0)
                {
                    var value = texts[0].GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        meta.Prompt ??= value;
                    }
                }
            }
        }
        catch (JsonException)
        {
            // ignore
        }
    }

    private static void ReadExif(string path, GenerationMetadata meta)
    {
        try
        {
            IReadOnlyList<MetadataExtractor.Directory> dirs = ImageMetadataReader.ReadMetadata(path);
            foreach (var dir in dirs)
            {
                if (dir is ExifIfd0Directory or ExifSubIfdDirectory)
                {
                    if (dir.ContainsTag(ExifDirectoryBase.TagUserComment))
                    {
                        var comment = dir.GetString(ExifDirectoryBase.TagUserComment);
                        if (!string.IsNullOrWhiteSpace(comment))
                        {
                            if (comment.Contains("Negative prompt", StringComparison.OrdinalIgnoreCase) ||
                                comment.Contains("Steps:", StringComparison.OrdinalIgnoreCase))
                            {
                                ParseA1111(comment, meta);
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
