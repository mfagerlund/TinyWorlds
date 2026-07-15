using System.Numerics;

namespace TinyWorlds;

/// <summary>
/// Coordinate conversion for rendering.
///
/// Every world here uses standard maths convention: +y is UP. SVG uses +y DOWN. Without a flip a
/// cartpole renders with its pole hanging below the cart, which looks like a pendulum and is
/// exactly the kind of thing a byte-count "the file has content" check sails straight past.
///
/// Convert at the drawing boundary only — worlds must keep thinking in +y up. Sizes and radii are
/// unsigned and must NOT be passed through here.
/// </summary>
internal static class SvgCoords
{
    /// <summary>World point (+y up) to SVG point (+y down).</summary>
    public static Vector2 P(float x, float y) => new(x, -y);

    /// <summary>World point (+y up) to SVG point (+y down).</summary>
    public static Vector2 P(Vector2 world) => new(world.X, -world.Y);
}
