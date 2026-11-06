using DockerPortMirror.Ssh;

namespace DockerPortMirror.Tests.Ssh;

public sealed class BoundedLineBufferTests
{
    [Fact]
    public void Only_the_last_lines_are_kept()
    {
        var buffer = new BoundedLineBuffer(3);

        for (var i = 1; i <= 1000; i++)
            buffer.Add($"line {i}");

        Assert.Equal(["line 998", "line 999", "line 1000"], buffer.ToList());
    }

    [Fact]
    public void Fewer_lines_than_capacity_are_all_kept()
    {
        var buffer = new BoundedLineBuffer(3);
        buffer.Add("a");

        Assert.Equal(["a"], buffer.ToList());
    }
}
