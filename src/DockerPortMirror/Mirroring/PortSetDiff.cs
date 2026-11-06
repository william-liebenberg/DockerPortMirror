namespace DockerPortMirror.Mirroring;

/// <summary>What to add and remove to turn the current set into the desired set.</summary>
internal sealed record PortSetDiff<T>(IReadOnlyList<T> ToAdd, IReadOnlyList<T> ToRemove)
{
    public bool IsEmpty => ToAdd.Count == 0 && ToRemove.Count == 0;
}

internal static class PortSetDiff
{
    public static PortSetDiff<T> Compute<T>(IEnumerable<T> current, IEnumerable<T> desired)
    {
        var currentSet = current.ToHashSet();
        var desiredSet = desired.ToHashSet();
        return new PortSetDiff<T>(
            desiredSet.Where(item => !currentSet.Contains(item)).ToArray(),
            currentSet.Where(item => !desiredSet.Contains(item)).ToArray());
    }
}
