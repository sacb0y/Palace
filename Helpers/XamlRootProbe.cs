using System.Globalization;

namespace Palace.Helpers;

/// <summary>
/// FENRIR-STUDIO harness XamlRoot gate. Test-identity packages write Size /
/// RasterizationScale / pid under LocalState so <c>Assert-PalaceClientDipGate
/// -RequireXamlRoot</c> can match client DIP (never system-DPI math).
/// </summary>
public static class XamlRootProbe
{
    public const string FileName = "palace-xamlroot.log";

    /// <summary>Exact allowlist — same as ui-tests C1 (never C20A7629 / wildcards).</summary>
    public const string AllowedTestPackageName = "PalaceOrganizeTest-5E1F";

    public static bool ShouldWrite(string? packageName) =>
        string.Equals(packageName, AllowedTestPackageName, StringComparison.OrdinalIgnoreCase);

    public static string ResolvePath(string localFolderPath) =>
        Path.Combine(localFolderPath, FileName);

    public static string FormatLine(double width, double height, double scale, int pid) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{DateTime.UtcNow:O}\tXamlRoot.Size={width}x{height}\tRasterizationScale={scale}\tpid={pid}");

    /// <summary>
    /// Parse the latest probe line. Returns false when missing Size / scale / pid.
    /// </summary>
    public static bool TryParseLatest(
        string text,
        out double width,
        out double height,
        out double scale,
        out int pid)
    {
        width = 0;
        height = 0;
        scale = 0;
        pid = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i];
            if (!line.Contains("XamlRoot.Size=", StringComparison.Ordinal))
            {
                continue;
            }

            var sizeIdx = line.IndexOf("XamlRoot.Size=", StringComparison.Ordinal);
            var sizePart = line[(sizeIdx + "XamlRoot.Size=".Length)..];
            var tab = sizePart.IndexOf('\t');
            if (tab >= 0)
            {
                sizePart = sizePart[..tab];
            }

            var x = sizePart.IndexOf('x');
            if (x <= 0
                || !double.TryParse(
                    sizePart[..x],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out width)
                || !double.TryParse(
                    sizePart[(x + 1)..],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out height))
            {
                continue;
            }

            var scaleIdx = line.IndexOf("RasterizationScale=", StringComparison.Ordinal);
            if (scaleIdx < 0)
            {
                continue;
            }

            var scalePart = line[(scaleIdx + "RasterizationScale=".Length)..];
            tab = scalePart.IndexOf('\t');
            if (tab >= 0)
            {
                scalePart = scalePart[..tab];
            }

            if (!double.TryParse(
                    scalePart,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out scale))
            {
                continue;
            }

            var pidIdx = line.IndexOf("pid=", StringComparison.Ordinal);
            if (pidIdx < 0)
            {
                continue;
            }

            var pidPart = line[(pidIdx + "pid=".Length)..];
            tab = pidPart.IndexOf('\t');
            if (tab >= 0)
            {
                pidPart = pidPart[..tab];
            }

            if (!int.TryParse(pidPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out pid)
                || pid <= 0)
            {
                continue;
            }

            return true;
        }

        return false;
    }
}
