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

// ---- worlds that move: animated replays -----------------------------------------------------

// obs = [cartPos, cartVel, poleAngle, poleAngleVel], ALL normalised (angle by MAX_ANGLE_RADIANS,
// rates by 5). Gains are therefore in normalised units — a gain of 6 saturates the [-1,1] action on
// the first tilt and degrades to bang-bang. Pole terms only: adding position terms makes the
// controller fight the pole to get home and it drops it.
Render("cartpole", new CartPoleEnvironment(), steps: 400, size: new Vector2(480, 220),
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
    size: new Vector2(480, 220),
    policy: (obs, act) => act[0] = obs[2] * -9.72f + obs[3] * -2.208f
                                 + obs[4] * 6.903f + obs[5] * 0.728f);

Render("targetchase", new TargetChaseEnvironment(), steps: 300, size: new Vector2(420, 420),
    policy: (obs, act) => { act[0] = obs[0] * 10f; act[1] = obs[1] * 10f; });  // steer at the target

// Shown at the start line on purpose. This corridor is a real benchmark, not a toy: its centreline
// is a 30-amplitude sine of wavelength ~126, so it pitches at up to 56 degrees, while steering
// authority is only (steering * 0.08 * speed/MAX_SPEED) per step — you cannot hand-write a driver
// for it, which is why its own test is [Fact(Skip = "Slow test")] and wants evolution.
//
// Hand-rolled attempts either crashed at x~11 or sailed straight out the open end at x~258 having
// collected 1 of 41 checkpoints. Animating that would advertise a bad policy, not the world. What
// the gallery owes you is the track, the checkpoint chain and the car — so: the start line.
Render("corridor", new SimpleCorridorEnvironment(), steps: 1, size: new Vector2(600, 300),
    policy: (obs, act) => { act[0] = 0f; act[1] = 0.2f; }, animate: false);

// ---- worlds that don't move: stills ---------------------------------------------------------
// XOR and Spiral are static datasets — the picture IS the dataset, and an animation of a ring
// hopping between fixed points would be motion for its own sake. Landscape is a still for a
// different reason: its heatmap is 1,600 rects re-sliced through the agent's position every frame,
// so animating it honestly would mean animating all of them.

Render("xor", new XOREnvironment(), steps: 2, size: new Vector2(320, 320),
    policy: (obs, act) => act[0] = 0.5f);

Render("spiral", new SpiralEnvironment(), steps: 5, size: new Vector2(400, 400),
    policy: (obs, act) => act[0] = 0.5f);

Render("landscape",
    new LandscapeEnvironment(new LandscapeNavigationTask(
        OptimizationLandscapes.Rosenbrock, dimensions: 2, timesteps: 50)),
    steps: 20, size: new Vector2(400, 400),
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
