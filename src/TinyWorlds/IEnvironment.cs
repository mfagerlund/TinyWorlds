using FluentSvg;

namespace TinyWorlds;

/// <summary>
/// A small simulated world an agent acts in: observations out, actions in, reward back.
///
/// Deliberately shaped for evaluation rather than for a game loop — <see cref="Reset"/> takes a
/// seed because determinism is the point: the same seed must replay the same episode, or the
/// world is useless as a benchmark.
///
/// The interface is engine-free by design. A world here does its own arithmetic; it does not get
/// to reference a physics engine, a GPU library or a renderer. If a world needs a real physics
/// engine, keep the engine behind a factory interface on the CONSUMER's side and leave this core
/// lean — Evolvatron.Clones does exactly that with IMotorBody/RigidonPhysics.
/// </summary>
public interface IEnvironment
{
    /// <summary>
    /// Number of inputs the network receives.
    /// </summary>
    int InputCount { get; }

    /// <summary>
    /// Number of outputs the network produces.
    /// </summary>
    int OutputCount { get; }

    /// <summary>
    /// Maximum number of steps per episode.
    /// </summary>
    int MaxSteps { get; }

    /// <summary>
    /// Reset the environment to initial state with optional seed.
    /// </summary>
    void Reset(int seed = 0);

    /// <summary>
    /// Get current observations for the network.
    /// </summary>
    void GetObservations(Span<float> observations);

    /// <summary>
    /// Step the environment with network actions, return reward.
    /// </summary>
    float Step(ReadOnlySpan<float> actions);

    /// <summary>
    /// Check if the episode is complete.
    /// </summary>
    bool IsTerminal();

    /// <summary>
    /// Get final fitness based on terminal state (optional, defaults to cumulative reward).
    /// Return 0 to use cumulative reward instead.
    /// </summary>
    float GetFinalFitness() => 0f;

    /// <summary>
    /// Draw the world's CURRENT state into <paramref name="svg"/>.
    ///
    /// Required, not optional, and deliberately not a no-op default: a world you cannot look at is
    /// a world you debug by printing floats. Because the only dependency here is FluentSvg, this
    /// works headless — in a unit test, over SSH, on a machine with no GPU — which is exactly when
    /// you most need to see what the agent actually did.
    ///
    /// Implementations should draw in their own natural coordinates and leave framing to the
    /// caller. Call it after <see cref="Reset"/> or any <see cref="Step"/>; it must not mutate state.
    /// </summary>
    void Render(Svg svg);
}
