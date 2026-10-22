using System.Diagnostics;
using Palace.Helpers;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Storage.Streams;

namespace Palace.Services;

/// <param name="IsPlaceholder">The content could not be decoded; the cached JPEG is the placeholder tile.</param>
public readonly record struct ThumbnailInfo(string Path, int Width, int Height, bool IsPlaceholder = false);

/// <summary>BGRA pixels already scaled to the preview or tile box. Produced off the UI thread.</summary>
public readonly record struct CappedBgra(byte[] Bgra, int Width, int Height, double Milliseconds);

public sealed class ThumbnailService
{
    public const int MaxSide = 512;
    public const string LargePreviewSuffix = "_lg";

    private string _root;
    private DateTime _generationStartUtc;
    private DateTime _shelfGenerationStartUtc;
    private DateTime _jxlIntensityGenerationStartUtc;

    public ThumbnailService(string thumbsRoot)
    {
        _root = thumbsRoot;
        Directory.CreateDirectory(_root);
        _generationStartUtc = ThumbCacheVersion.EnsureMarker(_root, ThumbCacheVersion.DecodersGeneration);
        _shelfGenerationStartUtc = ThumbCacheVersion.EnsureMarker(_root, ThumbCacheVersion.ShelfPreviewGeneration);
        _jxlIntensityGenerationStartUtc = ThumbCacheVersion.EnsureMarker(_root, ThumbCacheVersion.JxlIntensityGeneration);
    }

    /// <summary>Active JPEG folder under the media cache root (<c>…/thumbs</c>).</summary>
    public string Root => _root;

    public void SetRoot(string thumbsRoot)
    {
        _root = thumbsRoot;
        Directory.CreateDirectory(_root);
        _generationStartUtc = ThumbCacheVersion.EnsureMarker(_root, ThumbCacheVersion.DecodersGeneration);
        _shelfGenerationStartUtc = ThumbCacheVersion.EnsureMarker(_root, ThumbCacheVersion.ShelfPreviewGeneration);
        _jxlIntensityGenerationStartUtc = ThumbCacheVersion.EnsureMarker(_root, ThumbCacheVersion.JxlIntensityGeneration);
    }

    /// <summary>
    /// Formats Palace decodes itself (EXR/HDR/TGA/Clip/PSD/JXL or a
    /// not-an-image file) must be shown from the cached large preview —
    /// <c>BitmapImage</c> would be blank or need a Store codec. Returns the
    /// path to display, or null while that preview doesn't exist yet.
    /// </summary>
    public string? DisplayPathFor(string? path)
    {
        if (string.IsNullOrEmpty(path) || GalleryMedia.IsRemoteUri(path))
        {
            return path;
        }

        var lg = ExistingLargePreviewForPath(path);
        if (lg is not null)
        {
            return lg;
        }

        // JXL without a decoded preview falls back to the WIC codec, if any.
        return StillDecode.HandlesPath(path) ? null : path;
    }

