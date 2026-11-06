using DockerPortMirror.Mirroring;

namespace DockerPortMirror.Tests.Mirroring;

public sealed class PortSetDiffTests
{
    [Fact]
    public void Equal_sets_give_an_empty_diff()
    {
        var diff = PortSetDiff.Compute([1, 2], [2, 1]);

        Assert.True(diff.IsEmpty);
    }

    [Fact]
    public void New_items_are_added_and_missing_items_removed()
    {
        var diff = PortSetDiff.Compute([80, 443, 5432], [443, 8080]);

        Assert.Equal([8080], diff.ToAdd);
        Assert.Equal([80, 5432], diff.ToRemove.Order());
    }

    [Fact]
    public void From_empty_everything_is_added()
    {
        var diff = PortSetDiff.Compute(Array.Empty<int>(), [3000]);

        Assert.Equal([3000], diff.ToAdd);
        Assert.Empty(diff.ToRemove);
    }
}
