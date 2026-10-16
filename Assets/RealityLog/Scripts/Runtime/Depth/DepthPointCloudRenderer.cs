#nullable enable

using System.Collections.Generic;
using UnityEngine;
using Meta.XR.MRUtilityKit;
using Meta.XR;
using RealityLog.Common;

namespace RealityLog.Depth
{
    /// <summary>
    /// Live coverage overlay built on Meta's EnvironmentRaycastManager.
    ///
    /// On every capture tick a grid of rays is cast against the live depth buffer. Each hit is a depth
    /// sample that feeds a coarse world-space voxel grid (<see cref="CoverageVoxelGrid"/>). The particle
    /// system then shows one point per voxel, coloured by how well that bit of surface has been captured
    /// for Gaussian-splat reconstruction:
    ///   red   = glimpsed only (one direction, never closer than 3 m)
    ///   amber = seen, but from too few directions or only from far away
    ///   green = seen from 3+ direction bins within 2.5 m
    /// Surfaces that never produced a sample have no point at all: passthrough shows through.
    ///
    /// With <see cref="coverageMode"/> off the original behaviour is kept: one particle per sample,
    /// hue = viewing azimuth, saturation = grazing angle.
    ///
    /// REQUIREMENTS:
    /// 1. MRUK package installed (com.meta.xr.mrutilitykit)
    /// 2. Scene permission granted
    /// 3. EnvironmentRaycastManager component in scene
    /// </summary>
    public class DepthPointCloudRenderer : MonoBehaviour
    {
        [SerializeField] private int gridWidth = 16;
        [SerializeField] private int gridHeight = 12;
        [SerializeField] private float minRaycastDistance = 0.25f;
        [SerializeField] private float raycastDistance = 10f;
        [SerializeField] private bool showDebugLines = true;
        [SerializeField] private CaptureTimer captureTimer = default!;
        [SerializeField] private EnvironmentRaycastManager environmentRaycastManager = default!;
        [SerializeField] private Transform trackingSpace = default!;
        [SerializeField] private Camera camera = default!;
        [SerializeField] private ParticleSystem pointCloudParticleSystem = default!;

        [Header("Coverage heatmap")]
        [Tooltip("Show one point per voxel coloured red/amber/green by capture quality. Off = original per-sample angle colouring.")]
        [SerializeField] private bool coverageMode = true;
        [Tooltip("Edge length of a coverage voxel in metres.")]
        [SerializeField] private float voxelSize = 0.10f;
        [Tooltip("Hard cap on tracked voxels; new surfaces are ignored once reached (existing ones keep updating).")]
        [SerializeField] private int maxVoxels = 50000;
        [Tooltip("Depth hits farther than this from the headset are not accumulated (keeps the grid bounded to the walked area).")]
        [SerializeField] private float maxSampleDistance = 6f;
        [Tooltip("How often the voxel point cloud is rebuilt, in Hz.")]
        [SerializeField] private float coverageRefreshHz = 3f;
        [Tooltip("Rendered size of a voxel point in metres.")]
        [SerializeField] private float voxelPointSize = 0.025f;
        [SerializeField] private Color redColor = new Color(1.00f, 0.20f, 0.15f, 0.9f);
        [SerializeField] private Color amberColor = new Color(1.00f, 0.72f, 0.05f, 0.9f);
        [SerializeField] private Color greenColor = new Color(0.20f, 0.95f, 0.30f, 0.9f);
        [Tooltip("Dots closer than this to the headset are drawn at full opacity ...")]
        [SerializeField] private float fadeStartDistance = 2.5f;
        [Tooltip("... fading out linearly until this distance; dots beyond it are not drawn.")]
        [SerializeField] private float fadeEndDistance = 5f;

        [Header("Free-space carving")]
        [Tooltip("Drop voxels that depth rays have repeatedly passed through (floating noise samples).")]
        [SerializeField] private bool carveFloaters = true;
        [Tooltip("Capture ticks that must have seen through a voxel before it can be dropped.")]
        [SerializeField] private int carveMinFrames = 2;
        [Tooltip("Voxels with fewer samples than this count as weakly supported and are dropped after carveMinFrames.")]
        [SerializeField] private int carveWeakSupportSamples = 5;
        [Tooltip("A well-supported voxel is dropped only when free observations reach this multiple of its samples (0 = never).")]
        [SerializeField] private int carveStrongRatio = 3;
        [Tooltip("Depth must be farther than the voxel by this many metres plus the fraction below to count as seen through.")]
        [SerializeField] private float carveMarginMeters = 0.15f;
        [SerializeField] private float carveMarginFraction = 0.02f;

