using System;
using UnityEngine;

namespace UavUsv
{
    /// <summary>
    /// Pure geometry from 护航守卫5.py: blocker point on the own→threat segment,
    /// threat-facing guard arc (wings), and complementary escort-arc slots.
    /// Horizontal plane uses Unity XZ (Y ignored).
    /// </summary>
    public static class EscortGuardGeometry
    {
        const float Eps = 1e-8f;

        public static Vector3 Horizontal(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        public static Vector3 NormalizeHorizontal(Vector3 v, Vector3 fallback)
        {
            v = Horizontal(v);
            if (v.sqrMagnitude <= Eps)
                return Horizontal(fallback).sqrMagnitude > Eps
                    ? Horizontal(fallback).normalized
                    : Vector3.forward;
            return v.normalized;
        }

        public static Vector3 Rotate90(Vector3 forward)
        {
            forward = NormalizeHorizontal(forward, Vector3.forward);
            return new Vector3(-forward.z, 0f, forward.x);
        }

        /// <summary>
        /// Strict interior blocker: own + t*(enemy-own), 0 &lt; t &lt; 1.
        /// Radius is clip(ratio * distance, rMin, rMax), then clamped inside the segment.
        /// </summary>
        public static Vector3 ComputeBlockerPoint(
            Vector3 own,
            Vector3 enemy,
            float ratio,
            float rMin,
            float rMax,
            Vector3 fallbackDirection,
            out float t)
        {
            own = Horizontal(own);
            enemy = Horizontal(enemy);
            Vector3 delta = enemy - own;
            float distance = delta.magnitude;
            if (distance <= Eps)
            {
                delta = NormalizeHorizontal(fallbackDirection, Vector3.forward);
                distance = 1f;
            }

            float requested = Mathf.Clamp(ratio * distance, rMin, rMax);
            float strictLower = distance * 1e-9f;
            float strictUpper = distance * (1f - 1e-9f);
            float radius = Mathf.Clamp(requested, strictLower, strictUpper);
            t = radius / distance;
            Vector3 point = own + delta.normalized * radius;
            point.y = own.y;
            return point;
        }

        public static float EffectiveGuardArcHalfAngle(
            int wingCount,
            float configuredHalfAngleRad,
            float guardArcRadius,
            float minimumGuardSpacing)
        {
            if (wingCount <= 1)
                return 0f;
            if (minimumGuardSpacing <= Eps || guardArcRadius <= Eps)
                return configuredHalfAngleRad;

            float ratio = Mathf.Clamp01(minimumGuardSpacing / (2f * guardArcRadius));
            float minimumStep = 2f * Mathf.Asin(ratio);
            float minimumHalfAngle = .5f * (wingCount - 1) * minimumStep;
            return Mathf.Max(configuredHalfAngleRad, minimumHalfAngle);
        }

        /// <summary>
        /// Threat-facing wing slots around <paramref name="own"/> at guardArcRadius.
        /// </summary>
        public static Vector3[] WingGoals(
            Vector3 own,
            Vector3 threatDir,
            int wingCount,
            float guardArcRadius,
            float halfAngleRad,
            float height)
        {
            if (wingCount <= 0)
                return Array.Empty<Vector3>();

            threatDir = NormalizeHorizontal(threatDir, Vector3.forward);
            Vector3 lateral = Rotate90(threatDir);
            var goals = new Vector3[wingCount];
            for (int i = 0; i < wingCount; i++)
            {
                float phi = wingCount == 1
                    ? 0f
                    : Mathf.Lerp(-halfAngleRad, halfAngleRad, i / (float)(wingCount - 1));
                Vector3 offset = guardArcRadius * (Mathf.Cos(phi) * threatDir + Mathf.Sin(phi) * lateral);
                goals[i] = new Vector3(own.x + offset.x, height, own.z + offset.z);
            }

            return goals;
        }

        public static Vector3[] WingGoalsWithSpacing(
            Vector3 own,
            Vector3 threatDir,
            int wingCount,
            float guardArcRadius,
            float configuredHalfAngleRad,
            float minimumGuardSpacing,
            float height)
        {
            float half = EffectiveGuardArcHalfAngle(
                wingCount, configuredHalfAngleRad, guardArcRadius, minimumGuardSpacing);
            return WingGoals(own, threatDir, wingCount, guardArcRadius, half, height);
        }

        /// <summary>
        /// Escort-arc offsets (relative to own) outside the threat-facing sector.
        /// Angles measured from threatDir toward lateral (CCW).
        /// </summary>
        public static Vector3[] EscortGoalOffsets(
            Vector3 threatDir,
            int count,
            float ringRadius,
            float guardArcHalfAngleRad,
            float escortClearanceRad)
        {
            if (count <= 0)
                return Array.Empty<Vector3>();

            threatDir = NormalizeHorizontal(threatDir, Vector3.forward);
            Vector3 lateral = Rotate90(threatDir);
            float start = guardArcHalfAngleRad + escortClearanceRad;
            float end = 2f * Mathf.PI - guardArcHalfAngleRad - escortClearanceRad;
            var offsets = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                float phi = count == 1
                    ? .5f * (start + end)
                    : Mathf.Lerp(start, end, i / (float)(count - 1));
                offsets[i] = ringRadius * (Mathf.Cos(phi) * threatDir + Mathf.Sin(phi) * lateral);
            }

            return offsets;
        }

