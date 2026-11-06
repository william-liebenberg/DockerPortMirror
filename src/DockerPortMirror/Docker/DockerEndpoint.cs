using DockerPortMirror.Ssh;

namespace DockerPortMirror.Docker;

/// <summary>The Docker context in use, its endpoint, and the SSH destination that reaches its host.</summary>
internal sealed record DockerEndpoint(string Context, string Host, SshTarget SshTarget);
