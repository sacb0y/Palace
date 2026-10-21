namespace Palace.Helpers;

/// <summary>
/// Review hotkeys only. Touch swipe classify lives in <see cref="TouchGestures"/>.
/// X = reject, K = keep, S = skip.
/// </summary>
public static class ReviewHotkeys
{
    public const int KeyLetterX = 88;
    public const int KeyLetterK = 75;
    public const int KeyLetterS = 83;
    public const int KeyLetterZ = 90;

    public static TouchGestures.SwipeAction FromKey(int virtualKey, bool controlDown = false)
    {
        if (controlDown)
        {
            return TouchGestures.SwipeAction.None;
        }

        return virtualKey switch
        {
            KeyLetterX => TouchGestures.SwipeAction.Reject,
            KeyLetterK => TouchGestures.SwipeAction.Keep,
            KeyLetterS => TouchGestures.SwipeAction.Skip,
            _ => TouchGestures.SwipeAction.None
        };
    }

    public static bool IsDecisionKey(int virtualKey, bool controlDown = false) =>
        FromKey(virtualKey, controlDown) is TouchGestures.SwipeAction.Reject
            or TouchGestures.SwipeAction.Keep
            or TouchGestures.SwipeAction.Skip;

    public static bool IsUndoChord(bool controlDown, int virtualKey) =>
        controlDown && virtualKey == KeyLetterZ;
}
