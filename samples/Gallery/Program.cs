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

Render("cartpole", new CartPoleEnvironment(), steps: 40, size: new Vector2(480, 200),
    policy: (obs, act) => act[0] = obs[2] * 4f + obs[3]);   // nudge toward upright

Render("doublepole", new DoublePoleEnvironment(), steps: 60, size: new Vector2(480, 200),
    policy: (obs, act) => act[0] = obs[2] * 4f);

Render("xor", new XOREnvironment(), steps: 2, size: new Vector2(320, 320),
    policy: (obs, act) => act[0] = 0.5f);

Render("spiral", new SpiralEnvironment(), steps: 5, size: new Vector2(400, 400),
    policy: (obs, act) => act[0] = 0.5f);

Render("targetchase", new TargetChaseEnvironment(), steps: 60, size: new Vector2(420, 420),
    policy: (obs, act) => { act[0] = obs[0] * 10f; act[1] = obs[1] * 10f; });  // steer at the target

// Shown at the start line on purpose. This corridor is a real benchmark, not a toy: its centreline
// is a 30-amplitude sine of wavelength ~126, so it pitches at up to 56 degrees, while steering
// authority is only (steering * 0.08 * speed/MAX_SPEED) per step — you cannot hand-write a driver
// for it, which is why its own test is [Fact(Skip = "Slow test")] and wants evolution.
//
// A few hand-rolled attempts either crashed at x~11 or sailed straight out the open end at x~258
// having collected 1 of 41 checkpoints. Showing that would advertise a bad policy, not the world.
// What the gallery owes you is the track, the checkpoint chain and the car — so: the start line.
Render("corridor", new SimpleCorridorEnvironment(), steps: 1, size: new Vector2(600, 300),
    policy: (obs, act) => { act[0] = 0f; act[1] = 0.2f; });

Render("landscape",
    new LandscapeEnvironment(new LandscapeNavigationTask(
        OptimizationLandscapes.Rosenbrock, dimensions: 2, timesteps: 50)),
    steps: 20, size: new Vector2(400, 400),
    policy: (obs, act) => { for (int i = 0; i < act.Length; i++) act[i] = -obs[i]; });

Console.WriteLine($"gallery written to {Path.GetFullPath(outDir)}");

void Render(string name, IEnvironment env, int steps, Vector2 size, Action<float[], float[]> policy)
{
    env.Reset(seed: 1);

    var obs = new float[env.InputCount];
    var act = new float[env.OutputCount];

    for (int i = 0; i < steps && !env.IsTerminal(); i++)
    {
        env.GetObservations(obs);
        Array.Clear(act);
        policy(obs, act);
        env.Step(act);
    }

    string path = Path.Combine(outDir, name + ".svg");
    var svg = new Svg(path, size, title: name);
    env.Render(svg);
    svg.SaveToFile();

    Console.WriteLine($"  {name,-12} {steps,3} steps, terminal={env.IsTerminal(),-5} -> {name}.svg");
}
