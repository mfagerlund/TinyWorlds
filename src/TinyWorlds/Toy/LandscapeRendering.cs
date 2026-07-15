using System.Numerics;
using FluentSvg;

namespace TinyWorlds;

/// <summary>
/// Shared heatmap drawing for the landscape worlds. <see cref="LandscapeEnvironment"/> and
/// <see cref="LandscapeEnvironmentAdapter"/> are two front-ends over the same optimization
/// landscapes, so they render identically — the drawing lives here rather than being copy-pasted
/// into both.
/// </summary>
internal static class LandscapeRendering
{
    private const int Cells = 40;

    /// <summary>
    /// Draws dims 0 and 1 of <paramref name="landscape"/> as a cost heatmap and marks
    /// <paramref name="position"/>.
    ///
    /// Higher-dimensional landscapes are sliced THROUGH the agent's current position: the probe
    /// keeps the agent's real values for dims 2+ and only sweeps 0/1. A fixed cross-section
    /// (e.g. zeros) would be cheaper but would show a landscape the agent is not standing in.
    /// </summary>
    public static void Draw(
        Svg svg,
        LandscapeNavigationTask.LandscapeFunction landscape,
        float[] position,
        float minBound,
        float maxBound)
    {
        float span = maxBound - minBound;
        float step = span / Cells;

        var probe = (float[])position.Clone();
        var costs = new float[Cells, Cells];
        float best = float.MaxValue;
        float worst = float.MinValue;

        for (int iy = 0; iy < Cells; iy++)
        {
            for (int ix = 0; ix < Cells; ix++)
            {
                probe[0] = minBound + (ix + 0.5f) * step;
                if (probe.Length > 1)
                    probe[1] = minBound + (iy + 0.5f) * step;

                float c = landscape(probe);
                costs[ix, iy] = c;
                if (c < best) best = c;
                if (c > worst) worst = c;
            }
        }

        float range = MathF.Max(worst - best, 1e-6f);

        for (int iy = 0; iy < Cells; iy++)
        {
            for (int ix = 0; ix < Cells; ix++)
            {
                // Log compression is not cosmetic: these landscapes (Rosenbrock and friends) span
                // orders of magnitude, and a linear ramp renders as one flat slab with a single
                // bright pixel at the optimum — technically correct and completely unreadable.
                float t = MathF.Log(1f + 9f * ((costs[ix, iy] - best) / range)) / MathF.Log(10f);
                int v = (int)(255f * (1f - t));
                int b = Math.Min(255, v + 40);

                // Cell origin is its TOP-left in SVG space, so offset by one step in world +y
                // before flipping — otherwise the grid lands one cell off.
                svg.AddRectangleSized(
                       SvgCoords.P(minBound + ix * step, minBound + (iy + 1) * step),
                       new Vector2(step, step))
                   .SetFill($"rgb({v},{v},{b})")
                   .SetStroke("transparent");
            }
        }

        var here = SvgCoords.P(position[0], position.Length > 1 ? position[1] : 0f);
        svg.AddCircle(here, span * 0.02f).SetFill("#e67e22").SetStroke("transparent");
    }
}
