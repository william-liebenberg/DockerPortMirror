using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace DockerPortMirror.Tests.Cli;

[CollectionDefinition(nameof(ConsoleEncodingTests), DisableParallelization = true)]
public sealed class ExclusiveConsoleAccess;

[Collection(nameof(ConsoleEncodingTests))]
public sealed class ConsoleEncodingTests
{
    [Fact]
    public async Task Redirected_cli_output_preserves_emoji_on_a_legacy_windows_console()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows console code pages are Windows-only.");

        // Some test runners launch without an attached console. Give the child one to inherit.
        var allocated = GetConsoleOutputCP() == 0;
        if (allocated)
            Assert.True(AllocConsole(), $"AllocConsole failed: {Marshal.GetLastPInvokeError()}");

        var originalCodePage = GetConsoleOutputCP();
        try
        {
            Assert.True(SetConsoleOutputCP(437), $"SetConsoleOutputCP failed: {Marshal.GetLastPInvokeError()}");
            const string markers = "➕➡️➖\U0001F7E2";
            // The parser echoes the invalid bind value without starting Docker or SSH.
            var executable = Path.Combine(AppContext.BaseDirectory, "DockerPortMirror.exe");
            using var process = Process.Start(new ProcessStartInfo(executable, ["--bind", markers])
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            })!;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                var error = await process.StandardError.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token);
                Assert.NotEqual(0, process.ExitCode);
                Assert.Contains(markers, error, StringComparison.Ordinal);
                Assert.Equal(437u, GetConsoleOutputCP());
            }
            finally
            {
                if (!process.HasExited)
                    process.Kill();
            }
        }
        finally
        {
            if (allocated)
                FreeConsole();
            else
                SetConsoleOutputCP(originalCodePage);
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

    [DllImport("kernel32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeConsole();
}
