using System;
using System.Collections.Generic;
using UnityEngine;

namespace UavUsv
{
    /// <summary>
    /// Grid A* planner in Gazebo ENU coordinates. The implementation mirrors the
    /// Nav2 configuration used by the ROS project while keeping planning visible
    /// and executable from Unity.
    /// </summary>
    public sealed class GridAStarPlanner
    {
        public readonly struct CircularObstacle
        {
            public CircularObstacle(Vector2 center, float radius)
            {
                Center = center;
                Radius = Mathf.Max(0f, radius);
            }

            public Vector2 Center { get; }
            public float Radius { get; }
        }

        private readonly float minX;
        private readonly float minY;
        private readonly float maxX;
        private readonly float maxY;
        private readonly float cellSize;
        private readonly int width;
        private readonly int height;

        private static readonly Vector2Int[] Neighbours =
        {
            new Vector2Int(-1, -1),
            new Vector2Int(0, -1),
            new Vector2Int(1, -1),
            new Vector2Int(-1, 0),
            new Vector2Int(1, 0),
            new Vector2Int(-1, 1),
            new Vector2Int(0, 1),
            new Vector2Int(1, 1)
        };

        public GridAStarPlanner(float minX, float minY, float maxX, float maxY, float cellSize)
        {
            if (cellSize <= 0f)
                throw new ArgumentOutOfRangeException(nameof(cellSize));

            this.minX = Mathf.Min(minX, maxX);
            this.minY = Mathf.Min(minY, maxY);
            this.maxX = Mathf.Max(minX, maxX);
            this.maxY = Mathf.Max(minY, maxY);
            this.cellSize = cellSize;
            width = Mathf.FloorToInt((this.maxX - this.minX) / cellSize) + 1;
            height = Mathf.FloorToInt((this.maxY - this.minY) / cellSize) + 1;
        }

        public bool TryPlan(
            Vector2 start,
            Vector2 goal,
            IReadOnlyList<CircularObstacle> obstacles,
            out List<Vector2> path,
            out int expandedNodes)
        {
            return TryPlan(
                start,
                goal,
                obstacles,
                null,
                out path,
                out expandedNodes
            );
        }

        public bool TryPlan(
            Vector2 start,
            Vector2 goal,
            IReadOnlyList<CircularObstacle> obstacles,
            System.Func<Vector2, bool> additionalBlocked,
            out List<Vector2> path,
            out int expandedNodes)
        {
            path = new List<Vector2>();
            expandedNodes = 0;
            if (!InBounds(start) || !InBounds(goal))
                return false;

            Vector2Int startCell = ToCell(start);
            Vector2Int goalCell = ToCell(goal);
            bool[] blocked = BuildBlockedGrid(
                obstacles,
                additionalBlocked,
                startCell,
                goalCell
            );
            int nodeCount = width * height;
            float[] gScore = new float[nodeCount];
            int[] cameFrom = new int[nodeCount];
            bool[] closed = new bool[nodeCount];
            for (int i = 0; i < nodeCount; i++)
            {
                gScore[i] = float.PositiveInfinity;
                cameFrom[i] = -1;
            }

            int startIndex = Index(startCell);
            int goalIndex = Index(goalCell);
            var open = new MinHeap();
            gScore[startIndex] = 0f;
            open.Push(startIndex, Heuristic(startCell, goalCell));

            while (open.Count > 0)
            {
                int currentIndex = open.Pop();
                if (closed[currentIndex])
                    continue;

                closed[currentIndex] = true;
                expandedNodes++;
                if (currentIndex == goalIndex)
                {
                    path = ReconstructPath(cameFrom, currentIndex, start, goal);
                    path = Simplify(
                        path,
                        obstacles,
                        additionalBlocked,
                        cellSize
                    );
                    return true;
                }

                Vector2Int current = Cell(currentIndex);
                foreach (Vector2Int offset in Neighbours)
                {
                    Vector2Int next = current + offset;
                    if (!ValidCell(next))
                        continue;

                    int nextIndex = Index(next);
                    if (closed[nextIndex] || blocked[nextIndex])
                        continue;

                    bool diagonal = offset.x != 0 && offset.y != 0;
                    if (diagonal)
                    {
                        Vector2Int sideA = new Vector2Int(current.x + offset.x, current.y);
                        Vector2Int sideB = new Vector2Int(current.x, current.y + offset.y);
                        if (blocked[Index(sideA)] || blocked[Index(sideB)])
                            continue;
                    }

                    float stepCost = diagonal ? 1.41421356f : 1f;
                    float tentative = gScore[currentIndex] + stepCost;
                    if (tentative >= gScore[nextIndex])
                        continue;

                    cameFrom[nextIndex] = currentIndex;
                    gScore[nextIndex] = tentative;
                    open.Push(nextIndex, tentative + Heuristic(next, goalCell));
                }
            }

            return false;
        }

