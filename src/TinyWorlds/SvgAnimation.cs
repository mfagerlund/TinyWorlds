using System.Globalization;
using System.Numerics;
using FluentSvg;

namespace TinyWorlds;

/// <summary>
/// Small helpers over FluentSvg's SMIL support, shared by the animated worlds.
/// </summary>
internal static class SvgAnimation
{
    /// <summary>SMIL duration string, e.g. 8f -> "8s". Invariant culture — a comma decimal
    /// separator produces an SVG that silently refuses to animate.</summary>
    public static string Duration(float seconds) =>
        seconds.ToString("0.###", CultureInfo.InvariantCulture) + "s";

    /// <summary>
    /// Make an animation loop. FluentSvg emits fill="freeze" but no repeatCount, so by default an
    /// animation plays once and stops on the last frame — fine for a one-shot, wrong for a gallery
    /// image someone opens and stares at.
    /// </summary>
    public static T Loop<T>(this T animate) where T : Svg.IAttributed
        => animate.SetAttribute("repeatCount", "indefinite");

    /// <summary>Loops both halves of an <see cref="Svg.AddAnimateXy"/> pair.</summary>
    public static void Loop(this (Svg.Animate x, Svg.Animate y) pair)
    {
        pair.x.Loop();
        pair.y.Loop();
    }

    /// <summary>
    /// Animates a circle's centre across <paramref name="centres"/>.
    /// </summary>
    public static void AnimateCircle(Svg svg, Svg.Circle circle, string duration, IReadOnlyList<Vector2> centres)
        => svg.AddAnimateXy(circle, duration, (ICollection<Vector2>)centres.ToList()).Loop();

    /// <summary>
    /// Animates a centre-sized rectangle across <paramref name="centres"/>.
    ///
    /// SVG rects are positioned by their TOP-LEFT corner, so the centres are converted here. Pass
    /// the same <paramref name="size"/> used to create the rect or it will drift.
    /// </summary>
    public static void AnimateRectCentre(
        Svg svg, Svg.Rectangle rect, string duration, IReadOnlyList<Vector2> centres, Vector2 size)
    {
        var half = size * 0.5f;
        var topLefts = centres.Select(c => c - half).ToList();
        svg.AddAnimateXy(rect, duration, topLefts, attributePrefix: "").Loop();
    }
}
