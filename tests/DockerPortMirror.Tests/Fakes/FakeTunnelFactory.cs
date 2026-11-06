using DockerPortMirror.Mirroring;

namespace DockerPortMirror.Tests.Fakes;

internal sealed class FakeTunnel : ITunnel
{
    private readonly TaskCompletionSource<TunnelExit> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public FakeTunnel(TunnelSpec spec)
    {
        Spec = spec;
    }

    public TunnelSpec Spec { get; }

    public string Via => "fakehost";

    public Task<TunnelExit> Completion => _completion.Task;

    public bool IsDisposed { get; private set; }

    public void Exit(string error) => _completion.TrySetResult(new TunnelExit(255, [error]));

    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        _completion.TrySetResult(new TunnelExit(null, []));
        return ValueTask.CompletedTask;
    }
}

internal sealed class FakeTunnelFactory : ITunnelFactory
{
    public List<TunnelSpec> Attempts { get; } = [];

    public List<FakeTunnel> Opened { get; } = [];

    /// <summary>Local ports whose next attempts fail, with the error message.</summary>
    public Dictionary<int, string> Failures { get; } = [];

    public Task<ITunnel> OpenAsync(TunnelSpec spec, CancellationToken cancellationToken)
    {
        Attempts.Add(spec);
        if (Failures.TryGetValue(spec.LocalPort, out var error))
            throw new TunnelStartException(error);

        var tunnel = new FakeTunnel(spec);
        Opened.Add(tunnel);
        return Task.FromResult<ITunnel>(tunnel);
    }
}