        private bool[] BuildBlockedGrid(
            IReadOnlyList<CircularObstacle> obstacles,
            System.Func<Vector2, bool> additionalBlocked,
            Vector2Int start,
            Vector2Int goal)
        {
            bool[] blocked = new bool[width * height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Vector2 point = ToWorld(new Vector2Int(x, y));
                    if (additionalBlocked != null && additionalBlocked(point))
                    {
                        blocked[y * width + x] = true;
                        continue;
                    }

                    if (obstacles != null)
                    {
                        for (int i = 0; i < obstacles.Count; i++)
                        {
                            CircularObstacle obstacle = obstacles[i];
                            if ((point - obstacle.Center).sqrMagnitude <=
                                obstacle.Radius * obstacle.Radius)
                            {
                                blocked[y * width + x] = true;
                                break;
                            }
                        }
                    }
                }
            }

            blocked[Index(start)] = false;
            blocked[Index(goal)] = false;
            if (obstacles != null)
                ClearStartEscapeCorridors(blocked, start, goal, obstacles);
            return blocked;
        }

        private void ClearStartEscapeCorridors(
            bool[] blocked,
            Vector2Int start,
            Vector2Int goal,
            IReadOnlyList<CircularObstacle> obstacles)
        {
            Vector2 startWorld = ToWorld(start);
            Vector2 goalWorld = ToWorld(goal);
            for (int i = 0; i < obstacles.Count; i++)
            {
                CircularObstacle obstacle = obstacles[i];
                Vector2 fromCenter = startWorld - obstacle.Center;
                float startDistance = fromCenter.magnitude;
                if (startDistance > obstacle.Radius)
                    continue;

                Vector2 direction = startDistance > .01f
                    ? fromCenter / startDistance
                    : (goalWorld - startWorld).normalized;
                if (direction.sqrMagnitude < .5f)
                    direction = Vector2.right;

                float escapeDistance = obstacle.Radius - startDistance + cellSize * 2f;
                for (float distance = 0f; distance <= escapeDistance; distance += cellSize * .5f)
                {
                    Vector2Int corridorCell = ToCell(startWorld + direction * distance);
                    for (int y = -1; y <= 1; y++)
                    {
                        for (int x = -1; x <= 1; x++)
                        {
                            Vector2Int clearCell = corridorCell + new Vector2Int(x, y);
                            if (ValidCell(clearCell))
                                blocked[Index(clearCell)] = false;
                        }
                    }
                }
            }
        }

        private List<Vector2> ReconstructPath(
            int[] cameFrom,
            int current,
            Vector2 exactStart,
            Vector2 exactGoal)
        {
            var reversed = new List<Vector2> { exactGoal };
            int startIndex = current;
            while (cameFrom[startIndex] >= 0)
            {
                startIndex = cameFrom[startIndex];
                reversed.Add(ToWorld(Cell(startIndex)));
            }

            reversed.Reverse();
            if (reversed.Count == 0 || Vector2.Distance(reversed[0], exactStart) > .01f)
                reversed.Insert(0, exactStart);
            else
                reversed[0] = exactStart;

            reversed[reversed.Count - 1] = exactGoal;
            return reversed;
        }

        private static List<Vector2> Simplify(
            IReadOnlyList<Vector2> rawPath,
            IReadOnlyList<CircularObstacle> obstacles,
            System.Func<Vector2, bool> additionalBlocked,
            float sampleSpacing)
        {
            if (rawPath.Count <= 2)
                return new List<Vector2>(rawPath);

            var simplified = new List<Vector2> { rawPath[0] };
            int anchor = 0;
            while (anchor < rawPath.Count - 1)
            {
                int furthestVisible = anchor + 1;
                for (int candidate = rawPath.Count - 1; candidate > anchor + 1; candidate--)
                {
                    if (SegmentIsClear(
                            rawPath[anchor],
                            rawPath[candidate],
                            obstacles,
                            additionalBlocked,
                            sampleSpacing))
                    {
                        furthestVisible = candidate;
                        break;
                    }
                }

                simplified.Add(rawPath[furthestVisible]);
                anchor = furthestVisible;
            }

            return simplified;
        }

