namespace DockerPortMirror.Mirroring;

/// <summary>
/// Exponential backoff per tunnel (1s, 2s, 4s ... 60s), so a port that cannot be mirrored is not retried
/// and reported every poll.
/// </summary>
internal sealed class RetryPolicy
{
    public static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan MaximumDelay = TimeSpan.FromSeconds(60);

    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<TunnelSpec, Entry> _entries = new();

    public RetryPolicy(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    /// <summary>Whether the tunnel has no pending backoff.</summary>
    public bool IsDue(TunnelSpec spec) =>
        !_entries.TryGetValue(spec, out var entry) || _timeProvider.GetUtcNow() >= entry.NextAttempt;

    /// <summary>Reads the reason of the last failure and the time of the next attempt of a tunnel in backoff.</summary>
    public bool TryGetPending(TunnelSpec spec, out string reason, out DateTimeOffset nextAttempt)
    {
        if (_entries.TryGetValue(spec, out var entry))
        {
            reason = entry.LastReason;
            nextAttempt = entry.NextAttempt;
            return true;
        }

        reason = "";
        nextAttempt = default;
        return false;
    }

    /// <summary>Records a failure and returns how long to wait and whether it is worth reporting.</summary>
    public RetryDecision Fail(TunnelSpec spec, string reason)
    {
        _entries.TryGetValue(spec, out var previous);
        var failures = (previous?.Failures ?? 0) + 1;
        var delay = DelayFor(failures);
        _entries[spec] = new Entry(failures, _timeProvider.GetUtcNow() + delay, reason);
        var isNewState = previous is null || !string.Equals(previous.LastReason, reason, StringComparison.Ordinal);
        return new RetryDecision(delay, failures, isNewState);
    }

    /// <summary>Forgets the failures of a tunnel that is healthy again.</summary>
    public void Reset(TunnelSpec spec) => _entries.Remove(spec);

    /// <summary>Forgets every tunnel that is no longer wanted.</summary>
    public void Retain(IReadOnlySet<TunnelSpec> wanted)
    {
        foreach (var spec in _entries.Keys.Where(spec => !wanted.Contains(spec)).ToArray())
            _entries.Remove(spec);
    }

    internal static TimeSpan DelayFor(int failures)
    {
        var exponent = Math.Min(failures - 1, 16);
        var delay = InitialDelay * Math.Pow(2, exponent);
        return delay < MaximumDelay ? delay : MaximumDelay;
    }

    private sealed record Entry(int Failures, DateTimeOffset NextAttempt, string LastReason);
}

/// <param name="Delay">How long to wait before the next attempt.</param>
/// <param name="Failures">How many times in a row the tunnel failed.</param>
/// <param name="IsNewState">True for the first failure or a failure with a different reason: report it as a warning.</param>
internal readonly record struct RetryDecision(TimeSpan Delay, int Failures, bool IsNewState);
