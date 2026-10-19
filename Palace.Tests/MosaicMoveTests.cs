using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class MosaicMoveTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "mm");

    [Fact]
    public void Moving_out_of_the_viewed_folder_prunes_in_place()
    {
        Assert.Equal(MosaicMoveUpdate.Prune,
            MosaicMove.Plan(Path.Combine(Root, "A"), flatView: false, Path.Combine(Root, "B")));
        Assert.Equal(MosaicMoveUpdate.Prune,
            MosaicMove.Plan(Path.Combine(Root, "A"), flatView: false, Path.Combine(Root, "AB")));
    }

    [Fact]
    public void Moving_into_a_subfolder_of_the_viewed_folder_requeries_so_headers_regroup()
    {
        Assert.Equal(MosaicMoveUpdate.Requery,
            MosaicMove.Plan(Root, flatView: false, Path.Combine(Root, "A", "B")));
    }

    [Fact]
    public void Flat_views_update_paths_in_place()
    {
        Assert.Equal(MosaicMoveUpdate.UpdateInPlace, MosaicMove.Plan(null, flatView: false, Root));
        Assert.Equal(MosaicMoveUpdate.UpdateInPlace, MosaicMove.Plan(Root, flatView: true, Path.Combine(Root, "x")));
    }
}
