using DockerPortMirror.Ssh;

namespace DockerPortMirror.Tests.Ssh;

public sealed class SshTargetTests
{
    [Theory]
    [InlineData("ssh://macdocker", "macdocker")]
    [InlineData("ssh://user@host", "user@host")]
    [InlineData("SSH://Host", "Host")]
    [InlineData("ssh://user@host/some/path", "user@host")]
    [InlineData("ssh://us%40er@host", "us@er@host")]
    [InlineData("ssh://user@host:2222", "user@host:2222")]
    public void Ssh_endpoints_give_a_target(string endpoint, string expected)
    {
        Assert.Equal(expected, SshTarget.FromEndpoint(endpoint));
    }

    [Theory]
    [InlineData("unix:///var/run/docker.sock")]
    [InlineData("npipe:////./pipe/docker_engine")]
    [InlineData("tcp://192.168.1.5:2376")]
    public void Non_ssh_endpoints_give_no_target(string endpoint)
    {
        Assert.Null(SshTarget.FromEndpoint(endpoint));
    }
}
