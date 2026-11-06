using System.Net;
using DockerPortMirror.Mirroring;
using DockerPortMirror.Tests.Fakes;

namespace DockerPortMirror.Tests.Mirroring;

public sealed class RetryPolicyTests
{
    private static readonly TunnelSpec Spec = new("127.0.0.1", 3000, IPAddress.Loopback, 3000);

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(6, 32)]
    [InlineData(7, 60)]
    [InlineData(100, 60)]
    public void Delay_doubles_up_to_a_minute(int failures, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), RetryPolicy.DelayFor(failures));
    }

    [Fact]
    public void Unknown_tunnel_is_due()
    {
        Assert.True(new RetryPolicy(new ManualTimeProvider()).IsDue(Spec));
    }

    [Fact]
    public void Failed_tunnel_is_due_after_its_delay()
    {
        var time = new ManualTimeProvider();
        var policy = new RetryPolicy(time);

        policy.Fail(Spec, "in use");
        policy.Fail(Spec, "in use");

        Assert.False(policy.IsDue(Spec));
        time.Advance(TimeSpan.FromSeconds(1.5));
        Assert.False(policy.IsDue(Spec));
        time.Advance(TimeSpan.FromSeconds(0.5));
        Assert.True(policy.IsDue(Spec));
    }

    [Fact]
    public void Only_the_first_failure_and_changes_of_reason_are_new_states()
    {
        var policy = new RetryPolicy(new ManualTimeProvider());

        Assert.True(policy.Fail(Spec, "in use").IsNewState);
        Assert.False(policy.Fail(Spec, "in use").IsNewState);
        Assert.True(policy.Fail(Spec, "connection refused").IsNewState);
    }

    [Fact]
    public void Reset_forgets_failures()
    {
        var policy = new RetryPolicy(new ManualTimeProvider());
        policy.Fail(Spec, "in use");

        policy.Reset(Spec);

        Assert.True(policy.IsDue(Spec));
        Assert.Equal(1, policy.Fail(Spec, "in use").Failures);
    }

    [Fact]
    public void Retain_forgets_unwanted_tunnels()
    {
        var policy = new RetryPolicy(new ManualTimeProvider());
        policy.Fail(Spec, "in use");

        policy.Retain(new HashSet<TunnelSpec>());

        Assert.True(policy.IsDue(Spec));
    }
}
