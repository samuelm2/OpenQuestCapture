#nullable enable

using System.Collections.Generic;
using UnityEngine;

namespace RealityLog.Depth
{
    /// <summary>Coverage quality of one voxel, from the point of view of Gaussian-splat reconstruction.</summary>
    public enum CoverageState : byte
    {
        /// <summary>Only glimpsed: a single viewing direction and never closer than the amber distance.</summary>
        Red = 0,
        /// <summary>Seen, but from too few directions or only from far away.</summary>
        Amber = 1,
        /// <summary>Seen from enough distinct directions at close range.</summary>
        Green = 2,
    }

    public struct CoverageVoxel
    {
        public Vector3Int Cell;
        /// <summary>Number of depth samples that landed in this voxel (saturates).</summary>
        public ushort SampleCount;
        /// <summary>Distinct viewing-direction bins that have seen this voxel (one bit per bin).</summary>
        public ulong DirectionBits;
        /// <summary>Number of set bits in <see cref="DirectionBits"/>.</summary>
        public byte DirectionCount;
        /// <summary>Closest distance (metres) from which the voxel has been seen.</summary>
        public float MinDistance;
        /// <summary>Number of capture ticks in which a depth ray passed through this voxel and hit something behind it.</summary>
        public byte FreeCount;
        /// <summary>Tick in which the last free-space observation was recorded (one observation per tick at most).</summary>
        public int LastFreeTick;
    }

    /// <summary>
    /// Coarse world-space voxel grid that accumulates, per cell, how many depth samples hit it,
    /// from how many distinct viewing directions (12 azimuth x 3 elevation bins) and from how close.
    /// Plain C# so it can be unit tested without a scene.
    /// </summary>
    public sealed class CoverageVoxelGrid
    {
        public const int AzimuthBins = 12;
        public const int ElevationBins = 3;
        public const int DirectionBinCount = AzimuthBins * ElevationBins;
        private const float ElevationSplitDegrees = 20f;

        public float VoxelSize { get; }
        public int MaxVoxels { get; }

        /// <summary>A voxel is green when seen from at least this many direction bins ...</summary>
        public int GreenMinDirections = 3;
        /// <summary>... and from at most this distance (metres).</summary>
        public float GreenMaxDistance = 2.5f;
        /// <summary>A voxel seen from a single direction and never closer than this is red.</summary>
        public float AmberMaxDistance = 3.0f;

        /// <summary>A voxel is dropped once this many ticks have seen through it ...</summary>
        public int FreeMinObservations = 2;
        /// <summary>... while it has fewer samples than this (weak support).</summary>
        public int WeakSupportSamples = 5;
        /// <summary>A well-supported voxel is still dropped when free observations reach this multiple of its samples (0 = never).</summary>
        public int StrongCarveRatio = 3;
        /// <summary>Depth must be farther than the voxel by this margin (metres) plus the fraction below to count as seen through.</summary>
        public float FreeMarginMeters = 0.15f;
        public float FreeMarginFraction = 0.02f;

        private int tick;
        private readonly List<Vector3Int> pruneScratch = new();

        private readonly Dictionary<Vector3Int, int> index = new();
        private readonly List<CoverageVoxel> voxels = new();

        /// <summary>Incremented on every change, so renderers can skip rebuilding when nothing moved.</summary>
        public int Version { get; private set; }

        public int Count => voxels.Count;
        public bool IsFull => voxels.Count >= MaxVoxels;
        public IReadOnlyList<CoverageVoxel> Voxels => voxels;

        public CoverageVoxelGrid(float voxelSize, int maxVoxels)
        {
            VoxelSize = Mathf.Max(0.01f, voxelSize);
            MaxVoxels = Mathf.Max(1, maxVoxels);
        }

        public void Clear()
        {
            index.Clear();
            voxels.Clear();
            Version++;
        }

        public Vector3Int CellOf(Vector3 worldPoint)
        {
            return new Vector3Int(
                Mathf.FloorToInt(worldPoint.x / VoxelSize),
                Mathf.FloorToInt(worldPoint.y / VoxelSize),
                Mathf.FloorToInt(worldPoint.z / VoxelSize));
        }

        public Vector3 CenterOf(Vector3Int cell)
        {
            return new Vector3((cell.x + 0.5f) * VoxelSize, (cell.y + 0.5f) * VoxelSize, (cell.z + 0.5f) * VoxelSize);
        }

        /// <summary>
        /// Bin index (0..35) of the direction from a surface point towards the viewer.
        /// 12 azimuth sectors of 30 degrees around world up, 3 elevation bands (below -20, level, above +20 degrees).
        /// </summary>
        public static int DirectionBin(Vector3 surfaceToViewer)
        {
            var d = surfaceToViewer.normalized;
            float azimuth = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            if (azimuth < 0f) azimuth += 360f;
            int a = Mathf.Clamp((int)(azimuth / (360f / AzimuthBins)), 0, AzimuthBins - 1);
            float elevation = Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) * Mathf.Rad2Deg;
            int e = elevation < -ElevationSplitDegrees ? 0 : (elevation > ElevationSplitDegrees ? 2 : 1);
            return e * AzimuthBins + a;
        }

