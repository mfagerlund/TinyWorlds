using System.Numerics;

namespace TinyWorlds;

/// <summary>
/// A uniform spatial hash over static line segments, so a sensor ray tests against the handful of
/// walls near it rather than all several hundred. The whole race track is ~500 segments and each
/// car fires 9 rays per step across a population of hundreds — the brute-force version dominates
/// the profile.
/// </summary>
public class Grid
{
    private readonly List<LineSegment>[,] _cells;

    /// <param name="gridSize">Cells per side. The grid is square.</param>
    /// <param name="totalGridSize">World-space extent the grid covers, starting at the origin.</param>
    public Grid(int gridSize, Vector2 totalGridSize)
    {
        GridSize = gridSize;
        CellSize = totalGridSize / gridSize;
        TotalGridSize = totalGridSize;
        _cells = new List<LineSegment>[gridSize, gridSize];
        for (int y = 0; y < gridSize; y++)
        {
            for (int x = 0; x < gridSize; x++)
            {
                _cells[x, y] = new List<LineSegment>();
            }
        }
    }

    public HashSet<LineSegment> LineSegments { get; } = [];
    public int GridSize { get; }
    public Vector2 CellSize { get; }
    public Vector2 TotalGridSize { get; }

    public void AddLine(LineSegment line)
    {
        LineSegments.Add(line);
        foreach (var cell in GetCells(line))
        {
            _cells[cell.X, cell.Y].Add(line);
        }
    }

    public List<LineSegment> GetEdgesOverlappingCircle(Vector2 center, float radius)
    {
        var overlappingEdges = new HashSet<LineSegment>();
        var minCell = ToCell(center - new Vector2(radius, radius));
        var maxCell = ToCell(center + new Vector2(radius, radius));

        for (int x = minCell.X; x <= maxCell.X; x++)
        {
            for (int y = minCell.Y; y <= maxCell.Y; y++)
            {
                if (!Contains(x, y))
                {
                    continue;
                }

                foreach (var line in _cells[x, y])
                {
                    if (line.Overlaps(center, radius))
                    {
                        overlappingEdges.Add(line);
                    }
                }
            }
        }

        return overlappingEdges.ToList();
    }

    public (Vector2? collision, float? distance) GetClosestLineIntersection(LineSegment sensor)
    {
        Vector2? closestPoint = null;
        (int X, int Y)? closestPointCell = null;
        float? closestDistance = null;

        var visited = new HashSet<LineSegment>();
        foreach (var cell in GetCells(sensor))
        {
            foreach (var staticLine in _cells[cell.X, cell.Y])
            {
                if (!visited.Add(staticLine))
                {
                    // Already tested — segments span multiple cells.
                    continue;
                }

                // sensor.GetIntersection(wall), not the reverse: the intersection point is
                // computed by parameterising along the SECOND segment, so swapping the arguments
                // rounds differently.
                var intersection = sensor.GetIntersection(staticLine);
                if (intersection.HasValue)
                {
                    var distance = (sensor.Start - intersection.Value).Length();
                    if (distance < (closestDistance ?? float.MaxValue))
                    {
                        closestDistance = distance;
                        closestPoint = intersection;
                        closestPointCell = ToCell(intersection.Value);
                    }
                }
            }

            if (closestPoint.HasValue && closestPointCell == cell)
            {
                // We have a hit AND it landed in the cell we're currently walking, so no later cell
                // can beat it. Checking the cell (rather than just "we have a hit") matters: when a
                // ray runs nearly parallel to a wall, a wall met in this cell can intersect it at a
                // point that lies several cells further along.
                break;
            }
        }

        return (closestPoint, closestDistance);
    }

    /// <summary>
    /// The cells a segment passes through, in order from start to end, skipping any that fall
    /// outside the grid. Amanatides-Woo style DDA traversal.
    /// </summary>
    public IEnumerable<(int X, int Y)> GetCells(LineSegment line)
    {
        Vector2 start = line.Start;
        Vector2 end = line.End;

        var startCell = ToCell(start);
        var endCell = ToCell(end);

        int stepX = (end.X > start.X) ? 1 : (end.X < start.X) ? -1 : 0;
        int stepY = (end.Y > start.Y) ? 1 : (end.Y < start.Y) ? -1 : 0;

        float tMaxX = (stepX != 0)
            ? ((stepX > 0 ? (startCell.X + 1) * CellSize.X : startCell.X * CellSize.X) - start.X) / (end.X - start.X)
            : float.PositiveInfinity;
        float tMaxY = (stepY != 0)
            ? ((stepY > 0 ? (startCell.Y + 1) * CellSize.Y : startCell.Y * CellSize.Y) - start.Y) / (end.Y - start.Y)
            : float.PositiveInfinity;

        float tDeltaX = (stepX != 0) ? CellSize.X / MathF.Abs(end.X - start.X) : float.PositiveInfinity;
        float tDeltaY = (stepY != 0) ? CellSize.Y / MathF.Abs(end.Y - start.Y) : float.PositiveInfinity;

        var currentCell = startCell;
        if (Contains(currentCell.X, currentCell.Y))
        {
            yield return currentCell;
        }

        int hits = 0;

        while (currentCell != endCell)
        {
            if (tMaxX < tMaxY)
            {
                tMaxX += tDeltaX;
                currentCell.X += stepX;
            }
            else
            {
                tMaxY += tDeltaY;
                currentCell.Y += stepY;
            }

            if (Contains(currentCell.X, currentCell.Y))
            {
                yield return currentCell;
            }

            if (hits++ > 1000)
            {
                throw new InvalidOperationException("GetCells failed - too many cells retrieved!");
            }
        }
    }

    private bool Contains(int x, int y) => x >= 0 && y >= 0 && x < GridSize && y < GridSize;

    private (int X, int Y) ToCell(Vector2 worldPosition)
    {
        var cell = worldPosition / CellSize;
        return ((int)MathF.Floor(cell.X), (int)MathF.Floor(cell.Y));
    }

    public class LineSegment(Vector2 start, Vector2 end)
    {
        public string? Id { get; set; }
        public Vector2 Start { get; } = start;
        public Vector2 End { get; } = end;
        public Vector2 MidPoint => (Start + End) / 2;

        public float Angle => (End - Start).AngleRad();
        public override string ToString() => $"Id={Id}, Start={Start}, End={End}";

        public Vector2? GetIntersection(LineSegment other) =>
            Vec.IntersectSegments(Start, End, other.Start, other.End);

        public bool Overlaps(Vector2 center, float radius)
        {
            Vector2 d = End - Start;
            Vector2 f = Start - center;

            float a = Vector2.Dot(d, d);
            float b = 2 * Vector2.Dot(f, d);
            float c = Vector2.Dot(f, f) - radius * radius;

            float discriminant = b * b - 4 * a * c;
            if (discriminant < 0)
            {
                return false;
            }

            discriminant = (float)Math.Sqrt(discriminant);

            float t1 = (-b - discriminant) / (2 * a);
            float t2 = (-b + discriminant) / (2 * a);

            return t1 >= 0 && t1 <= 1 || t2 >= 0 && t2 <= 1;
        }
    }
}
