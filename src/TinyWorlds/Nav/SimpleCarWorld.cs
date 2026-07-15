using System.Numerics;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentSvg;

namespace TinyWorlds;

/// <summary>
/// A drift-happy car on a hand-drawn race track, scored on how far around it gets.
///
/// The track is a real authored SVG, not a procedure, which is what makes this different from
/// <see cref="SimpleCorridorEnvironment"/>: the corners have no pattern to learn, so a policy has
/// to actually read its sensors.
///
/// This class is the track and the scoring; <see cref="SimpleCar"/> is the vehicle, and
/// <see cref="FollowTheCorridorEnvironment"/> is the <see cref="IEnvironment"/> wrapper around the
/// pair. Loading parses the SVG, so build one world and share it across a population rather than
/// constructing one per individual.
///
/// COORDINATES: this is the one world in TinyWorlds that is not +y-up. Its geometry comes out of an
/// SVG file, so it lives in that file's frame, where +y is down, and it renders without the
/// <see cref="SvgCoords"/> flip the other worlds apply. Mirroring it at load to satisfy the
/// convention would buy nothing — a top-down track has no up, and there is no gravity for "down"
/// to mean anything to — while costing the ability to check this port against the implementation it
/// was lifted from. Left as-is on purpose.
/// </summary>
public class SimpleCarWorld(
    Grid wallGrid,
    List<SimpleCarWorld.ProgressMarker> progressMarkers,
    Grid.LineSegment start,
    Grid.LineSegment finish,
    int maxSteps)
{
    public const float TotalGridSize = 256;
    public const int GridSize = 16;

    public Grid WallGrid { get; } = wallGrid;
    public List<ProgressMarker> ProgressMarkers { get; set; } = progressMarkers;
    public Grid.LineSegment Start { get; } = start;
    public Grid.LineSegment Finish { get; } = finish;
    public float ProgressMarkerRadius => 12;
    public int MaxSteps { get; set; } = maxSteps;

    public float Update(SimpleCar simpleCar, float[] action)
    {
        if (simpleCar.IsDead)
        {
            return 0;
        }

        simpleCar.Update(
            action[0],
            action[1]);

        var deltaReward = -0.05f / MaxSteps;

        var hit = WallGrid.GetEdgesOverlappingCircle(simpleCar.Position, SimpleCar.Radius).FirstOrDefault();
        if (hit != null)
        {
            simpleCar.IsDead = true;

            // Glancing blows hurt less than head-on ones: scale the damage by how aligned the car's
            // travel was with the wall it hit.
            Vector2 collisionEdgeDirection = (hit.End - hit.Start).Normalized();
            Vector2 carVelocityDirection = simpleCar.Velocity.Normalized();

            float impactFactor = Math.Abs(Vector2.Dot(collisionEdgeDirection, carVelocityDirection));
            float damage = -Sqr(simpleCar.Speed / simpleCar.MaxSpeed) * impactFactor;

            return damage * 0.1f;
        }

        while (simpleCar.CurrentProgressMarkerId < ProgressMarkers.Count)
        {
            var progressMarker = ProgressMarkers[simpleCar.CurrentProgressMarkerId];
            if ((simpleCar.Position - progressMarker.Position).Length() < ProgressMarkerRadius)
            {
                deltaReward += 1f / ProgressMarkers.Count;
                simpleCar.CurrentProgressMarkerId++;
            }
            else
            {
                break;
            }
        }

        if (simpleCar.CurrentProgressMarkerId == 4 && !simpleCar.StepWhenReached4thMarker.HasValue)
        {
            simpleCar.StepWhenReached4thMarker = simpleCar.UpdateCount;
        }

        // Early kill: a car that hasn't got going by now is idling or circling, and simulating it
        // for the remaining steps buys nothing.
        if (!simpleCar.StepWhenReached4thMarker.HasValue && simpleCar.UpdateCount > 100)
        {
            simpleCar.IsDead = true;
            return -0.5f;
        }

        if (simpleCar.CurrentProgressMarkerId == ProgressMarkers.Count)
        {
            deltaReward += (1 - (float)simpleCar.UpdateCount / MaxSteps) * 0.05f;
            deltaReward += 0.05f;
            simpleCar.IsDead = true;
        }

        if (simpleCar.UpdateCount > 5 && (simpleCar.Position - simpleCar.StartPosition).Length() < 2f)
        {
            deltaReward -= 1f / MaxSteps;
        }

        deltaReward -= Math.Abs(simpleCar.SteeringInput) * 0.0001f / MaxSteps;
        simpleCar.RewardSum += deltaReward;
        return deltaReward;
    }

    private static float Sqr(float x) => x * x;

    public class ProgressMarker(Vector2 position)
    {
        public Vector2 Position { get; set; } = position;
    }

    /// <summary>
    /// Builds the world from the race track embedded in this assembly. Parses SVG, so call it once
    /// and share the result.
    /// </summary>
    public static SimpleCarWorld Load(int maxSteps = 320)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("TinyWorlds.Nav.RaceTrack.svg")
            ?? throw new InvalidOperationException(
                "Embedded resource 'TinyWorlds.Nav.RaceTrack.svg' is missing. It is declared as an " +
                "EmbeddedResource in TinyWorlds.csproj; a rename of the file or folder breaks the " +
                "resource name, which is derived from the path.");
        using var reader = new StreamReader(stream);
        return LoadFromSvg(reader.ReadToEnd(), maxSteps);
    }

    /// <summary>Builds a world from SVG text with the layer ids this world expects.</summary>
    public static SimpleCarWorld LoadFromSvg(string svgText, int maxSteps = 320)
    {
        var paths = GetVectorPaths(svgText);
        List<ProgressMarker>? progressMarkers = null;
        Rescale(paths);

        var wallGrid = new Grid(GridSize, Vector2.One * TotalGridSize);
        Grid.LineSegment? start = null;
        Grid.LineSegment? finish = null;
        foreach (var path in paths)
        {
            switch (path.id)
            {
                case "Side 1":
                case "Side 2":
                {
                    foreach (var subPath in path.subPaths)
                    {
                        for (var i = 0; i < subPath.Count - 1; i++)
                        {
                            wallGrid.AddLine(new Grid.LineSegment(subPath[i], subPath[i + 1]) { Id = path.id });
                        }
                    }

                    break;
                }
                case "Progress":
                {
                    progressMarkers = ExtractProgressMarkers(path.subPaths[0]);
                    break;
                }
                case "Start":
                    start = new Grid.LineSegment(path.subPaths[0][0], path.subPaths[0][1]) { Id = path.id };
                    break;
                case "Finish":
                    finish = new Grid.LineSegment(path.subPaths[0][0], path.subPaths[0][1]) { Id = path.id };
                    break;
                default:
                    throw new InvalidOperationException($"Unhandled id:{path.id}");
            }
        }

        if (progressMarkers == null || start == null || finish == null)
        {
            throw new InvalidOperationException(
                "Track SVG must contain 'Progress', 'Start' and 'Finish' paths.");
        }

        return new SimpleCarWorld(wallGrid, progressMarkers, start, finish, maxSteps);
    }

    public static List<(string id, List<List<Vector2>> subPaths)> GetVectorPaths(string svgText, float scale = 1)
        => GetTextPaths(svgText)
            .Select(p => (p.id, subPaths: Svg.DecodePath(p.pathData, scale)))
            .ToList();

    public static List<(string id, string pathData)> GetTextPaths(string svgText)
    {
        var result = new List<(string id, string pathData)>();
        var pathRegex = new Regex(@"<path[^>]*\/>");
        var attributeRegex = new Regex(@"id=""([^""]+)""|d=""([^""]+)""");

        foreach (Match pathMatch in pathRegex.Matches(svgText))
        {
            if (!pathMatch.Success)
            {
                continue;
            }

            var id = "null";
            string? pathData = null;

            // Last id wins, and that is load-bearing rather than sloppy: the track was drawn in
            // Affinity, which writes both id="Side-2" and serif:id="Side 2". The pattern is
            // unanchored, so it matches inside serif:id too, and the spaced name is the one the
            // switch above expects.
            foreach (Match attributeMatch in attributeRegex.Matches(pathMatch.Value))
            {
                if (attributeMatch.Groups[1].Success)
                {
                    id = attributeMatch.Groups[1].Value;
                }
                else if (attributeMatch.Groups[2].Success)
                {
                    pathData = attributeMatch.Groups[2].Value;
                }
            }

            result.Add((id, pathData!));
        }

        return result;
    }

    /// <summary>
    /// Resamples the drawn "Progress" spline into evenly spaced markers. The drawing's own vertices
    /// bunch up on corners and thin out on straights, so they'd make progress reward depend on how
    /// the track was drawn.
    /// </summary>
    private static List<ProgressMarker> ExtractProgressMarkers(List<Vector2> path)
    {
        var numberOfMarkers = 256;
        if (path == null || path.Count < 2)
        {
            throw new ArgumentException("Path must contain at least two points.");
        }

        var markers = new List<ProgressMarker>();
        var totalLength = CalculateTotalLength(path);

        var spacing = totalLength / (numberOfMarkers - 1);
        var distanceSoFar = 0f;

        markers.Add(new ProgressMarker(path[0]));

        for (var i = 1; i < path.Count; i++)
        {
            var start = path[i - 1];
            var end = path[i];
            var segmentLength = Vector2.Distance(start, end);

            while (distanceSoFar + segmentLength >= spacing)
            {
                var t = (spacing - distanceSoFar) / segmentLength;
                var marker = Vec.Lerp(start, end, t);
                markers.Add(new ProgressMarker(marker));

                distanceSoFar = 0f;
                start = marker;
                segmentLength = Vector2.Distance(start, end);
            }

            distanceSoFar += segmentLength;
        }

        markers.Add(new ProgressMarker(path[^1]));

        return markers;
    }

    private static float CalculateTotalLength(List<Vector2> path)
    {
        var length = 0f;

        for (var i = 1; i < path.Count; i++)
        {
            length += Vector2.Distance(path[i - 1], path[i]);
        }

        return length;
    }

    /// <summary>Fits the drawing, whatever size it was authored at, into the 0..256 grid.</summary>
    private static void Rescale(List<(string id, List<List<Vector2>> subPaths)> paths)
    {
        var points = paths.SelectMany(p => p.subPaths.SelectMany(pd => pd)).ToList();
        var minMax = points.GetMinMax();
        var range = minMax.max - minMax.min;
        var scale = TotalGridSize / Math.Max(range.X, range.Y);
        foreach (var path in paths)
        {
            foreach (var subPath in path.subPaths)
            {
                for (var index = 0; index < subPath.Count; index++)
                {
                    subPath[index] = (subPath[index] - minMax.min) * scale;
                }
            }
        }
    }

    /// <summary>
    /// Draws the track. Not the car — the world doesn't own one, several can drive the same track —
    /// so callers that want a car draw it themselves; see
    /// <see cref="FollowTheCorridorEnvironment.Render"/>.
    /// </summary>
    public void Render(Svg svg, bool renderProgressMarkers = false)
    {
        svg.AddRectangleSized(Vector2.Zero, WallGrid.TotalGridSize);
        foreach (var lineSegment in WallGrid.LineSegments)
        {
            RenderLineSegment(svg, lineSegment);
        }

        RenderLineSegment(svg, Start).SetStroke("darkblue").SetStrokeWidth(0.6f);
        svg.AddCircle(Start.MidPoint, 2).SetStroke("green");
        RenderLineSegment(svg, Finish).SetStroke("darkgreen").SetStrokeWidth(0.6f);
        if (renderProgressMarkers)
        {
            foreach (var progressMarker in ProgressMarkers)
            {
                svg.AddCircle(progressMarker.Position, ProgressMarkerRadius)
                   .SetStrokeWidth(0.3f).SetStroke("green").SetStrokeOpacity(0.5f);
            }
        }
    }

    private static Svg.Line RenderLineSegment(Svg svg, Grid.LineSegment lineSegment)
        => svg.AddLine(lineSegment.Start, lineSegment.End).SetStrokeWidth(0.3f);

    /// <summary>
    /// The vehicle. Deliberately loose: steering authority grows with speed (so it oversteers when
    /// you most want it not to) and velocity only blends halfway toward the direction it's pointing
    /// each step, which makes it drift through corners. Both are what stop this being a trivial
    /// "point at the next marker" problem.
    /// </summary>
    public class SimpleCar
    {
        public Vector2 Position { get; private set; }
        public Vector2 Velocity { get; private set; } = Vector2.Zero;
        public float SteeringInput { get; private set; }
        public float SteeringAngle { get; private set; }
        public float HeadingAngle { get; private set; }
        public Vector2 Direction { get; private set; } = new(0, 0);
        public float DeltaTime => 0.2f;

        public static float Radius { get; set; } = 2;
        public float Speed { get; private set; }

        public float StartHeadingAngle { get; set; }
        public Vector2 StartPosition { get; set; }

        public SimpleCar(SimpleCarWorld world) : this(
            world.Start.MidPoint,
            world.Start.Angle + (float)Math.PI / 2)
        {
        }

        public SimpleCar(Vector2 position, float headingAngle = 0)
        {
            StartPosition = position;
            StartHeadingAngle = headingAngle;
            Reset();
            InitializeSensors();
        }

        public float MaxSteeringAngle { get; set; } = 30f * ((float)Math.PI / 180f);
        public float MaxSpeed { get; set; } = 16;
        public float AccelerationRate { get; set; } = 16;
        public float DecelerationRate { get; set; } = 16;
        public float SteeringSensitivity { get; set; } = 50f;
        public Sensor[] Sensors { get; set; } = [];
        public int CurrentProgressMarkerId { get; set; } = 0;
        public float RewardSum { get; set; }
        public bool IsDead { get; set; }
        public int UpdateCount { get; set; }
        public int? StepWhenReached4thMarker { get; set; }

        public void Reset()
        {
            IsDead = false;
            Position = StartPosition;
            HeadingAngle = StartHeadingAngle;
            SteeringAngle = 0;
            CurrentProgressMarkerId = 0;
            RewardSum = 0;
            Speed = 0;
            Velocity = Vector2.Zero;
            UpdateCount = 0;
            StepWhenReached4thMarker = null;
        }

        /// <summary>
        /// Nine range-finders: one straight ahead and four symmetric pairs out to 60 degrees. The
        /// forward one reaches furthest because that's where speed makes lookahead matter.
        /// </summary>
        private void InitializeSensors()
        {
            float rangeScale = 2.5f;
            var sensors = new List<Sensor>
            {
                new Sensor(this, 0f, rangeScale * 17f)
            };

            AddDoubleSensor(sensors, 15, rangeScale * 12);
            AddDoubleSensor(sensors, 30, rangeScale * 10);
            AddDoubleSensor(sensors, 45, rangeScale * 8);
            AddDoubleSensor(sensors, 60, rangeScale * 5);
            Sensors = sensors.ToArray();
        }

        private void AddDoubleSensor(List<Sensor> sensors, float angle, float range)
        {
            sensors.Add(new Sensor(this, -angle, range));
            sensors.Add(new Sensor(this, angle, range));
        }

        public void Update(float steeringInput, float throttleInput)
        {
            UpdateCount++;

            // Steering authority scales with speed, and then some: the quadratic term means the car
            // turns in hardest exactly when it's going too fast to want that.
            float speedFactor = Speed / MaxSpeed;
            speedFactor = speedFactor * (1 + speedFactor * 0.8f);
            SteeringInput = Math.Clamp(steeringInput, -1, 1);
            SteeringAngle += SteeringInput * SteeringSensitivity * DeltaTime * speedFactor;
            SteeringAngle = Math.Clamp(SteeringAngle, -MaxSteeringAngle, MaxSteeringAngle);

            HeadingAngle += SteeringAngle * DeltaTime;

            Direction = new Vector2((float)Math.Cos(HeadingAngle), (float)Math.Sin(HeadingAngle));

            // Acceleration falls off as you approach top speed; braking is just as weak, so you
            // cannot scrub speed off in a hurry.
            throttleInput = Math.Clamp(throttleInput, -1, 1);
            if (throttleInput > 0)
            {
                float effectiveAccelerationRate = AccelerationRate * (1 - Speed / MaxSpeed);
                Speed += effectiveAccelerationRate * DeltaTime * throttleInput;
            }
            else if (throttleInput < 0)
            {
                float effectiveDecelerationRate = DecelerationRate * (1 - Speed / MaxSpeed);
                Speed -= effectiveDecelerationRate * DeltaTime * Math.Abs(throttleInput);
            }

            Speed = Math.Clamp(Speed, 0, MaxSpeed);

            // Drift: velocity lags the direction the car points, 50/50 per step.
            Vector2 targetVelocity = Direction * Speed;
            Velocity = Velocity * 0.5f + targetVelocity * 0.5f;

            Position += Velocity * DeltaTime;
        }

        /// <summary>Fires every sensor against the walls and returns their readings, 0..1.</summary>
        public float[] GetState(Grid wallGrid)
        {
            float[] values = new float[Sensors.Length];
            for (var index = 0; index < Sensors.Length; index++)
            {
                var sensor = Sensors[index];
                sensor.Update(wallGrid);
                values[index] = sensor.SensorValue;
            }

            return values;
        }

        public float[] GetStateAddendum()
        {
            var nose = Position + Direction * Radius * 0.5f;
            return [Position.X, Position.Y, nose.X, nose.Y];
        }
    }

    /// <summary>
    /// A range-finder bolted to the car at a fixed angle.
    ///
    /// KNOWN WART, preserved from the original: the reading is 1 when a wall is touching the car
    /// and falls to 0 as that wall reaches the sensor's maximum range — but a sensor that hits
    /// NOTHING also reads 1. So "wall in my face" and "open road" produce the same number, and a
    /// wall receding past maximum range makes the reading jump discontinuously from ~0 to 1. It
    /// should almost certainly be 0 for a miss.
    ///
    /// Left alone deliberately. This is a port, and changing behaviour mid-port destroys the only
    /// way to tell a port bug from an intended change. Fixing it is a separate, testable commit —
    /// and it will invalidate every policy trained against this world, which is the honest cost.
    /// </summary>
    public class Sensor(SimpleCar simpleCar, float angle, float range)
    {
        public SimpleCar SimpleCar { get; } = simpleCar;
        public float Angle { get; } = angle * (float)Math.PI / 180;
        public float Range { get; } = range;
        public Grid.LineSegment? Ray { get; private set; }
        public (Vector2? collision, float? distance) Intersection { get; set; }
        public float SensorValue { get; private set; }

        public Grid.LineSegment GetSensorRay()
        {
            float sensorAngleRadians = Angle + SimpleCar.HeadingAngle;
            Vector2 sensorDirection = new Vector2(
                (float)Math.Cos(sensorAngleRadians),
                (float)Math.Sin(sensorAngleRadians)
            );

            return new Grid.LineSegment(
                SimpleCar.Position,
                SimpleCar.Position + sensorDirection.Normalized() * Range
            );
        }

        public void Update(Grid wallGrid)
        {
            Ray = GetSensorRay();
            Intersection = wallGrid.GetClosestLineIntersection(Ray);
            SensorValue = !Intersection.distance.HasValue
                ? 1
                : 1 - Intersection.distance.Value / Range;
        }
    }
}
