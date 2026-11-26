using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace DockerPortMirror.Tests.Cli;

[CollectionDefinition(nameof(ConsoleEncodingTests), DisableParallelization = true)]
public sealed class ExclusiveConsoleAccess;

[Collection(nameof(ConsoleEncodingTests))]
public sealed class ConsoleEncodingTests
{
    private const uint LegacyCodePage = 437;

    [Fact]
    public async Task Redirected_cli_output_preserves_emoji_on_a_legacy_windows_console()
    {
        using var console = new LegacyConsole();
        using var timeout = CreateTimeout();
        const string markers = "➕➡️➖\U0001F7E2";
        using var process = StartWithInvalidBind(markers);
        try
        {
            var error = await process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            Assert.NotEqual(0, process.ExitCode);
            Assert.Contains(markers, error, StringComparison.Ordinal);
            Assert.Equal(LegacyCodePage, GetConsoleOutputCP());
        }
        finally
        {
            if (!process.HasExited)
                process.Kill();
        }
    }

    [Fact]
    public async Task Shell_keeps_its_code_page_during_cli_output_and_after_forced_termination()
    {
        using var console = new LegacyConsole();
        using var timeout = CreateTimeout();
        var shellBefore = await ReadShellCodePageAsync(timeout.Token);
        // Stay below Windows' command-line limit, but exceed the stderr pipe buffer.
        // Reading only the start confirms startup while leaving the application blocked on output.
        using var process = StartWithInvalidBind(new string('➕', 24_000));
        try
        {
            var firstCharacter = new char[1];
            Assert.Equal(1, await process.StandardError.ReadAsync(firstCharacter, timeout.Token));
            var shellDuring = await ReadShellCodePageAsync(timeout.Token);
            var outputCodePageDuring = GetConsoleOutputCP();
            Assert.False(process.HasExited, "The CLI must still be writing when it is forcibly terminated.");

            process.Kill();
            await process.WaitForExitAsync(timeout.Token);
            var shellAfter = await ReadShellCodePageAsync(timeout.Token);

            Assert.Equal(shellBefore, shellDuring);
            Assert.Equal(shellBefore, shellAfter);
            Assert.Equal(LegacyCodePage, outputCodePageDuring);
            Assert.Equal(LegacyCodePage, GetConsoleOutputCP());
        }
        finally
        {
            if (!process.HasExited)
                process.Kill();
        }
    }

    private static Process StartWithInvalidBind(string value)
    {
        // The parser echoes the invalid bind value without starting Docker or SSH.
        var executable = Path.Combine(AppContext.BaseDirectory, "DockerPortMirror.exe");
        return Process.Start(new ProcessStartInfo(executable, ["--bind", value])
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
        })!;
    }

    private static async Task<string> ReadShellCodePageAsync(CancellationToken cancellationToken)
    {
        using var shell = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), ["/d", "/c", "chcp"])
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
        })!;
        var output = await shell.StandardOutput.ReadToEndAsync(cancellationToken);
        await shell.WaitForExitAsync(cancellationToken);
        Assert.Equal(0, shell.ExitCode);
        var codePage = Regex.Match(output, @"[0-9]+\s*$");
        Assert.True(codePage.Success, output);
        return codePage.Value.Trim();
    }

    private static CancellationTokenSource CreateTimeout()
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        return timeout;
    }

    private sealed class LegacyConsole : IDisposable
    {
        private readonly bool _allocated;
        private readonly uint _originalCodePage;

        public LegacyConsole()
        {
            Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows console code pages are Windows-only.");

            // Some test runners launch without an attached console. Give both children one to inherit.
            _originalCodePage = GetConsoleOutputCP();
            _allocated = _originalCodePage == 0;
            if (_allocated)
                Assert.True(AllocConsole(), $"AllocConsole failed: {Marshal.GetLastPInvokeError()}");

            if (!SetConsoleOutputCP(LegacyCodePage))
            {
                var error = Marshal.GetLastPInvokeError();
                Dispose();
                Assert.Fail($"SetConsoleOutputCP failed: {error}");
            }
        }

        public void Dispose()
        {
            if (_allocated)
                FreeConsole();
            else
                SetConsoleOutputCP(_originalCodePage);
        }
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern uint GetConsoleOutputCP();

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleOutputCP(uint codePage);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeConsole();
}
