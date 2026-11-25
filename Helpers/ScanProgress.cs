namespace Palace.Helpers;

/// <summary>
/// Structured scan / import progress (D7). Prefer this over a bare filename string
/// so the UI can show phase + current file and cancel long work.
/// Done/Total are optional (0 = unknown); callers that know a count should pass them.
/// </summary>
public readonly record struct ScanProgress(
    string Phase,
    string? CurrentPath = null,
    int Done = 0,
    int Total = 0)
{
    public static ScanProgress File(string path, string phase = "index", int done = 0, int total = 0) =>
        new(phase, path, done, total);

    public string FileName =>
        string.IsNullOrEmpty(CurrentPath) ? "" : System.IO.Path.GetFileName(CurrentPath);

    public override string ToString() =>
        string.IsNullOrEmpty(CurrentPath) ? Phase : CurrentPath;
}
