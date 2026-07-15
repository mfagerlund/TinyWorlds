using FluentSvg;
using System.Numerics;
namespace TinyWorlds;

/// <summary>
/// XOR environment for testing neural network learning.
/// Presents 4 XOR test cases and measures output accuracy.
/// </summary>
public class XOREnvironment : IEnvironment
{
    private static readonly (float, float, float)[] TestCases = new[]
    {
        (0f, 0f, 0f), // 0 XOR 0 = 0
        (0f, 1f, 1f), // 0 XOR 1 = 1
        (1f, 0f, 1f), // 1 XOR 0 = 1
        (1f, 1f, 0f)  // 1 XOR 1 = 0
    };

    private int _currentCase;
    private float _totalError;

    public int InputCount => 2;
    public int OutputCount => 1;
    public int MaxSteps => 4; // One step per test case

    public void Reset(int seed = 0)
    {
        _currentCase = 0;
        _totalError = 0f;
    }

    public void GetObservations(Span<float> observations)
    {
        if (_currentCase >= TestCases.Length)
        {
            observations[0] = 0f;
            observations[1] = 0f;
            return;
        }

        var (x, y, _) = TestCases[_currentCase];
        observations[0] = x;
        observations[1] = y;
    }

    public float Step(ReadOnlySpan<float> actions)
    {
        if (_currentCase >= TestCases.Length)
            return 0f;

        var (_, _, expected) = TestCases[_currentCase];
        float output = actions[0];

        // Compute error (squared difference)
        float error = (output - expected) * (output - expected);
        _totalError += error;

        _currentCase++;

        // Return reward at end of episode
        if (_currentCase >= TestCases.Length)
        {
            // Fitness = -average_error (higher is better)
            // Perfect solution = 0 error = 0 fitness
            // Bad solution = high error = negative fitness
            return -(_totalError / TestCases.Length);
        }

        return 0f; // Intermediate steps have no reward
    }

    public bool IsTerminal()
    {
        return _currentCase >= TestCases.Length;
    }

    /// <summary>
    /// The four XOR corners, with the case currently being presented ringed in orange. Fill shows
    /// the target class (filled = 1, hollow = 0). A truth table has no geometry, so this is the
    /// honest picture: what it asks, and where it is.
    /// </summary>
    public void Render(Svg svg)
    {
        (float x, float y, float t)[] cases =
        [
            (0f, 0f, 0f),
            (0f, 1f, 1f),
            (1f, 0f, 1f),
            (1f, 1f, 0f),
        ];

        svg.AddRectangleFromTo(SvgCoords.P(-0.2f, -0.2f), SvgCoords.P(1.2f, 1.2f))
           .SetFill("transparent").SetStroke("#ddd").SetStrokeWidth(0.01);

        for (int i = 0; i < cases.Length; i++)
        {
            var c = cases[i];
            var p = SvgCoords.P(c.x, c.y);
            svg.AddCircle(p, 0.08f)
               .SetFill(c.t > 0.5f ? "#2c3e50" : "transparent")
               .SetStroke("#2c3e50").SetStrokeWidth(0.02);

            if (i == _currentCase % cases.Length)
                svg.AddCircle(p, 0.15f)
                   .SetFill("transparent").SetStroke("#e67e22").SetStrokeWidth(0.02);
        }
    }
}
