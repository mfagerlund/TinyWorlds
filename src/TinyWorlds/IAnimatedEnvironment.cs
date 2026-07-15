using FluentSvg;

namespace TinyWorlds;

/// <summary>
/// A world whose episode can be replayed as a single animated SVG.
///
/// A frozen frame is a fine picture of a dataset and a poor picture of a cart-pole: the whole
/// question is what it did over time. Worlds that move implement this; worlds that are static
/// datasets (XOR, Spiral) deliberately do not, because for them the snapshot IS the picture.
///
/// The output is SMIL attribute animation — shapes are emitted once and their attributes carry a
/// value per frame — which keeps the file proportional to (shapes + frames) rather than
/// (shapes x frames), and lets the SVG interpolate between frames for free. Same approach
/// Evolvatron.Clones uses in SvgExporter.ExportAnimatedSwimmer.
/// </summary>
public interface IAnimatedEnvironment : IEnvironment
{
    /// <summary>
    /// Whether <see cref="IEnvironment.Step"/> appends to the replay trace. **Off by default,
    /// deliberately.** These worlds run inside evolutionary inner loops — hundreds of individuals
    /// times hundreds of generations — and recording by default would be an invisible memory leak
    /// paid by every training run to serve the rare debugging one.
    ///
    /// Set it before <see cref="IEnvironment.Reset"/>, which clears the trace and captures the
    /// opening frame.
    /// </summary>
    bool Recording { get; set; }

    /// <summary>
    /// Number of frames currently recorded. 0 when <see cref="Recording"/> was never enabled.
    /// </summary>
    int FrameCount { get; }

    /// <summary>
    /// Draw the recorded episode as one looping animated SVG.
    ///
    /// Falls back to a single <see cref="IEnvironment.Render"/> snapshot if fewer than two frames
    /// were recorded — asking for an animation of nothing should give you the still, not an
    /// exception or an empty file.
    /// </summary>
    void RenderAnimated(Svg svg, float durationSeconds = 8f);
}
