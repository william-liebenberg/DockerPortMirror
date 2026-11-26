using System.Text;

namespace DockerPortMirror.Diagnostics;

internal static class ConsoleOutput
{
    public static Encoding? OriginalEncoding { get; private set; }

    public static Encoding UseUtf8()
    {
        // ProcessStartInfo normally uses the console encoding to decode redirected child output.
        var original = Console.OutputEncoding;
        OriginalEncoding = original;
        Console.OutputEncoding = Encoding.UTF8;
        return original;
    }
}
