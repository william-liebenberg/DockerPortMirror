using DockerPortMirror.Cli;

namespace DockerPortMirror.Tests.Cli;

public sealed class MirrorOptionsValidatorTests
{
    private static readonly MirrorOptionsValidator Validator = new();

    [Fact]
    public void Defaults_are_valid()
    {
        Assert.True(Validator.Validate(null, new MirrorOptions()).Succeeded);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("localhost")]
    [InlineData("LOCALHOST")]
    public void Loopback_bind_addresses_are_valid(string bindAddress)
    {
        Assert.True(Validator.Validate(null, new MirrorOptions { BindAddress = bindAddress }).Succeeded);
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("192.168.1.10")]
    [InlineData("")]
    public void Other_bind_addresses_are_rejected(string bindAddress)
    {
        var result = Validator.Validate(null, new MirrorOptions { BindAddress = bindAddress });

        Assert.True(result.Failed);
        Assert.Contains("bind address", result.FailureMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3601)]
    public void Out_of_range_poll_is_rejected(int seconds)
    {
        Assert.True(Validator.Validate(null, new MirrorOptions { PollSeconds = seconds }).Failed);
    }

    [Fact]
    public void Blank_context_or_target_is_rejected()
    {
        Assert.True(Validator.Validate(null, new MirrorOptions { Context = " " }).Failed);
        Assert.True(Validator.Validate(null, new MirrorOptions { SshTarget = "" }).Failed);
    }
}
