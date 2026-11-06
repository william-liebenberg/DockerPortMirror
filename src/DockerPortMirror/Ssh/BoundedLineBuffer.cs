namespace DockerPortMirror.Ssh;

/// <summary>Keeps the last lines written to it, so a long-running process cannot grow memory without bound.</summary>
internal sealed class BoundedLineBuffer
{
    private readonly Queue<string> _lines;
    private readonly int _capacity;
    private readonly Lock _gate = new();

    public BoundedLineBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
        _lines = new Queue<string>(capacity);
    }

    public void Add(string line)
    {
        lock (_gate)
        {
            if (_lines.Count == _capacity)
                _lines.Dequeue();
            _lines.Enqueue(line);
        }
    }

    public IReadOnlyList<string> ToList()
    {
        lock (_gate)
            return _lines.ToArray();
    }
}
