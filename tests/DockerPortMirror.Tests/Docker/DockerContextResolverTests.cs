using DockerPortMirror.Cli;
using DockerPortMirror.Docker;
using DockerPortMirror.Tests.Fakes;
using Microsoft.Extensions.Options;

namespace DockerPortMirror.Tests.Docker;

public sealed class DockerContextResolverTests
{
    private const string MacContext = """[{ "Name": "mac", "Endpoints": { "docker": { "Host": "ssh://macdocker", "SkipTLSVerify": false } } }]""";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void Endpoint_is_read_from_context_inspect_output()
    {
        const string json = """
            [
              {
                "Name": "mac",
                "Metadata": {},
                "Endpoints": { "docker": { "Host": "ssh://macdocker", "SkipTLSVerify": false } }
              }
            ]
            """;

        Assert.Equal("ssh://macdocker", DockerContextResolver.ParseEndpoint(json));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""[{ "Name": "x" }]""")]
    [InlineData("""[{ "Endpoints": { "kubernetes": { "Host": "https://k8s" } } }]""")]
    [InlineData("""[{ "Endpoints": { "docker": { "Host": 42 } } }]""")]
    public void Missing_endpoint_gives_null(string json)
    {
        Assert.Null(DockerContextResolver.ParseEndpoint(json));
    }

    [Fact]
    public void Malformed_output_is_a_user_facing_error()
    {
        Assert.Throws<DockerPortMirrorException>(() => DockerContextResolver.ParseEndpoint("not json"));
    }

    [Fact]
    public async Task Current_context_is_resolved_to_its_ssh_target()
    {
        var runner = new FakeProcessRunner()
            .On("docker context show", 0, "mac\n")
            .On("docker context inspect mac", 0, MacContext)
            .On("ssh -G macdocker", 0, "hostname mac.local\n");
        var resolver = new DockerContextResolver(runner, Options.Create(new MirrorOptions()));

        var endpoint = await resolver.ResolveAsync(Token);

        Assert.Equal(new DockerEndpoint("mac", "ssh://macdocker", new("macdocker")), endpoint);
    }

    [Fact]
    public async Task Options_override_the_context_and_target_and_the_result_is_kept()
    {
        var runner = new FakeProcessRunner()
            .On("docker context inspect mac", 0, MacContext)
            .On("ssh -G me@other", 0);
        var resolver = new DockerContextResolver(runner, Options.Create(new MirrorOptions { Context = "mac", SshTarget = "me@other" }));

        var first = await resolver.ResolveAsync(Token);
        var second = await resolver.ResolveAsync(Token);

        Assert.Equal("me@other", first.SshTarget.Destination);
        Assert.Same(first, second);
        Assert.Equal(2, runner.Calls.Count);
    }

    [Fact]
    public async Task Non_ssh_context_is_a_user_facing_error()
    {
        var runner = new FakeProcessRunner()
            .On("docker context inspect default", 0, """[{ "Endpoints": { "docker": { "Host": "npipe:////./pipe/docker_engine" } } }]""");
        var resolver = new DockerContextResolver(runner, Options.Create(new MirrorOptions { Context = "default" }));

        var error = await Assert.ThrowsAsync<DockerPortMirrorException>(() => resolver.ResolveAsync(Token));

        Assert.Contains("--ssh-target", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Failing_docker_command_reports_its_error()
    {
        var runner = new FakeProcessRunner()
            .On("docker context inspect nope", 1, "", "context \"nope\" does not exist");
        var resolver = new DockerContextResolver(runner, Options.Create(new MirrorOptions { Context = "nope" }));

        var error = await Assert.ThrowsAsync<DockerPortMirrorException>(() => resolver.ResolveAsync(Token));

        Assert.Equal("Unable to inspect Docker context 'nope'. context \"nope\" does not exist", error.Message);
    }
}
