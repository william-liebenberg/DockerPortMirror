using DockerPortMirror.Processes;

namespace DockerPortMirror.Tests.Fakes;

internal sealed class FakeProcessRunner : IProcessRunner
{
    private readonly Dictionary<string, ProcessResult> _results = new(StringComparer.Ordinal);

    public List<string> Calls { get; } = [];

    public FakeProcessRunner On(string commandLine, int exitCode, string stdout = "", string stderr = "")
    {
        _results[commandLine] = new ProcessResult(exitCode, stdout, stderr);
        return this;
    }

    public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var commandLine = string.Join(' ', [fileName, .. arguments]);
        Calls.Add(commandLine);
        return _results.TryGetValue(commandLine, out var result)
            ? Task.FromResult(result)
            : throw new InvalidOperationException($"Unexpected command: {commandLine}");
    }
}
