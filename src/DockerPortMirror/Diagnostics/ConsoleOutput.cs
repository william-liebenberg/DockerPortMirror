using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace DockerPortMirror.Diagnostics;

internal static partial class ConsoleOutput
{
    private const int StandardOutputHandle = -11;
    private const int StandardErrorHandle = -12;
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static void Initialize()
    {
        // Unix console streams already use UTF-8. On Windows, install process-local writers
        // instead of setting Console.OutputEncoding, which changes the shared console code page.
        if (!OperatingSystem.IsWindows())
            return;

        Console.SetOut(CreateWindowsWriter(StandardOutputHandle, Console.OpenStandardOutput()));
        Console.SetError(CreateWindowsWriter(StandardErrorHandle, Console.OpenStandardError()));
    }

    [SupportedOSPlatform("windows")]
    private static TextWriter CreateWindowsWriter(int standardHandle, Stream stream)
    {
        // Check each handle independently: either stdout or stderr may be redirected.
        var handle = GetStdHandle(standardHandle);
        return GetConsoleMode(handle, out _)
            ? new WindowsConsoleWriter(handle)
            : new StreamWriter(stream, Utf8) { AutoFlush = true };
    }

    private sealed class WindowsConsoleWriter(nint handle) : TextWriter
    {
        public override Encoding Encoding => Utf8;

        public override void Write(char value) => Write(new ReadOnlySpan<char>(in value));

        public override void Write(string? value) => Write(value.AsSpan());

        public override void Write(char[] buffer, int index, int count) => Write(buffer.AsSpan(index, count));

        public override unsafe void Write(ReadOnlySpan<char> buffer)
        {
            while (!buffer.IsEmpty)
            {
                // Keep native writes bounded and don't cut a surrogate pair at a chunk boundary.
                var length = Math.Min(buffer.Length, 8192);
                if (length < buffer.Length && char.IsHighSurrogate(buffer[length - 1]) && char.IsLowSurrogate(buffer[length]))
                    length--;

                int written;
                fixed (char* chars = buffer)
                {
                    if (!WriteConsoleW(handle, chars, length, out written, 0))
                        throw new IOException("Unable to write to the Windows console.", new Win32Exception(Marshal.GetLastPInvokeError()));
                }
                if (written == 0)
                    throw new IOException("The Windows console did not accept any characters.");
                buffer = buffer[written..];
            }
        }
    }

    [LibraryImport("kernel32.dll")]
    private static partial nint GetStdHandle(int standardHandle);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetConsoleMode(nint handle, out uint mode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool WriteConsoleW(nint handle, char* buffer, int length, out int written, nint reserved);
}
