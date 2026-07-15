using System.Numerics;

namespace TinyWorlds;

/// <summary>
/// The handful of vector operations System.Numerics doesn't give us, plus two it gives us
/// differently enough to matter.
///
/// Note the deliberate <c>(float)Math.Xxx</c> rather than <c>MathF.Xxx</c>: those compute in double
/// and round once at the end, where MathF computes in float throughout, and the two disagree by an
/// ulp often enough to matter. An ulp is not a rounding detail here — it moves a marker-crossing or
/// wall-contact test across its threshold, and from there the whole episode is different. A
/// benchmark whose trajectories shift when someone "tidies" Math into MathF is not a benchmark, so
/// these stay pinned.
/// </summary>
internal static class Vec
{
    /// <summary>
    /// Unit vector, or <see cref="Vector2.Zero"/> if this vector has no length.
    ///
    /// NOT the same as <c>Vector2.Normalize</c>, which divides by zero and hands back
    /// <c>(NaN, NaN)</c>. That NaN then propagates silently through every downstream float and
    /// poisons a whole episode's rewards. Godot's <c>Normalized()</c> — which this code was ported
    /// from — returns zero instead, and the callers here rely on it: a car that crashes while
    /// stationary normalizes a zero velocity.
    /// </summary>
    public static Vector2 Normalized(this Vector2 v)
    {
        float lengthSquared = v.LengthSquared();
        return lengthSquared == 0f ? Vector2.Zero : v / (float)Math.Sqrt(lengthSquared);
    }

    /// <summary>Angle of this vector from +x, in radians, in (-pi, pi].</summary>
    public static float AngleRad(this Vector2 v) => (float)Math.Atan2(v.Y, v.X);

    /// <summary>
    /// Linear interpolation, <c>a + (b - a) * t</c>.
    ///
    /// NOT <c>Vector2.Lerp</c>, which evaluates the algebraically-equal-but-numerically-different
    /// <c>a * (1 - t) + b * t</c>. Both are defensible; they are not interchangeable.
    /// </summary>
    public static Vector2 Lerp(Vector2 a, Vector2 b, float t) => a + (b - a) * t;

    /// <summary>
    /// Intersection point of two line segments, or null if they don't cross.
    ///
    /// Sign-only orientation and a single division, so it stays stable when segments are nearly
    /// parallel — which is the common case for a sensor ray grazing a wall.
    /// </summary>
    public static Vector2? IntersectSegments(Vector2 aStart, Vector2 aEnd, Vector2 bStart, Vector2 bEnd)
    {
        float aX = aEnd.X - aStart.X;
        float aY = aEnd.Y - aStart.Y;
        float bX = bEnd.X - bStart.X;
        float bY = bEnd.Y - bStart.Y;
        float toBX = bStart.X - aStart.X;
        float toBY = bStart.Y - aStart.Y;

        float denom = aX * bY - aY * bX;
        if (MathF.Abs(denom) < 1e-10f)
        {
            // Parallel: no single crossing point.
            return null;
        }

        float invDenom = 1f / denom;
        float t = -(aX * toBY - aY * toBX) * invDenom;  // along b
        float u = -(bX * toBY - bY * toBX) * invDenom;  // along a

        // Positive form on purpose: if any input is NaN every comparison is false, so this says
        // "no crossing". The negated form (t < 0 || t > 1 || ...) says the opposite and hands back
        // a NaN intersection point.
        bool crosses = t >= 0f && t <= 1f && u >= 0f && u <= 1f;
        if (!crosses)
        {
            return null;
        }

        // Parameterised along b, so the caller's argument order decides the rounding.
        return new Vector2(bStart.X + t * bX, bStart.Y + t * bY);
    }

    /// <summary>Component-wise bounding box of a point set.</summary>
    public static (Vector2 min, Vector2 max) GetMinMax(this IEnumerable<Vector2> points)
    {
        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);

        foreach (var p in points)
        {
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
        }

        return (min, max);
    }
}