    private bool IsStaleGeneration(string dest, string filePath)
    {
        try
        {
            var written = File.GetLastWriteTimeUtc(dest);
            if (ThumbCacheVersion.IsStale(written, _shelfGenerationStartUtc, ThumbCacheVersion.ShelfPreviewChanged(filePath)))
            {
                return true;
            }

            if (ThumbCacheVersion.IsStale(written, _jxlIntensityGenerationStartUtc, ThumbCacheVersion.JxlIntensityChanged(filePath)))
            {
                return true;
            }

            if (written >= _generationStartUtc)
            {
                return false;
            }

            var ext = PathSafe.Extension(filePath);
            var hdrCandidate = ext is ".png" or ".jxr" or ".wdp" or ".hdp" or ".avif" or ".heic" or ".heif"
                && HdrFile.ProbePath(filePath) is { IsHdr: true, Kind: not HdrKind.UltraHdrJpeg };
            return ThumbCacheVersion.IsStale(written, _generationStartUtc,
                ThumbCacheVersion.RenderingChanged(filePath, hdrCandidate));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// True when <see cref="EnsureThumbnailAsync"/> would return the cached thumb as-is (exists,
    /// right size, current generation, large preview present). Scan fast path uses this so an
    /// unchanged file still regenerates a stale thumbnail.
    /// </summary>
    public bool IsCachedThumbCurrent(string filePath, string hash, Models.AssetKind kind) =>
        kind == Models.AssetKind.Other || IsCachedCurrent(PathForHash(hash), filePath, kind);

    private bool IsCachedCurrent(string dest, string filePath, Models.AssetKind kind) =>
        File.Exists(dest) && !ShouldRegenerate(dest, filePath)
        && !(kind == Models.AssetKind.Image && IsStaleGeneration(dest, filePath))
        && !NeedsLargePreviewBackfill(kind, filePath);

    public string PathForHash(string hash, string? suffix = null) =>
        Path.Combine(_root, ThumbFileName.FileName(hash, suffix));

    public string? ExistingPathForHash(string? hash, string? suffix = null)
    {
        if (string.IsNullOrEmpty(hash))
        {
            return null;
        }

        var path = PathForHash(hash, suffix);
        return File.Exists(path) ? path : null;
    }

    public void TryDelete(string? hash, string? filePath = null)
    {
        var paths = new List<string>();
        if (!string.IsNullOrEmpty(hash))
        {
            paths.Add(PathForHash(hash));
            paths.Add(PathForHash(hash, LargePreviewSuffix));
        }

        if (LargePreviewPathFor(filePath) is { } lg)
        {
            paths.Add(lg);
        }

        foreach (var path in paths)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                ThumbStatus.Delete(path);
            }
            catch
            {
                // Best-effort cache cleanup.
            }
        }
    }

    /// <summary>
    /// Ensures a mosaic JPEG exists under the Palace thumb cache — never
    /// beside the source. Online-only: cached JPEG only; do not
    /// <see cref="StorageFile.GetThumbnailAsync"/> or WIC-open the original
    /// (AVIF still recalls Dropbox). Local video / AVIF: provider thumb only.
    /// Local video / AVIF / HEIC / PSD: provider thumb only. Other local
    /// images may WIC-decode the original. Missing / API-only paths return
    /// a cached JPEG if present.
    /// </summary>
    /// <param name="requireKnownStatus">Scan: regenerate a current image thumbnail whose placeholder status
    /// is unknown (cached before <see cref="ThumbStatus"/>), so the catalog's undecodable flag is accurate.</param>
    public async Task<ThumbnailInfo?> EnsureThumbnailAsync(
        string filePath,
        string? hash,
        Models.AssetKind kind,
        bool requireKnownStatus = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(hash))
        {
            return null;
        }

        if (!CloudFile.TryGetAttributes(filePath, out var attrs))
        {
            return ReadCached(hash);
        }

        var onlineOnly = CloudFile.IsOnlineOnly(attrs);
        if (onlineOnly || kind == Models.AssetKind.Other)
        {
            return ReadCached(hash);
        }

        var dest = PathForHash(hash);
        if (IsCachedCurrent(dest, filePath, kind)
            && !(requireKnownStatus && kind == Models.AssetKind.Image && ThumbStatus.Read(dest) is null))
        {
            TouchLastAccess(dest);
            return ReadCached(hash) ?? new ThumbnailInfo(dest, 0, 0, ThumbStatus.Read(dest) == true);
        }