        /// <summary>
        /// Records one depth sample. Returns false when the sample was dropped (degenerate, or the grid is full
        /// and the sample would create a new voxel).
        /// </summary>
        public bool AddSample(Vector3 hitPoint, Vector3 viewerPosition)
        {
            var toViewer = viewerPosition - hitPoint;
            float distance = toViewer.magnitude;
            if (distance < 1e-4f) return false;

            var cell = CellOf(hitPoint);
            if (!index.TryGetValue(cell, out int i))
            {
                if (IsFull) return false;
                i = voxels.Count;
                index[cell] = i;
                voxels.Add(new CoverageVoxel { Cell = cell, MinDistance = float.MaxValue });
            }

            var v = voxels[i];
            if (v.SampleCount < ushort.MaxValue) v.SampleCount++;
            ulong mask = 1UL << DirectionBin(toViewer);
            if ((v.DirectionBits & mask) == 0)
            {
                v.DirectionBits |= mask;
                v.DirectionCount++;
            }
            if (distance < v.MinDistance) v.MinDistance = distance;
            voxels[i] = v;
            Version++;
            return true;
        }

        /// <summary>Starts a new capture tick; free-space observations are counted at most once per voxel per tick.</summary>
        public void BeginTick()
        {
            tick++;
        }

        /// <summary>
        /// Records that a depth ray from <paramref name="origin"/> along <paramref name="direction"/> hit a surface at
        /// <paramref name="hitDistance"/>: every existing voxel the ray passes through before the hit (minus a margin
        /// that grows with distance, to absorb depth noise) was seen through, i.e. is probably a floating sample.
        /// Walking the ray in half-voxel steps is the same test as projecting each voxel into the depth image and
        /// comparing depths, restricted to the pixels we actually sampled.
        /// </summary>
        public int ObserveFreeRay(Vector3 origin, Vector3 direction, float hitDistance, float startDistance)
        {
            float end = hitDistance - (FreeMarginMeters + FreeMarginFraction * hitDistance);
            if (end <= startDistance) return 0;

            float step = VoxelSize * 0.5f;
            var last = new Vector3Int(int.MinValue, int.MinValue, int.MinValue);
            int observed = 0;
            for (float t = startDistance; t < end; t += step)
            {
                var cell = CellOf(origin + direction * t);
                if (cell == last) continue;
                last = cell;
                if (!index.TryGetValue(cell, out int i)) continue;

                var v = voxels[i];
                if (v.LastFreeTick == tick) continue;
                v.LastFreeTick = tick;
                if (v.FreeCount < byte.MaxValue) v.FreeCount++;
                voxels[i] = v;
                observed++;
            }
            return observed;
        }

        private bool ShouldPrune(in CoverageVoxel v)
        {
            if (v.FreeCount < FreeMinObservations) return false;
            if (v.SampleCount < WeakSupportSamples) return true;
            return StrongCarveRatio > 0 && v.FreeCount >= v.SampleCount * StrongCarveRatio;
        }

        /// <summary>Removes voxels that have been seen through often enough for their support. Returns the number removed.</summary>
        public int Prune()
        {
            pruneScratch.Clear();
            for (int i = 0; i < voxels.Count; i++)
            {
                if (ShouldPrune(voxels[i])) pruneScratch.Add(voxels[i].Cell);
            }
            for (int k = 0; k < pruneScratch.Count; k++)
            {
                Remove(pruneScratch[k]);
            }
            if (pruneScratch.Count > 0) Version++;
            return pruneScratch.Count;
        }

        private void Remove(Vector3Int cell)
        {
            if (!index.TryGetValue(cell, out int i)) return;
            int lastIdx = voxels.Count - 1;
            if (i != lastIdx)
            {
                var moved = voxels[lastIdx];
                voxels[i] = moved;
                index[moved.Cell] = i;
            }
            voxels.RemoveAt(lastIdx);
            index.Remove(cell);
        }

        public CoverageState StateOf(in CoverageVoxel v)
        {
            if (v.DirectionCount >= GreenMinDirections && v.MinDistance <= GreenMaxDistance) return CoverageState.Green;
            if (v.DirectionCount <= 1 && v.MinDistance > AmberMaxDistance) return CoverageState.Red;
            return CoverageState.Amber;
        }

        public void CountStates(out int red, out int amber, out int green)
        {
            red = amber = green = 0;
            for (int i = 0; i < voxels.Count; i++)
            {
                switch (StateOf(voxels[i]))
                {
                    case CoverageState.Green: green++; break;
                    case CoverageState.Amber: amber++; break;
                    default: red++; break;
                }
            }
        }
    }
}
