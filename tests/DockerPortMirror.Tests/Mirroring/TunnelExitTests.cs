using DockerPortMirror.Mirroring;

namespace DockerPortMirror.Tests.Mirroring;

public sealed class TunnelExitTests
{
    [Fact]
    public void Reason_is_the_last_error_lines_without_diagnostics()
    {
        var exit = new TunnelExit(255, ["debug1: Connecting", "bind [127.0.0.1]:5432: Address already in use", "debug1: exit", "channel_setup_fwd_listener_tcpip: cannot listen to port: 5432"]);

        Assert.Equal("bind [127.0.0.1]:5432: Address already in use channel_setup_fwd_listener_tcpip: cannot listen to port: 5432", exit.Reason);
    }

    [Fact]
    public void Reason_falls_back_to_the_exit_code()
    {
        Assert.Equal("exit code 255", new TunnelExit(255, ["debug1: only diagnostics"]).Reason);
        Assert.Equal("stopped", new TunnelExit(null, []).Reason);
    }
}
