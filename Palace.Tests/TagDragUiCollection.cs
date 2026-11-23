using Xunit;

namespace Palace.Tests;

/// <summary>
/// Serializes TagDragUi / TagChipCapturePointer tests that mutate shared static probe state.
/// </summary>
[CollectionDefinition(Name)]
public sealed class TagDragUiCollection
{
    public const string Name = "TagDragUi static state";
}
