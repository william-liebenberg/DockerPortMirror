using Microsoft.Extensions.Options;

namespace DockerPortMirror.Cli;

/// <summary>Validates the merged options (file, environment and command line) when the host starts.</summary>
internal sealed class MirrorOptionsValidator : IValidateOptions<MirrorOptions>
{
    public const int MaximumPollSeconds = 3600;

    public ValidateOptionsResult Validate(string? name, MirrorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var errors = new List<string>();

        if (!MirrorOptions.AllowedBindAddresses.Contains(options.BindAddress, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add(
                $"For safety, the bind address must be {string.Join(" or ", MirrorOptions.AllowedBindAddresses)}; " +
                $"'{options.BindAddress}' would expose the remote ports to the network.");
        }

        if (ValidatePoll(options.PollSeconds) is { } pollError)
            errors.Add(pollError);

        if (options.Context is { } context && string.IsNullOrWhiteSpace(context))
            errors.Add("The Docker context must not be blank.");

        if (options.SshTarget is { } target && string.IsNullOrWhiteSpace(target))
            errors.Add("The SSH target must not be blank.");

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    public static string? ValidatePoll(int seconds) =>
        seconds is < 1 or > MaximumPollSeconds
            ? $"The poll interval must be between 1 and {MaximumPollSeconds} seconds; got {seconds}."
            : null;
}