        private static bool SegmentIsClear(
            Vector2 start,
            Vector2 end,
            IReadOnlyList<CircularObstacle> obstacles,
            System.Func<Vector2, bool> additionalBlocked,
            float sampleSpacing)
        {
            Vector2 segment = end - start;
            float lengthSquared = segment.sqrMagnitude;
            if (obstacles != null)
            {
                for (int i = 0; i < obstacles.Count; i++)
                {
                    CircularObstacle obstacle = obstacles[i];
                    float t = lengthSquared > .0001f
                        ? Mathf.Clamp01(Vector2.Dot(obstacle.Center - start, segment) / lengthSquared)
                        : 0f;
                    Vector2 closest = start + segment * t;
                    if ((closest - obstacle.Center).sqrMagnitude <=
                        obstacle.Radius * obstacle.Radius)
                        return false;
                }
            }

            if (additionalBlocked != null)
            {
                float length = Mathf.Sqrt(lengthSquared);
                int samples = Mathf.Max(
                    1,
                    Mathf.CeilToInt(length / Mathf.Max(.25f, sampleSpacing * .5f))
                );
                for (int i = 1; i < samples; i++)
                {
                    if (additionalBlocked(Vector2.Lerp(start, end, (float)i / samples)))
                        return false;
                }
            }

            return true;
        }

        private bool InBounds(Vector2 point) =>
            point.x >= minX && point.x <= maxX && point.y >= minY && point.y <= maxY;

        private Vector2Int ToCell(Vector2 point) => new Vector2Int(
            Mathf.Clamp(Mathf.RoundToInt((point.x - minX) / cellSize), 0, width - 1),
            Mathf.Clamp(Mathf.RoundToInt((point.y - minY) / cellSize), 0, height - 1)
        );

        private Vector2 ToWorld(Vector2Int cell) =>
            new Vector2(minX + cell.x * cellSize, minY + cell.y * cellSize);

        private int Index(Vector2Int cell) => cell.y * width + cell.x;
        private Vector2Int Cell(int index) => new Vector2Int(index % width, index / width);
        private bool ValidCell(Vector2Int cell) =>
            cell.x >= 0 && cell.x < width && cell.y >= 0 && cell.y < height;

        private static float Heuristic(Vector2Int from, Vector2Int to)
        {
            int dx = Mathf.Abs(from.x - to.x);
            int dy = Mathf.Abs(from.y - to.y);
            int diagonal = Mathf.Min(dx, dy);
            return diagonal * 1.41421356f + (Mathf.Max(dx, dy) - diagonal);
        }

        private sealed class MinHeap
        {
            private readonly List<Entry> entries = new List<Entry>();
            public int Count => entries.Count;

            public void Push(int node, float priority)
            {
                entries.Add(new Entry(node, priority));
                int index = entries.Count - 1;
                while (index > 0)
                {
                    int parent = (index - 1) / 2;
                    if (entries[parent].Priority <= entries[index].Priority)
                        break;
                    Swap(parent, index);
                    index = parent;
                }
            }

            public int Pop()
            {
                int node = entries[0].Node;
                int last = entries.Count - 1;
                entries[0] = entries[last];
                entries.RemoveAt(last);
                int index = 0;
                while (index < entries.Count)
                {
                    int left = index * 2 + 1;
                    int right = left + 1;
                    if (left >= entries.Count)
                        break;

                    int smallest = right < entries.Count &&
                                   entries[right].Priority < entries[left].Priority
                        ? right
                        : left;
                    if (entries[index].Priority <= entries[smallest].Priority)
                        break;

                    Swap(index, smallest);
                    index = smallest;
                }

                return node;
            }

            private void Swap(int a, int b)
            {
                Entry temporary = entries[a];
                entries[a] = entries[b];
                entries[b] = temporary;
            }

            private readonly struct Entry
            {
                public Entry(int node, float priority)
                {
                    Node = node;
                    Priority = priority;
                }

                public int Node { get; }
                public float Priority { get; }
            }
        }
    }
}