        private int hitCount = 0;
        private int totalRaycastCount = 0;

        private CoverageVoxelGrid? coverage;
        private ParticleSystem.Particle[]? particleBuffer;
        private float lastCoverageRefreshTime = -1f;
        private int renderedCoverageCount = 0;
        private int lastPrunedCount = 0;

        /// <summary>The voxel grid behind the overlay (null until Start). Read-only for HUDs.</summary>
        public CoverageVoxelGrid? Coverage => coverage;
        public bool CoverageMode => coverageMode;
        public int LastHitCount => hitCount;
        /// <summary>Voxels removed by free-space carving on the last capture tick.</summary>
        public int LastPrunedCount => lastPrunedCount;

        private void Start()
        {
            Debug.Log($"[{Constants.LOG_TAG}] DepthPointCloudRenderer - Started at {captureTimer.TargetCaptureFPS} FPS, coverage mode {coverageMode}");

            coverage = new CoverageVoxelGrid(voxelSize, maxVoxels)
            {
                FreeMinObservations = carveMinFrames,
                WeakSupportSamples = carveWeakSupportSamples,
                StrongCarveRatio = carveStrongRatio,
                FreeMarginMeters = carveMarginMeters,
                FreeMarginFraction = carveMarginFraction,
            };

            if (pointCloudParticleSystem != null)
            {
                var main = pointCloudParticleSystem.main;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startSpeed = 0.00001f; // Small non-zero value to prevent culling
                main.startLifetime = 1000f; // Long lifetime
                main.maxParticles = Mathf.Max(100000, maxVoxels);
            }
            else
            {
                Debug.LogError($"[{Constants.LOG_TAG}] DepthPointCloudRenderer - ParticleSystem not assigned!");
            }
        }

        public void ClearPointCloud()
        {
            coverage?.Clear();
            if (pointCloudParticleSystem != null)
            {
                pointCloudParticleSystem.Clear();
            }
        }

        private void Update()
        {
            if (!showDebugLines || environmentRaycastManager == null) return;

            if (captureTimer.IsCapturing && captureTimer.ShouldCaptureThisFrame)
            {
                CastSampleGrid();
            }

            // The distance fade follows the head, so the dots are refreshed whether or not we are recording.
            if (coverageMode)
            {
                RefreshCoverageParticlesIfDue();
            }
        }

        private void CastSampleGrid()
        {
            // Cast grid of rays from the camera
            if (camera == null)
            {
                Debug.LogWarning($"[{Constants.LOG_TAG}] DepthPointCloudRenderer - No camera found!");
                return;
            }

            hitCount = 0;
            totalRaycastCount = 0;
            Vector3 cameraPos = camera.transform.position;
            coverage?.BeginTick();

            for (int y = 0; y < gridHeight; y++)
            {
                for (int x = 0; x < gridWidth; x++)
                {
                    // Generate grid position in viewport space (0 to 1)
                    float u = (x + 0.5f) / gridWidth;
                    float v = (y + 0.5f) / gridHeight;

                    // Convert to world space ray
                    Ray ray = camera.ViewportPointToRay(new Vector3(u, v, 0f));

                    totalRaycastCount++;

                    // Raycast against live depth buffer
                    // TODO: Since we already have the depth buffer, we could use it directly instead of raycasting
                    if (!environmentRaycastManager.Raycast(ray, out EnvironmentRaycastHit hit, raycastDistance))
                    {
                        continue;
                    }

                    float distance = Vector3.Distance(cameraPos, hit.point);

                    // Filter out hits that are too close (likely invalid depth or near plane)
                    if (distance < minRaycastDistance)
                    {
                        continue;
                    }

                    hitCount++;

                    if (coverageMode && coverage != null)
                    {
                        if (distance <= maxSampleDistance)
                        {
                            coverage.AddSample(hit.point, cameraPos);
                        }
                        if (carveFloaters)
                        {
                            // Everything this ray crossed before the hit is free space: evidence against floaters there.
                            float hitAlongRay = Vector3.Distance(ray.origin, hit.point);
                            coverage.ObserveFreeRay(ray.origin, ray.direction, hitAlongRay, minRaycastDistance);
                        }
                        continue;
                    }

                    // Legacy mode: one particle per sample, coloured by viewing angle
                    Color pointColor = GetColorFromSurfaceNormal(hit.point, cameraPos, hit.normal);
                    var emitParams = new ParticleSystem.EmitParams();
                    emitParams.position = hit.point;
                    emitParams.startColor = pointColor;
                    emitParams.startSize = 0.01f;
                    pointCloudParticleSystem.Emit(emitParams, 1);
                }
            }

            if (coverageMode && carveFloaters && coverage != null)
            {
                lastPrunedCount = coverage.Prune();
            }
        }

