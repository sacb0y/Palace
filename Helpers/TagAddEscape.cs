namespace Palace.Helpers;

/// <summary>T2 Add tags Escape: first clears pending/typed input; second unfocuses the box.</summary>
public enum TagAddEscapeAction
{
    ClearInput = 0,
    Unfocus = 1
}

public static class TagAddEscape
{
    public static TagAddEscapeAction Resolve(bool hasPendingChips, bool hasTypedText) =>
        hasPendingChips || hasTypedText
            ? TagAddEscapeAction.ClearInput
            : TagAddEscapeAction.Unfocus;
}