        if (kind == Models.AssetKind.Image)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var custom = await TryCustomStillThumbAsync(filePath, dest, hash, cancellationToken).ConfigureAwait(false);
            if (custom is not null)
            {
                return custom;
            }
        }

        if (GalleryMedia.UsesShellThumbnail(kind, filePath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var shell = await EnsureShellThumbnailAsync(filePath, hash, cancellationToken).ConfigureAwait(false);
            if (shell is null && kind == Models.AssetKind.Video && FfmpegPoster.IsAvailable)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // No system codec (MKV H.264 4:4:4, WebM VP9/AV1 …): bundled ffmpeg.
                var tmp = dest + ".ff.jpg";
                if (await FfmpegPoster.TryExtractAsync(filePath, tmp, MaxSide).ConfigureAwait(false))
                {
                    ThumbFileReplace.TryMoveOverwrite(tmp, dest);
                    if (!File.Exists(dest))
                    {
                        return null;
                    }

                    var size = ImageDimensions.TryRead(dest);
                    return Finish(dest, size?.Width ?? 0, size?.Height ?? 0);
                }
            }

            return shell;
        }

        if (!GalleryMedia.MayOpenOriginalForThumb(onlineOnly, kind, filePath))
        {
            return ReadCached(hash);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = await StorageFile.GetFileFromPathAsync(filePath).AsTask(cancellationToken).ConfigureAwait(false);
            using var stream = await file.OpenAsync(FileAccessMode.Read).AsTask(cancellationToken).ConfigureAwait(false);
            return await EncodeScaledJpegAsync(stream, dest, hash, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return ReadCached(hash);
        }
    }

    /// <summary>
    /// Decode <paramref name="path"/> to BGRA already fitted inside the box.
    /// Runs off the UI thread. Does not point a <c>BitmapImage</c> at the file.
    /// </summary>
    public async Task<CappedBgra?> DecodeCappedBgraAsync(string? path, int boxWidth, int boxHeight, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(path) || GalleryMedia.IsRemoteUri(path))
        {
            return null;
        }

        if (!CloudFile.Exists(path) || CloudFile.IsOnlineOnly(path))
        {
            return null;
        }

        var sw = Stopwatch.StartNew();
        var file = await StorageFile.GetFileFromPathAsync(path).AsTask(cancellationToken).ConfigureAwait(false);
        using var stream = await file.OpenAsync(FileAccessMode.Read).AsTask(cancellationToken).ConfigureAwait(false);
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken).ConfigureAwait(false);
        var sourceWidth = (int)decoder.PixelWidth;
        var sourceHeight = (int)decoder.PixelHeight;
        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            return null;
        }

        var (width, height) = ThumbScheduler.FitDecodeSize(sourceWidth, sourceHeight, boxWidth, boxHeight);
        var transform = new BitmapTransform
        {
            ScaledWidth = (uint)width,
            ScaledHeight = (uint)height,
            InterpolationMode = BitmapInterpolationMode.Fant
        };
        var pixels = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            transform,
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.ColorManageToSRgb).AsTask(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var bgra = pixels.DetachPixelData();
        if (bgra.Length < width * height * 4)
        {
            return null;
        }

        return new CappedBgra(bgra, width, height, sw.Elapsed.TotalMilliseconds);
    }

    public async Task<ThumbnailInfo?> CacheJpegAsync(string hash, Stream jpegOrImage, string? suffix = null)
    {
        var dest = PathForHash(hash, suffix);
        try
        {
            Directory.CreateDirectory(_root);
            if (jpegOrImage.CanSeek)
            {
                jpegOrImage.Position = 0;
            }

            await using var output = File.Create(dest);
            await jpegOrImage.CopyToAsync(output).ConfigureAwait(false);
            ThumbStatus.Write(dest, isPlaceholder: false);
            TouchLastAccess(dest);
            EnforceCacheBudget();
            var written = ImageDimensions.TryRead(dest);
            return written is { } size
                ? new ThumbnailInfo(dest, size.Width, size.Height)
                : new ThumbnailInfo(dest, 0, 0);
        }
        catch
        {
            return ReadCached(hash, suffix);
        }
    }

    /// <summary>
    /// Decode any image stream, scale to <see cref="MaxSide"/>, and replace the
    /// mosaic JPEG for <paramref name="hash"/> under the thumb cache (never
    /// beside the source). Used for video-frame posters.
    /// </summary>
    public Task<ThumbnailInfo?> CacheScaledJpegAsync(string hash, IRandomAccessStream imageStream, string? suffix = null)
    {
        var dest = PathForHash(hash, suffix);
        return EncodeScaledJpegAsync(imageStream, dest, hash);
    }

    /// <summary>
    /// Windows shell / provider poster. Local video / AVIF / HEIC / PSD
    /// only — never the online-only path (HEIF/AVIF handlers open the original).
    /// </summary>
    private async Task<ThumbnailInfo?> EnsureShellThumbnailAsync(string filePath, string hash, CancellationToken cancellationToken)
    {
        var cached = ReadCached(hash);
        if (cached is not null)
        {
            return cached;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = await StorageFile.GetFileFromPathAsync(filePath).AsTask(cancellationToken).ConfigureAwait(false);
            using var thumb = await file.GetThumbnailAsync(ThumbnailMode.PicturesView, MaxSide).AsTask(cancellationToken).ConfigureAwait(false);
            if (thumb is null || !GalleryMedia.AcceptsProviderThumbnail(thumb.Type == ThumbnailType.Image))
            {
                return null;
            }

            var dest = PathForHash(hash);
            return await EncodeScaledJpegAsync(thumb, dest, hash, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private async Task<ThumbnailInfo?> EncodeScaledJpegAsync(
        IRandomAccessStream stream,
        string dest,
        string hash,
        int maxSide = MaxSide,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken).ConfigureAwait(false);
        var width = decoder.PixelWidth;
        var height = decoder.PixelHeight;
        if (width == 0 || height == 0)
        {
            return null;
        }

        var scale = Math.Min(maxSide / (double)width, maxSide / (double)height);
        scale = Math.Min(scale, 1.0);
        var tw = Math.Max(1u, (uint)Math.Round(width * scale));
        var th = Math.Max(1u, (uint)Math.Round(height * scale));
        var transform = new BitmapTransform
        {
            ScaledWidth = tw,
            ScaledHeight = th,
            InterpolationMode = BitmapInterpolationMode.Fant
        };
        var pixels = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            transform,
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.ColorManageToSRgb).AsTask(cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        var part = dest + ".part";
        try
        {
            var folder = await StorageFolder.GetFolderFromPathAsync(_root).AsTask(cancellationToken).ConfigureAwait(false);
            var outFile = await folder.CreateFileAsync(Path.GetFileName(part), CreationCollisionOption.ReplaceExisting).AsTask(cancellationToken).ConfigureAwait(false);
            using var outStream = await outFile.OpenAsync(FileAccessMode.ReadWrite).AsTask(cancellationToken).ConfigureAwait(false);
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, outStream).AsTask(cancellationToken).ConfigureAwait(false);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, tw, th, 96, 96, pixels.DetachPixelData());
            await encoder.FlushAsync().AsTask(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            ThumbFileReplace.TryDelete(part);
            throw;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            ThumbFileReplace.TryDelete(part);
            return ExistingThumb(dest, (int)width, (int)height);
        }

        return CommitThumbFile(part, dest, (int)tw, (int)th, (int)width, (int)height);
    }

    /// <summary>
    /// Move a finished JPEG onto <paramref name="dest"/>. If another decode has
    /// <paramref name="dest"/> open, keep that file and do not throw.
    /// </summary>
    private ThumbnailInfo? CommitThumbFile(string part, string dest, int encodedWidth, int encodedHeight, int sourceWidth, int sourceHeight)
    {
        ThumbFileReplace.TryMoveOverwrite(part, dest);
        return ExistingThumb(dest, encodedWidth > 0 ? encodedWidth : sourceWidth, encodedHeight > 0 ? encodedHeight : sourceHeight);
    }

    private ThumbnailInfo? ExistingThumb(string dest, int width, int height)
    {
        if (!File.Exists(dest))
        {
            return null;
        }

        ThumbStatus.Write(dest, isPlaceholder: false);
        TouchLastAccess(dest);
        EnforceCacheBudget();
        var written = ImageDimensions.TryRead(dest);
        return written is { } size
            ? new ThumbnailInfo(dest, size.Width, size.Height)
            : new ThumbnailInfo(dest, width, height);
    }

    /// <summary>Side of the cached full-view JPEG for stills Windows cannot show.</summary>
    public const int LargePreviewMaxSide = 2048;

    /// <summary>
    /// Cached full-view JPEG for EXR / Radiance / TGA / Clip / non-image
    /// stills that <c>BitmapImage</c> cannot decode. Keyed by path, size and
    /// mtime so the gallery can resolve it without the content hash.
    /// </summary>
    public string? ExistingLargePreviewForPath(string? filePath)
    {
        var path = LargePreviewPathFor(filePath);
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        try
        {
            // Stale once the source changes; regenerated under the same name.
            return !File.Exists(filePath) || File.GetLastWriteTimeUtc(path) >= File.GetLastWriteTimeUtc(filePath!)
                ? path
                : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Keyed by path only, so it can be found (and deleted) without the file or its hash.</summary>
    private string? LargePreviewPathFor(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return null;
        }

        var digest = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(
            System.Text.Encoding.UTF8.GetBytes(filePath.ToUpperInvariant())));
        return PathForHash("p" + digest, LargePreviewSuffix);
    }

    private bool NeedsLargePreviewBackfill(Models.AssetKind kind, string filePath) =>
        kind == Models.AssetKind.Image
        && ExistingLargePreviewForPath(filePath) is null
        && (StillDecode.HandlesPath(filePath)
            // Shelf metadata under a .bmp/.tif name: full view needs the per-path preview
            // even when an identical file's hash-keyed mosaic thumb is already cached.
            || (!CloudFile.IsOnlineOnly(filePath) && SubstanceShelfFile.IsShelfMetadataFile(filePath)));

    /// <summary>
    /// Custom still paths: EXR / Radiance / TGA / Clip decoders, HDR stills
    /// tone-mapped from scRGB (WinRT unorm decode shows HDR PNG/JXR/AVIF as
    /// clipped red), and a placeholder tile for files that are not images.
    /// Returns null to fall through to the WinRT / shell thumbnail.
    /// </summary>
    private async Task<ThumbnailInfo?> TryCustomStillThumbAsync(string filePath, string dest, string hash, CancellationToken cancellationToken)
    {
        try
        {
            byte[] head;
            await using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                head = new byte[64];
                var n = await fs.ReadAsync(head).ConfigureAwait(false);
                Array.Resize(ref head, n);
            }

            var ext = PathSafe.Extension(filePath);
            if (StillDecode.IsNotAnImage(head, ext))
            {
                if (SubstanceShelfFile.IsShelfMetadata(head)
                    && await TryShelfPreviewThumbAsync(filePath, dest, hash, cancellationToken).ConfigureAwait(false) is { } shelf)
                {
                    return shelf;
                }

                var ph = StillDecode.Placeholder();
                await EncodeBgraJpegAsync(ph.Bgra!, ph.Width, ph.Height, dest).ConfigureAwait(false);
                await EncodeBgraJpegAsync(ph.Bgra!, ph.Width, ph.Height, LargePreviewPathFor(filePath)).ConfigureAwait(false);
                return Finish(dest, ph.Width, ph.Height, isPlaceholder: true);
            }

            if (StillDecode.HandlesPath(filePath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var large = await Task.Run(() => StillDecode.TryDecodeFile(filePath, LargePreviewMaxSide, cancellationToken), cancellationToken).ConfigureAwait(false);
                if (large is null)
                {
                    var ph = StillDecode.Placeholder();
                    await EncodeBgraJpegAsync(ph.Bgra!, ph.Width, ph.Height, dest).ConfigureAwait(false);
                    await EncodeBgraJpegAsync(ph.Bgra!, ph.Width, ph.Height, LargePreviewPathFor(filePath)).ConfigureAwait(false);
                    return Finish(dest, ph.Width, ph.Height, isPlaceholder: true);
                }

                if (large.EncodedImage is { } encoded)
                {
                    // Clip Studio canvas preview (PNG) — let WinRT decode it.
                    using var ras = new InMemoryRandomAccessStream();
                    await ras.WriteAsync(encoded.AsBuffer());
                    ras.Seek(0);
                    var lgPath = LargePreviewPathFor(filePath);
                    if (lgPath is not null)
                    {
                        await EncodeScaledJpegAsync(ras, lgPath, hash, LargePreviewMaxSide, cancellationToken).ConfigureAwait(false);
                        ras.Seek(0);
                    }

                    return await EncodeScaledJpegAsync(ras, dest, hash, cancellationToken: cancellationToken).ConfigureAwait(false);
                }

                await EncodeBgraJpegAsync(large.Bgra!, large.Width, large.Height, LargePreviewPathFor(filePath)).ConfigureAwait(false);
                var (tw, th) = StillDecode.Fit(large.Width, large.Height, MaxSide);
                var small = tw == large.Width && th == large.Height
                    ? large.Bgra!
                    : StillDecode.BoxScaleBgra(large.Bgra!, large.Width, large.Height, tw, th, cancellationToken);
                await EncodeBgraJpegAsync(small, tw, th, dest).ConfigureAwait(false);
                return Finish(dest, tw, th);
            }

            if (PathSafe.IsJxl(filePath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Probe transfer so PQ/HLG uses 80-nit scRGB before ToneMap;
                // SDR / gain-map base stays relative 0..1 (no ×2.54 hot thumbs).
                var jxlProbe = HdrFile.ProbePath(filePath);
                var jxlTransfer = jxlProbe.Kind == HdrKind.HdrJxl ? jxlProbe.Transfer : HdrTransfer.Srgb;
                var jxl = await Task.Run(
                    () => JxlDecode.TryDecodeFile(filePath, HdrJxlPresent.ThumbMaxPixels, jxlTransfer, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
                if (jxl is not null)
                {
                    var lgJ = await Task.Run(
                        () => StillDecode.FromScrgb(jxl.Rgba, jxl.Width, jxl.Height, LargePreviewMaxSide, cancellationToken),
                        cancellationToken).ConfigureAwait(false);
                    await EncodeBgraJpegAsync(lgJ.Bgra!, lgJ.Width, lgJ.Height, LargePreviewPathFor(filePath)).ConfigureAwait(false);
                    var (jw, jh) = StillDecode.Fit(lgJ.Width, lgJ.Height, MaxSide);
                    var smallJ = jw == lgJ.Width && jh == lgJ.Height ? lgJ.Bgra! : StillDecode.BoxScaleBgra(lgJ.Bgra!, lgJ.Width, lgJ.Height, jw, jh, cancellationToken);
                    await EncodeBgraJpegAsync(smallJ, jw, jh, dest).ConfigureAwait(false);
                    return Finish(dest, jw, jh);
                }
            }

            var probe = HdrFile.ProbePath(filePath);
            if (probe.IsHdr && probe.Kind != HdrKind.UltraHdrJpeg)
            {
                var frame = await HdrWicDecode.TryLoadAsync(filePath, probe, MaxSide, MaxSide, ImageScaling.Fit, cancellationToken)
                    .ConfigureAwait(false);
                if (frame is not null && frame.Width > 0 && frame.Height > 0)
                {
                    var r = await Task.Run(() => StillDecode.FromScrgb(frame.ScrgbRgba, frame.Width, frame.Height, MaxSide)).ConfigureAwait(false);
                    await EncodeBgraJpegAsync(r.Bgra!, r.Width, r.Height, dest).ConfigureAwait(false);
                    return Finish(dest, r.Width, r.Height);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Fall back to WinRT / shell thumbnail.
        }

        return null;
    }

    /// <summary>
    /// Substance Painter shelf metadata saved under an image name: thumbnail and full view
    /// come from its embedded preview. Null when it has none or the preview won't decode.
    /// </summary>
    private async Task<ThumbnailInfo?> TryShelfPreviewThumbAsync(string filePath, string dest, string hash, CancellationToken cancellationToken)
    {
        try
        {
            if (new FileInfo(filePath).Length > SubstanceShelfFile.MaxBytes)
            {
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var data = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
            var preview = SubstanceShelfFile.TryExtractPreview(data);
            if (preview is null)
            {
                return null;
            }

            using var ras = new InMemoryRandomAccessStream();
            await ras.WriteAsync(preview.AsBuffer());
            ras.Seek(0);
            if (LargePreviewPathFor(filePath) is { } lgPath)
            {
                await EncodeScaledJpegAsync(ras, lgPath, hash, LargePreviewMaxSide, cancellationToken).ConfigureAwait(false);
                ras.Seek(0);
            }

            return await EncodeScaledJpegAsync(ras, dest, hash, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // No WebP codec, truncated preview, …: placeholder.
            return null;
        }
    }

    private ThumbnailInfo Finish(string dest, int w, int h, bool isPlaceholder = false)
    {
        ThumbStatus.Write(dest, isPlaceholder);
        TouchLastAccess(dest);
        EnforceCacheBudget();
        return new ThumbnailInfo(dest, w, h, isPlaceholder);
    }

    private async Task EncodeBgraJpegAsync(byte[] bgra, int width, int height, string? dest)
    {
        if (dest is null)
        {
            return;
        }

        // JPEG has no alpha: composite straight alpha over the dark tile colour.
        var flat = new byte[bgra.Length];
        for (var i = 0; i < bgra.Length; i += 4)
        {
            var a = bgra[i + 3];
            for (var c = 0; c < 3; c++)
            {
                flat[i + c] = (byte)((bgra[i + c] * a + 32 * (255 - a) + 127) / 255);
            }

            flat[i + 3] = 255;
        }

        Directory.CreateDirectory(_root);
        var part = dest + ".part";
        try
        {
            var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(dest)!);
            var outFile = await folder.CreateFileAsync(Path.GetFileName(part), CreationCollisionOption.ReplaceExisting);
            using var outStream = await outFile.OpenAsync(FileAccessMode.ReadWrite);
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, outStream);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, (uint)width, (uint)height, 96, 96, flat);
            await encoder.FlushAsync();
            ThumbFileReplace.TryMoveOverwrite(part, dest);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            ThumbFileReplace.TryDelete(part);
        }
    }

    private ThumbnailInfo? ReadCached(string hash, string? suffix = null)
    {
        var dest = PathForHash(hash, suffix);
        if (!File.Exists(dest))
        {
            return null;
        }

        TouchLastAccess(dest);
        var placeholder = ThumbStatus.Read(dest) == true;
        var existing = ImageDimensions.TryRead(dest);
        return existing is { } size
            ? new ThumbnailInfo(dest, size.Width, size.Height, placeholder)
            : new ThumbnailInfo(dest, 0, 0, placeholder);
    }

    private void EnforceCacheBudget()
    {
        if (!MediaCache.Enabled)
        {
            return;
        }

        try
        {
            var cacheRoot = Directory.GetParent(_root)?.FullName;
            if (string.IsNullOrEmpty(cacheRoot))
            {
                return;
            }

            // Off UI thread: callers already await Encode/Cache on a pool or
            // continue from async; keep eviction out of the dispatcher.
            MediaCache.EnforceMaxSize(cacheRoot);
        }
        catch
        {
            // Budget enforcement is best-effort.
        }
    }

    private static void TouchLastAccess(string path)
    {
        try
        {
            File.SetLastAccessTimeUtc(path, DateTime.UtcNow);
        }
        catch
        {
            // Some volumes ignore LastAccessTime.
        }
    }

    private static bool ShouldRegenerate(string dest, string originalPath)
    {
        if (CloudFile.IsOnlineOnly(originalPath))
        {
            return false;
        }

        return GalleryMedia.ShouldRegenerateCachedThumb(ImageDimensions.TryRead(dest), MaxSide);
    }

    internal static string SanitizeHash(string hash) => ThumbFileName.Sanitize(hash);
}