        private void RefreshCoverageParticlesIfDue()
        {
            if (coverage == null || pointCloudParticleSystem == null || camera == null) return;

            float interval = coverageRefreshHz > 0f ? 1f / coverageRefreshHz : 0f;
            if (Time.unscaledTime - lastCoverageRefreshTime < interval) return;
            lastCoverageRefreshTime = Time.unscaledTime;

            var voxels = coverage.Voxels;
            int count = voxels.Count;
            if (count == 0)
            {
                if (renderedCoverageCount > 0) pointCloudParticleSystem.Clear();
                renderedCoverageCount = 0;
                return;
            }

            if (particleBuffer == null || particleBuffer.Length < count)
            {
                particleBuffer = new ParticleSystem.Particle[Mathf.Min(coverage.MaxVoxels, Mathf.Max(count, 1024) * 2)];
            }

            Vector3 headPos = camera.transform.position;
            float fadeStart = Mathf.Max(0f, fadeStartDistance);
            float fadeEnd = Mathf.Max(fadeStart + 0.01f, fadeEndDistance);
            float fadeEndSq = fadeEnd * fadeEnd;
            float fadeRange = fadeEnd - fadeStart;

            int n = 0;
            for (int i = 0; i < count; i++)
            {
                var v = voxels[i];
                Vector3 pos = coverage.CenterOf(v.Cell);
                float distSq = (pos - headPos).sqrMagnitude;
                if (distSq >= fadeEndSq) continue; // beyond the fade: not drawn at all

                float fade = 1f;
                if (distSq > fadeStart * fadeStart)
                {
                    fade = 1f - (Mathf.Sqrt(distSq) - fadeStart) / fadeRange;
                }

                Color c = coverage.StateOf(v) switch
                {
                    CoverageState.Green => greenColor,
                    CoverageState.Amber => amberColor,
                    _ => redColor,
                };
                c.a *= fade;

                ref var p = ref particleBuffer[n++];
                p.position = pos;
                p.velocity = Vector3.zero;
                p.startSize = voxelPointSize;
                p.startLifetime = 1000f;
                p.remainingLifetime = 1000f;
                p.rotation = 0f;
                p.startColor = c;
            }

            pointCloudParticleSystem.SetParticles(particleBuffer, n);
            renderedCoverageCount = n;
        }

        private Color GetColorFromSurfaceNormal(Vector3 hitPoint, Vector3 cameraPos, Vector3 surfaceNormal)
        {
            // Calculate view direction (from surface to camera)
            Vector3 viewDir = (cameraPos - hitPoint).normalized;

            // Pick a stable reference direction perpendicular to the normal
            // Use world-up cross normal, fallback to world-forward if normal is vertical
            Vector3 referenceDir = Vector3.Cross(surfaceNormal, Vector3.up);
            if (referenceDir.sqrMagnitude < 0.01f)
            {
                referenceDir = Vector3.Cross(surfaceNormal, Vector3.forward);
            }

            // HUE: Azimuth (rotation around the normal) - shows DIRECTION
            float angle = Vector3.SignedAngle(referenceDir, viewDir, surfaceNormal);
            if (angle < 0f) angle += 360f;
            float hue = angle / 360f;

            // SATURATION: 0.0 = head-on (white), 1.0 = grazing (vivid)
            float dotProduct = Mathf.Abs(Vector3.Dot(viewDir, surfaceNormal));
            float saturation = 1.0f - dotProduct;

            return Color.HSVToRGB(hue, saturation, 1.0f);
        }
    }
}
