using DockerPortMirror.Docker;

namespace DockerPortMirror.Tests.Docker;

public sealed class DockerContextResolverTests
{
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
    public void Missing_endpoint_gives_null(string json)
    {
        Assert.Null(DockerContextResolver.ParseEndpoint(json));
    }
}
