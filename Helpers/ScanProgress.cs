namespace Palace.Helpers;

/// <summary>
/// Structured scan / import progress (D7). Prefer this over a bare filename string
/// so the UI can show done/total and cancel long 3D thumbnail work.
/// </summary>
public readonly record struct ScanProgress(
    int Done,
    int Total,
    string Phase,
    string? CurrentPath = null)
{
    public static ScanProgress File(string path, int done = 0, int total = 0, string phase = "index") =>
        new(done, total, phase, path);

    public string FileName =>
        string.IsNullOrEmpty(CurrentPath) ? "" : System.IO.Path.GetFileName(CurrentPath);

    public override string ToString() =>
        string.IsNullOrEmpty(CurrentPath) ? Phase : CurrentPath;
}