        /// <summary>
        /// Minimum-cost assignment of agents to goals (exhaustive, fine for ≤4).
        /// Returns agentIndex → goalIndex; -1 if unused.
        /// </summary>
        public static int[] AssignMinCost(Vector3[] agentPositions, Vector3[] goals)
        {
            int nAgents = agentPositions?.Length ?? 0;
            int nGoals = goals?.Length ?? 0;
            var result = new int[nAgents];
            for (int i = 0; i < nAgents; i++)
                result[i] = -1;
            if (nAgents == 0 || nGoals == 0)
                return result;

            int count = Mathf.Min(nAgents, nGoals);
            int[] agentOrder = new int[nAgents];
            for (int i = 0; i < nAgents; i++)
                agentOrder[i] = i;

            float bestCost = float.PositiveInfinity;
            int[] bestPick = null;
            PermutePrefix(agentOrder, 0, count, pick =>
            {
                float cost = 0f;
                for (int slot = 0; slot < count; slot++)
                {
                    Vector3 a = Horizontal(agentPositions[pick[slot]]);
                    Vector3 g = Horizontal(goals[slot]);
                    cost += Vector3.Distance(a, g);
                }

                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestPick = (int[])pick.Clone();
                }
            });

            if (bestPick != null)
            {
                for (int slot = 0; slot < count; slot++)
                    result[bestPick[slot]] = slot;
            }

            return result;
        }

        /// <summary>
        /// Escort assignment: minimize travel while keeping anchor·offset &gt; 0
        /// (no crossing through own center), matching 护航守卫5._assign_escort_slots.
        /// </summary>
        public static int[] AssignEscortSlots(
            Vector3 own,
            Vector3[] agentPositions,
            Vector3[] goalOffsets)
        {
            int nAgents = agentPositions?.Length ?? 0;
            int nGoals = goalOffsets?.Length ?? 0;
            var result = new int[nAgents];
            for (int i = 0; i < nAgents; i++)
                result[i] = -1;
            if (nAgents == 0 || nGoals == 0)
                return result;

            int count = Mathf.Min(nAgents, nGoals);
            own = Horizontal(own);
            var anchors = new Vector3[nAgents];
            for (int i = 0; i < nAgents; i++)
                anchors[i] = Horizontal(agentPositions[i] - own);

            int[] agentOrder = new int[nAgents];
            for (int i = 0; i < nAgents; i++)
                agentOrder[i] = i;

            float bestCost = float.PositiveInfinity;
            int[] bestPick = null;
            PermutePrefix(agentOrder, 0, count, pick =>
            {
                float cost = 0f;
                bool feasible = true;
                for (int slot = 0; slot < count; slot++)
                {
                    int idx = pick[slot];
                    Vector3 offset = Horizontal(goalOffsets[slot]);
                    if (Vector3.Dot(anchors[idx], offset) <= Eps)
                    {
                        feasible = false;
                        break;
                    }

                    Vector3 goal = own + offset;
                    cost += Vector3.Distance(Horizontal(agentPositions[idx]), goal);
                }

                if (feasible && cost < bestCost)
                {
                    bestCost = cost;
                    bestPick = (int[])pick.Clone();
                }
            });

            if (bestPick == null)
            {
                // Fallback with crossing penalty (same as Python).
                bestCost = float.PositiveInfinity;
                PermutePrefix(agentOrder, 0, count, pick =>
                {
                    float cost = 0f;
                    for (int slot = 0; slot < count; slot++)
                    {
                        int idx = pick[slot];
                        Vector3 offset = Horizontal(goalOffsets[slot]);
                        float penalty = Vector3.Dot(anchors[idx], offset) <= Eps ? 1e6f : 0f;
                        Vector3 goal = own + offset;
                        cost += penalty + Vector3.Distance(Horizontal(agentPositions[idx]), goal);
                    }

                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        bestPick = (int[])pick.Clone();
                    }
                });
            }

            if (bestPick != null)
            {
                for (int slot = 0; slot < count; slot++)
                    result[bestPick[slot]] = slot;
            }

            return result;
        }

        static void PermutePrefix(int[] order, int start, int take, Action<int[]> onPick)
        {
            if (start >= take)
            {
                onPick(order);
                return;
            }

            for (int i = start; i < order.Length; i++)
            {
                (order[start], order[i]) = (order[i], order[start]);
                PermutePrefix(order, start + 1, take, onPick);
                (order[start], order[i]) = (order[i], order[start]);
            }
        }
    }
}
