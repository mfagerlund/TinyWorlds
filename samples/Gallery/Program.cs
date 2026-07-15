using System.Numerics;
using FluentSvg;
using TinyWorlds;

// Regenerates gallery/*.svg. Every world is driven by a real policy for real steps and then asked
// to draw itself — nothing here is a mock-up. If a render is wrong or a world is broken, the
// gallery shows it, which is the entire point of Render() being a required interface member.
//
//   dotnet run --project samples/Gallery

string outDir = args.Length > 0
    ? args[0]
    : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "gallery");
Directory.CreateDirectory(outDir);

// FluentSvg writes the size in MILLIMETRES — always, with no way to opt out; a null Size just makes
// it use the viewBox extent, still in mm. A browser renders 1mm as 96/25.4 px, so passing 480
// yields a 1814px image you can only see a corner of. Sizes below are stated in pixels, as intended,
// and converted here.
static Vector2 Px(float width, float height) => new Vector2(width, height) * (25.4f / 96f);

// ---- worlds that move: animated replays -----------------------------------------------------

// obs = [cartPos, cartVel, poleAngle, poleAngleVel], ALL normalised (angle by MAX_ANGLE_RADIANS,
// rates by 5). Gains are therefore in normalised units — a gain of 6 saturates the [-1,1] action on
// the first tilt and degrades to bang-bang. Pole terms only: adding position terms makes the
// controller fight the pole to get home and it drops it.
Render("cartpole", new CartPoleEnvironment(), steps: 400, size: Px(480, 220),
    policy: (obs, act) => act[0] = obs[2] * 2.5f + obs[3] * 0.9f);

// includeVelocity: true on purpose. The DEFAULT hides velocities, which makes this world
// non-Markovian — you cannot balance it from a single observation no matter how good your gains
// are, it needs recurrence (this is the DPNV benchmark, and why Elman nets exist in Evolvatron).
// A memoryless gallery policy would just fall over and misrepresent the world as broken.
// obs = [x, xdot, theta1, theta1dot, theta2, theta2dot], all normalised.
//
// These gains were FOUND, not reasoned out, by random search (worst-of-3-seeds, 2000/2000 steps).
// Worth knowing why hand-tuning failed: the two poles want OPPOSITE-SIGN feedback — negative on
// the long pole, positive on the short one. Any intuition carried over from single-pole cartpole
// gives you a controller that drops it in ~17 steps, and simply negating it does worse still.
// That coupling is the whole reason double-pole is a real benchmark and cartpole isn't.
Render("doublepole", new DoublePoleEnvironment(includeVelocity: true), steps: 300,
    size: Px(480, 220),
    policy: (obs, act) => act[0] = obs[2] * -9.72f + obs[3] * -2.208f
                                 + obs[4] * 6.903f + obs[5] * 0.728f);

Render("targetchase", new TargetChaseEnvironment(), steps: 300, size: Px(420, 420),
    policy: (obs, act) => { act[0] = obs[0] * 10f; act[1] = obs[1] * 10f; });  // steer at the target

// A complete lap of the hand-drawn track: 256/256 progress markers in 222 steps, ending in
// DeathCause.Finished.
//
// The driver is a plain linear map from the 9 range sensors to (steering, throttle) — no hidden
// layer, no memory. Nothing hand-tuned about it: CEM found it in 11 generations (population 400,
// top 10% elites), which is the honest headline for this world. You cannot hand-write a driver for
// it, but it is not hard to LEARN — a linear policy is enough, and that makes it a good smoke test
// for an optimizer rather than a hard benchmark.
Render("racetrack", new FollowTheCorridorEnvironment(), steps: 320, size: Px(480, 480),
    policy: (obs, act) =>
    {
        // 9 weights per action, then one bias each.
        ReadOnlySpan<float> w =
        [
            0.7725f, -0.838f, 0.5529f, -2.2242f, 2.6463f, 0.654f, -0.0643f, -0.7678f, 1.0911f,
            1.5076f, 0.2683f, 0.99f, 0.7271f, -0.5851f, -0.1209f, 1.6438f, 0.8627f, -0.0359f,
            -0.9244f, 0.5746f,
        ];

        for (int a = 0; a < 2; a++)
        {
            float sum = w[18 + a];
            for (int s = 0; s < 9; s++) sum += w[a * 9 + s] * obs[s];
            act[a] = MathF.Tanh(sum);
        }
    });

// Still, because this world is BROKEN — see the note on SimpleCorridorEnvironment itself. Not
// "hard": an omniscient oracle that knows the checkpoint list and steers straight at it collects
// 1 of 40, and 200 generations of CEM manage 6. There is no policy to animate because there is no
// policy. The gallery shows the track, the checkpoint chain and the car at the start line, which is
// the honest picture of a world nothing can drive.
//
// Prefer "racetrack" above: a real drawn track, solved 100% by a linear policy.
Render("corridor", new SimpleCorridorEnvironment(), steps: 1, size: Px(600, 300),
    policy: (obs, act) => { act[0] = 0f; act[1] = 0.2f; }, animate: false);

// ---- worlds that don't move: stills ---------------------------------------------------------
// XOR and Spiral are static datasets — the picture IS the dataset, and an animation of a ring
// hopping between fixed points would be motion for its own sake. Landscape is a still for a
// different reason: its heatmap is 1,600 rects re-sliced through the agent's position every frame,
// so animating it honestly would mean animating all of them.

Render("xor", new XOREnvironment(), steps: 2, size: Px(320, 320),
    policy: (obs, act) => act[0] = 0.5f);

Render("spiral", new SpiralEnvironment(), steps: 5, size: Px(400, 400),
    policy: (obs, act) => act[0] = 0.5f);

Render("landscape",
    new LandscapeEnvironment(new LandscapeNavigationTask(
        OptimizationLandscapes.Rosenbrock, dimensions: 2, timesteps: 50)),
    steps: 20, size: Px(400, 400),
    policy: (obs, act) => { for (int i = 0; i < act.Length; i++) act[i] = -obs[i]; });

Console.WriteLine($"\ngallery written to {Path.GetFullPath(outDir)}");

void Render(string name, IEnvironment env, int steps, Vector2 size, Action<float[], float[]> policy,
            bool animate = true)
{
    // Recording is opt-in and must be on before Reset, which captures the opening frame.
    if (animate && env is IAnimatedEnvironment rec) rec.Recording = true;

    env.Reset(seed: 1);

    var obs = new float[env.InputCount];
    var act = new float[env.OutputCount];
    int taken = 0;

    for (int i = 0; i < steps && !env.IsTerminal(); i++)
    {
        env.GetObservations(obs);
        Array.Clear(act);
        policy(obs, act);
        env.Step(act);
        taken++;
    }

    string path = Path.Combine(outDir, name + ".svg");
    var svg = new Svg(path, size, title: name);

    string kind;
    if (env is IAnimatedEnvironment a && a.FrameCount >= 2)
    {
        a.RenderAnimated(svg);
        kind = $"animated, {a.FrameCount} frames";
    }
    else
    {
        env.Render(svg);
        kind = "still";
    }

    svg.SaveToFile();
    Console.WriteLine($"  {name,-12} {taken,4}/{steps} steps  terminal={env.IsTerminal(),-5}  {kind}");
}
