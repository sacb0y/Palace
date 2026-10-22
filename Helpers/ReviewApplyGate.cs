namespace Palace.Helpers;

/// <summary>
/// Serializes Review Keep / Reject / Skip so rapid X then K cannot misattribute,
/// and captures the session asset id at start so a late complete only records
/// when the session is still on that id. WinUI-free for Palace.Tests.
/// </summary>
public sealed class ReviewApplyGate
{
    private bool _busy;
    private string? _capturedId;

    public bool IsBusy => _busy;

    public string? CapturedId => _capturedId;

    /// <summary>
    /// Begin a decision for <paramref name="sessionCurrentId"/>. Returns false
    /// when already busy or there is no current id.
    /// </summary>
    public bool TryBegin(string? sessionCurrentId, out string assetId)
    {
        assetId = "";
        if (_busy || string.IsNullOrWhiteSpace(sessionCurrentId))
        {
            return false;
        }

        _busy = true;
        _capturedId = sessionCurrentId;
        assetId = sessionCurrentId;
        return true;
    }

    /// <summary>
    /// Begin an Undo (or other session op) without capturing an asset id.
    /// Returns false when a decision / undo is already in flight.
    /// </summary>
    public bool TryBeginBusy()
    {
        if (_busy)
        {
            return false;
        }

        _busy = true;
        _capturedId = null;
        return true;
    }

    /// <summary>
    /// True when the session is still on the id captured at <see cref="TryBegin"/>.
    /// </summary>
    public bool StillOnCaptured(string? sessionCurrentId) =>
        _busy
        && !string.IsNullOrWhiteSpace(_capturedId)
        && string.Equals(_capturedId, sessionCurrentId, StringComparison.Ordinal);

    public void End()
    {
        _busy = false;
        _capturedId = null;
    }
}
