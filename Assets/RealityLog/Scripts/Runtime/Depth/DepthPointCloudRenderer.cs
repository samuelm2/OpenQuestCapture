#nullable enable

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
        [SerializeField] private float raycastDistance = 10f;
        [SerializeField] private float pointSize = 0.02f;
        [SerializeField] private Color pointColor = Color.green;
        [SerializeField] private bool showDebugLines = true;
        [SerializeField] private CaptureTimer captureTimer;
        [SerializeField] private EnvironmentRaycastManager environmentRaycastManager;

        private int hitCount = 0;
        private int totalRaycastCount = 0;

        private void Start()
        {
            Debug.Log($"[{Constants.LOG_TAG}] DepthPointCloudRenderer - Started at {captureTimer.TargetCaptureFPS} FPS");
        }

        private void Update()
        {
            if (!showDebugLines || environmentRaycastManager == null || !captureTimer.IsCapturing || !captureTimer.ShouldCaptureThisFrame) return;

            // Cast grid of rays from the camera
            Camera cam = Camera.main;
            if (cam == null)
            {
                Debug.LogWarning($"[{Constants.LOG_TAG}] DepthPointCloudRenderer - No main camera found!");
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
                    Ray ray = cam.ViewportPointToRay(new Vector3(u, v, 0f));

                    totalRaycastCount++;
                    
                    // Raycast against live depth buffer
                    if (environmentRaycastManager.Raycast(ray, out EnvironmentRaycastHit hit, raycastDistance))
                    {
                        hitCount++;
                        
                        // Draw a tiny sphere gameobject at the hit point
                        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                        sphere.transform.position = hit.point;
                        sphere.transform.localScale = new Vector3(pointSize, pointSize, pointSize);
                        sphere.GetComponent<MeshRenderer>().material.color = pointColor;
                    }
                }
            }
        }
    }
}
