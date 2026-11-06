using System.CommandLine;
using DockerPortMirror.Cli;

namespace DockerPortMirror.Tests.Cli;

public sealed class MirrorCommandTests
{
    private static async Task<(int ExitCode, MirrorCommandLine? CommandLine, string Output, string Error)> InvokeAsync(params string[] args)
    {
        MirrorCommandLine? captured = null;
        var command = MirrorCommand.Create((commandLine, _) =>
        {
            captured = commandLine;
            return Task.FromResult(0);
        });
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await command.Parse(args).InvokeAsync(new InvocationConfiguration { Output = output, Error = error });
        return (exitCode, captured, output.ToString(), error.ToString());
    }

    [Fact]
    public async Task No_options_leave_everything_to_configuration()
    {
        var (exitCode, commandLine, _, _) = await InvokeAsync();

        Assert.Equal(0, exitCode);
        Assert.Equal(new MirrorCommandLine(), commandLine);
        Assert.Empty(commandLine!.ToConfiguration());
    }

    [Fact]
    public async Task Options_are_parsed()
    {
        var (_, commandLine, _, _) = await InvokeAsync("--context", "mac", "--ssh-target", "me@host", "--bind", "localhost", "--poll", "5", "--verbose");

        Assert.Equal(new MirrorCommandLine("mac", "me@host", "localhost", 5, true), commandLine);
        Assert.Equal(
            new Dictionary<string, string?>
            {
                ["Context"] = "mac",
                ["SshTarget"] = "me@host",
                ["BindAddress"] = "localhost",
                ["PollSeconds"] = "5",
                ["Verbose"] = "True",
            },
            commandLine!.ToConfiguration().ToDictionary());
    }

    [Theory]
    [InlineData("--poll", "0")]
    [InlineData("--poll", "abc")]
    [InlineData("--bind", "0.0.0.0")]
    [InlineData("--unknown")]
    [InlineData("--config", "no-such-file.json")]
    public async Task Invalid_options_fail_without_running(params string[] args)
    {
        var (exitCode, commandLine, _, error) = await InvokeAsync(args);

        Assert.NotEqual(0, exitCode);
        Assert.Null(commandLine);
        Assert.NotEmpty(error);
    }

    [Fact]
    public async Task Help_lists_every_option()
    {
        var (exitCode, commandLine, output, _) = await InvokeAsync("--help");

        Assert.Equal(0, exitCode);
        Assert.Null(commandLine);
        foreach (var option in new[] { "--context", "--ssh-target", "--bind", "--poll", "--verbose", "--config", "--version" })
            Assert.Contains(option, output, StringComparison.Ordinal);
    }
}
