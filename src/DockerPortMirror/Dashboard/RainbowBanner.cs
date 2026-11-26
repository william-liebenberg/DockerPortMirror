using DockerPortMirror.Diagnostics;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace DockerPortMirror.Dashboard;

/// <summary>Draws the application name as a figlet with one rainbow colour per row.</summary>
internal sealed class RainbowBanner : Renderable
{
    private static readonly Style[] Rainbow =
    [
        new(Color.Red), new(Color.Orange1), new(Color.Yellow),
        new(Color.Green), new(Color.Blue), new(Color.Purple),
    ];

    private readonly IRenderable _figlet = new FigletText(AppInfo.Name);

    protected override Measurement Measure(RenderOptions options, int maxWidth)
    {
        var width = Math.Min(maxWidth, _figlet.Measure(options, int.MaxValue).Max);
        return new Measurement(width, width);
    }

    protected override IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
    {
        // Render at full width so the font never drops glyphs or wraps on narrow terminals.
        var lines = Segment.SplitLines(_figlet.Render(options, int.MaxValue));
        for (var row = 0; row < lines.Count; row++)
        {
            if (row > 0)
                yield return Segment.LineBreak;

            var remaining = maxWidth;
            foreach (var segment in lines[row])
            {
                // The default figlet font is ASCII, so each character occupies one cell.
                var length = Math.Min(remaining, segment.Text.Length);
                if (length == 0)
                    break;

                yield return new Segment(segment.Text[..length], Rainbow[row % Rainbow.Length]);
                remaining -= length;
            }
        }
    }
}
