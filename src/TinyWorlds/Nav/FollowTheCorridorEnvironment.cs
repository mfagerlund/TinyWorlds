using System.Numerics;
using FluentSvg;

namespace TinyWorlds;

public enum DeathCause
{
    None,
    WallCollision,
    TooSlowTo4thMarker,
    TooSlowAfter4thMarker,
    Finished,
    Timeout
}

/// <summary>
/// <see cref="IEnvironment"/> over <see cref="SimpleCarWorld"/>: drive a drifting car round a
/// hand-drawn race track using nine range-finders, scored on progress.
///
/// Harder than <see cref="SimpleCorridorEnvironment"/> in the way that counts — the track is drawn,
/// not generated, so its corners have no pattern to memorise.
///
/// Loading parses SVG, so share one world across a population:
/// <code>
/// var track = SimpleCarWorld.Load();
/// var envs = Enumerable.Range(0, 256).Select(_ => new FollowTheCorridorEnvironment(track));
/// </code>
/// The world is immutable once built; the car is per-environment.
/// </summary>
public class FollowTheCorridorEnvironment : IAnimatedEnvironment
{
    private readonly SimpleCarWorld _world;
    private readonly SimpleCarWorld.SimpleCar _car;
    private readonly float[] _action = new float[2];
    private readonly List<(Vector2 position, float heading, bool dead)> _trace = [];
    private int _currentStep;
    private float _lastReward;

    public int InputCount => 9;   // nine range sensors
    public int OutputCount => 2;  // steering, throttle
    public int MaxSteps => _world.MaxSteps;
    public DeathCause CauseOfDeath { get; private set; }

    public FollowTheCorridorEnvironment(int maxSteps = 320)
        : this(SimpleCarWorld.Load(maxSteps))
    {
    }

    public FollowTheCorridorEnvironment(SimpleCarWorld sharedWorld)
    {
        _world = sharedWorld;
        _car = new SimpleCarWorld.SimpleCar(_world);
        _currentStep = 0;
    }

    /// <summary>
    /// The seed is ignored: one drawn track, one start line, no randomness. Reset still replays
    /// identically, which is what the interface actually promises.
    /// </summary>
    public void Reset(int seed)
    {
        _car.Reset();
        _currentStep = 0;
        CauseOfDeath = DeathCause.None;
        _lastReward = 0f;

        _trace.Clear();
        CaptureFrame();
    }

    public void GetObservations(Span<float> observations)
    {
        var state = _car.GetState(_world.WallGrid);
        for (int i = 0; i < state.Length && i < observations.Length; i++)
        {
            observations[i] = state[i];
        }
    }

    public float Step(ReadOnlySpan<float> actions)
    {
        _action[0] = Math.Clamp(actions[0], -1f, 1f);
        _action[1] = Math.Clamp(actions[1], -1f, 1f);

        bool wasDead = _car.IsDead;
        _currentStep++;
        _lastReward = _world.Update(_car, _action);

        if (_car.IsDead && !wasDead && CauseOfDeath == DeathCause.None)
        {
            // SimpleCarWorld reports how the run ended only through the reward it hands back, so
            // the cause is inferred from its magnitude. Brittle, but the alternative is changing
            // the world's return type.
            if (_lastReward > 0)
            {
                CauseOfDeath = DeathCause.Finished;
            }
            else if (_lastReward <= -0.49f)  // the -0.5f early-kill
            {
                CauseOfDeath = _car.CurrentProgressMarkerId < 4
                    ? DeathCause.TooSlowTo4thMarker
                    : DeathCause.TooSlowAfter4thMarker;
            }
            else
            {
                CauseOfDeath = DeathCause.WallCollision;
            }
        }
        else if (_currentStep >= MaxSteps && CauseOfDeath == DeathCause.None)
        {
            CauseOfDeath = DeathCause.Timeout;
        }

        CaptureFrame();
        return _lastReward;
    }

    public bool IsTerminal() => _car.IsDead || _currentStep >= MaxSteps;

    // ---- rendering ---------------------------------------------------------------------------
    // No SvgCoords flip anywhere below, unlike every other world here: SimpleCarWorld's geometry
    // comes straight out of an SVG file and is already in SVG's +y-down frame. See the note on
    // SimpleCarWorld itself.

    /// <summary>
    /// The track plus the car on it. <see cref="SimpleCarWorld.Render"/> draws the track but not
    /// the car — the world doesn't own one — so the car is added here.
    /// </summary>
    public void Render(Svg svg)
    {
        _world.Render(svg, renderProgressMarkers: true);
        RenderCar(svg, _car.Position, _car.Direction, _car.IsDead);
    }

    private static void RenderCar(Svg svg, Vector2 position, Vector2 direction, bool dead)
    {
        string colour = dead ? "#c0392b" : "#e67e22";
        svg.AddCircle(position, SimpleCarWorld.SimpleCar.Radius)
           .SetFill(colour).SetStroke("transparent");
        svg.AddLine(position, position + direction * SimpleCarWorld.SimpleCar.Radius * 2.5f)
           .SetStroke(colour).SetStrokeWidth(0.5f);
    }

    // ---- IAnimatedEnvironment ----------------------------------------------------------------

    public bool Recording { get; set; }
    public int FrameCount => _trace.Count;

    private void CaptureFrame()
    {
        if (Recording)
        {
            _trace.Add((_car.Position, _car.HeadingAngle, _car.IsDead));
        }
    }

    public void RenderAnimated(Svg svg, float durationSeconds = 8f)
    {
        _world.Render(svg, renderProgressMarkers: true);

        if (_trace.Count < 2)
        {
            RenderCar(svg, _car.Position, _car.Direction, _car.IsDead);
            return;
        }

        string dur = SvgAnimation.Duration(durationSeconds);
        var positions = _trace.Select(f => f.position).ToList();
        var noses = _trace
            .Select(f => f.position + new Vector2((float)Math.Cos(f.heading), (float)Math.Sin(f.heading))
                                      * SimpleCarWorld.SimpleCar.Radius * 2.5f)
            .ToList();

        // The racing line actually driven, drawn once behind the car. An animation hides exactly
        // one thing — where it has been — and on a race track that is the interesting part.
        svg.AddPolyline(positions)
           .SetFill("transparent").SetStroke("#e67e22").SetStrokeWidth(0.4).SetStrokeOpacity(0.35f);

        var nose = svg.AddLine(positions[0], noses[0]).SetStroke("#e67e22").SetStrokeWidth(0.5);
        svg.AddAnimateLine(nose, dur, positions, noses).Loop();

        var body = svg.AddCircle(positions[0], SimpleCarWorld.SimpleCar.Radius)
                      .SetFill("#e67e22").SetStroke("transparent");
        SvgAnimation.AnimateCircle(svg, body, dur, positions);
    }

    // ---- accessors for visualisation ---------------------------------------------------------

    public Vector2 GetCarPosition() => _car.Position;
    public float GetCarHeading() => _car.HeadingAngle;
    public float GetCarSpeed() => _car.Speed;
    public Vector2 GetCarDirection() => _car.Direction;
    public SimpleCarWorld.Sensor[] GetSensors() => _car.Sensors;
    public SimpleCarWorld World => _world;
    public SimpleCarWorld.SimpleCar Car => _car;
}
