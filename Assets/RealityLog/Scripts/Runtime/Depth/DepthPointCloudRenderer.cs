#nullable enable

using System.Collections.Generic;
using UnityEngine;
using Meta.XR.MRUtilityKit;
using Meta.XR;
using RealityLog.Common;

namespace RealityLog.Depth
{
    /// <summary>
    /// Point cloud visualizer using Meta's EnvironmentRaycastManager for live depth raycasting.
    /// 
    /// REQUIREMENTS:
    /// 1. MRUK package installed (com.meta.xr.mrutilitykit)
    /// 2. Scene permission granted
    /// 3. EnvironmentRaycastManager component in scene
    /// 
    /// Raycasts directly against the live depth buffer for real-time accuracy.
    /// </summary>
    public class DepthPointCloudRenderer : MonoBehaviour
    {
        [SerializeField] private int gridWidth = 8;
        [SerializeField] private int gridHeight = 6;
        [SerializeField] private float minRaycastDistance = 0.25f;
        [SerializeField] private float raycastDistance = 10f;
        [SerializeField] private bool showDebugLines = true;
        [SerializeField] private CaptureTimer captureTimer;
        [SerializeField] private EnvironmentRaycastManager environmentRaycastManager;
        [SerializeField] private Transform trackingSpace;
        [SerializeField] private Camera camera;
        [SerializeField] private GameObject pointPrefab;

        private int hitCount = 0;
        private int totalRaycastCount = 0;
        private List<GameObject> spawnedSpheres = new List<GameObject>();
        
        private void Start()
        {
            Debug.Log($"[{Constants.LOG_TAG}] DepthPointCloudRenderer - Started at {captureTimer.TargetCaptureFPS} FPS");
        }

        private void ClearAllSpheres()
        {
            Debug.Log($"[{Constants.LOG_TAG}] DepthPointCloudRenderer - Clearing {spawnedSpheres.Count} spheres");
            foreach (GameObject sphere in spawnedSpheres)
            {
                if (sphere != null)
                {
                    Destroy(sphere);
                }
            }
            spawnedSpheres.Clear();
        }



        private void Update()
        {
            if (!showDebugLines || environmentRaycastManager == null || !captureTimer.IsCapturing || !captureTimer.ShouldCaptureThisFrame) return;

            // Cast grid of rays from the camera
            if (camera == null)
            {
                Debug.LogWarning($"[{Constants.LOG_TAG}] DepthPointCloudRenderer - No camera found!");
                return;
            }

            hitCount = 0;
            totalRaycastCount = 0;

            Debug.Log($"[{Constants.LOG_TAG}] DepthPointCloudRenderer - Casting {gridWidth * gridHeight} rays...");

            
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
                    if (environmentRaycastManager.Raycast(ray, out EnvironmentRaycastHit hit, raycastDistance))
                    {
                        float distance = Vector3.Distance(camera.transform.position, hit.point);
                        
                        // Filter out hits that are too close (likely invalid depth or near plane)
                        if (distance < minRaycastDistance)
                        {
                            continue;
                        }

                        hitCount++;

                        if (showDebugLines && x == gridWidth / 2 && y == gridHeight / 2)
                        {
                            Debug.Log($"[{Constants.LOG_TAG}] Depth Hit - Dist: {distance:F2}m, Pos: {hit.point}");
                            Debug.Log($"[{Constants.LOG_TAG}] Camera Pos: {camera.transform.position}");
                        }
                        Color pointColor = GetColorFromSurfaceNormal(hit.point, camera.transform.position, hit.normal);
                        // Instantiate point prefab at hit point  
                        GameObject point = Instantiate(pointPrefab, hit.point, Quaternion.identity);
                        point.transform.parent = trackingSpace;
                        point.GetComponent<MeshRenderer>().material.color = pointColor;
                        
                        spawnedSpheres.Add(point);
                        
                    }
                }
            }
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
            
            // VALUE: Viewing angle quality - shows QUALITY
            // Dot product: 1.0 = head-on (perpendicular), 0.0 = grazing (parallel)
            float dotProduct = Mathf.Abs(Vector3.Dot(viewDir, surfaceNormal));
            
            // Invert so grazing = bright, head-on = dark
            float value = 1.0f - dotProduct;
            value = Mathf.Lerp(0.3f, 1.0f, value); // Remap: grazing=1.0 (bright), head-on=0.3 (dim)
            
            float saturation = 1.0f; // Full saturation for vivid colors
            
            // Result: 
            // - Rainbow HUE shows viewing DIRECTION around surface
            // - BRIGHTNESS shows viewing QUALITY (bright=grazing/good, dim=head-on/okay)
            return Color.HSVToRGB(hue, saturation, value);
        }
    }
}
