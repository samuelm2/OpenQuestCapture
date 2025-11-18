# nullable enable

using UnityEngine;
using UnityEngine.XR;
using RealityLog.Common;
using RealityLog.Depth;

namespace RealityLog.Visualization
{
    /// <summary>
    /// Visualizes scan coverage by emitting colored line particles at depth sample points.
    /// Each line points toward the camera position and is colored based on viewing angle.
    /// </summary>
    public class CoverageLineVisualizer : MonoBehaviour
    {
        [Header("Dependencies")]
        [Tooltip("Required: Reference to DepthMapExporter to subscribe to depth frame events")]
        [SerializeField] private DepthMapExporter depthMapExporter = default!;
        
        [Header("Particle System")]
        [SerializeField] private ParticleSystem coverageParticleSystem = default!;
        
        [Header("Line Settings")]
        [Tooltip("Length of each coverage line in meters")]
        [SerializeField] private float lineLength = 0.05f; // 5cm
        
        [Tooltip("How many depth pixels to skip (higher = better performance, fewer lines)")]
        [SerializeField] private int downsampleFactor = 16; // Sample every 16th pixel
        
        [Tooltip("Lifetime of coverage lines in seconds")]
        [SerializeField] private float lineLifetime = 30f;
        
        [Tooltip("Enable/disable coverage visualization")]
        [SerializeField] private bool isEnabled = true;
        
        [Header("Color Settings")]
        [Tooltip("Saturation of direction colors (0-1)")]
        [SerializeField] private float colorSaturation = 0.9f;
        
        [Tooltip("Brightness of direction colors (0-1)")]
        [SerializeField] private float colorBrightness = 1f;
        
        [Header("Coordinate System")]
        [Tooltip("Optional: Tracking space transform to apply to depth camera poses (similar to PoseLogger)")]
        [SerializeField] private Transform? trackingSpace = null;
        
        private ParticleSystem.EmitParams emitParams = new();
        private ParticleSystemRenderer? particleRenderer;
        private bool isInitialized = false;
        private OVRCameraRig? ovrCameraRig;
        
        public bool IsEnabled
        {
            get => isEnabled;
            set
            {
                isEnabled = value;
                if (particleRenderer != null)
                {
                    particleRenderer.enabled = isEnabled;
                }
            }
        }
        
        private void OnEnable()
        {
            // Subscribe to depth data ready event
            if (depthMapExporter != null)
            {
                depthMapExporter.OnDepthDataReady += OnDepthDataReady;
            }
        }
        
        private void OnDisable()
        {
            // Unsubscribe from depth data ready event
            if (depthMapExporter != null)
            {
                depthMapExporter.OnDepthDataReady -= OnDepthDataReady;
            }
        }
        
        private void Start()
        {
            Initialize();
        }
        
        private void Initialize()
        {
            if (isInitialized) return;
            
            if (coverageParticleSystem == null)
            {
                Debug.LogError($"[{Constants.LOG_TAG}] CoverageLineVisualizer: ParticleSystem not assigned!");
                return;
            }
            
            // Verify particle system is configured correctly
            particleRenderer = coverageParticleSystem.GetComponent<ParticleSystemRenderer>();
            if (particleRenderer == null)
            {
                Debug.LogError($"[{Constants.LOG_TAG}] CoverageLineVisualizer: ParticleSystemRenderer not found!");
                return;
            }
            
            // Verify material is assigned
            if (particleRenderer.material == null)
            {
                Debug.LogWarning($"[{Constants.LOG_TAG}] CoverageLineVisualizer: No material assigned! Particles may not render.");
            }
            else
            {
                Debug.Log($"[{Constants.LOG_TAG}] CoverageLineVisualizer: Using material '{particleRenderer.material.name}' with shader '{particleRenderer.material.shader.name}'");
            }
            
            // Update lifetime from inspector setting
            var main = coverageParticleSystem.main;
            main.startLifetime = lineLifetime;
            
            // CRITICAL: Ensure simulation space is World, not Local
            if (main.simulationSpace != ParticleSystemSimulationSpace.World)
            {
                Debug.LogWarning($"[{Constants.LOG_TAG}] CoverageLineVisualizer: Particle simulation space is {main.simulationSpace}, changing to World!");
                main.simulationSpace = ParticleSystemSimulationSpace.World;
            }
            
            // Update stretch scale from inspector setting  
            particleRenderer.lengthScale = lineLength * 20f;
            
            // Find OVRCameraRig for accurate camera position tracking
            ovrCameraRig = FindObjectOfType<OVRCameraRig>();
            if (ovrCameraRig == null)
            {
                Debug.LogWarning($"[{Constants.LOG_TAG}] OVRCameraRig not found in scene. Camera position may be incorrect.");
            }
            else
            {
                Debug.Log($"[{Constants.LOG_TAG}] Found OVRCameraRig, will use centerEyeAnchor for camera position.");
            }
            
            // Start the particle system
            if (!coverageParticleSystem.isPlaying)
            {
                coverageParticleSystem.Play();
            }
            
            isInitialized = true;
            Debug.Log($"[{Constants.LOG_TAG}] CoverageLineVisualizer initialized successfully");
            
            // Delay test particle emission to allow VR tracking to initialize
            StartCoroutine(EmitTestParticlesDelayed());
        }
        
        private System.Collections.IEnumerator EmitTestParticlesDelayed()
        {
            // Wait for VR tracking to initialize (usually takes 1-2 frames)
            yield return new WaitForSeconds(0.5f);
            
            // Verify tracking is ready
            if (ovrCameraRig != null && ovrCameraRig.centerEyeAnchor != null)
            {
                Vector3 pos = ovrCameraRig.centerEyeAnchor.position;
                if (pos.magnitude < 0.01f)
                {
                    Debug.LogWarning($"[{Constants.LOG_TAG}] Tracking still at origin after delay, waiting longer...");
                    yield return new WaitForSeconds(1.0f);
                }
            }
            
            EmitTestParticles();
        }
        
        /// <summary>
        /// Emits test particles in a pattern to verify the system works.
        /// Remove this after confirming particles render correctly!
        /// </summary>
        private void EmitTestParticles()
        {
            Debug.Log($"[{Constants.LOG_TAG}] Emitting test particles...");
            Debug.Log($"[{Constants.LOG_TAG}] Particle system alive count BEFORE: {coverageParticleSystem.particleCount}");
            
            // Get camera position from OVRCameraRig if available
            Vector3 cameraWorldPos;
            Vector3 cameraForward;
            
            if (ovrCameraRig != null && ovrCameraRig.centerEyeAnchor != null)
            {
                cameraWorldPos = ovrCameraRig.centerEyeAnchor.position;
                cameraForward = ovrCameraRig.centerEyeAnchor.forward;
                Debug.Log($"[{Constants.LOG_TAG}] Using OVRCameraRig.centerEyeAnchor position");
            }
            else
            {
                Debug.LogError($"[{Constants.LOG_TAG}] No OVRCameraRig available!");
                return;
            }
            
            Vector3 centerPos = cameraWorldPos + cameraForward * 2f; // 2m in front
            
            Debug.Log($"[{Constants.LOG_TAG}] TEST PARTICLES DEBUG:");
            Debug.Log($"[{Constants.LOG_TAG}]   Camera world position = {cameraWorldPos}");
            Debug.Log($"[{Constants.LOG_TAG}]   Camera forward = {cameraForward}");
            Debug.Log($"[{Constants.LOG_TAG}]   Camera rotation (euler) = {ovrCameraRig.centerEyeAnchor.rotation.eulerAngles}");
            Debug.Log($"[{Constants.LOG_TAG}]   Center position (2m in front) = {centerPos}");
            
            for (int i = 0; i < 12; i++)
            {
                float angle = i * 30f * Mathf.Deg2Rad; // 30 degree intervals
                Vector3 offset = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * 0.5f;
                Vector3 worldPos = centerPos + offset;
                
                // Different color per particle
                Color color = Color.HSVToRGB(i / 12f, 1f, 1f);
                Vector3 direction = (cameraWorldPos - worldPos).normalized;
                
                Debug.Log($"[{Constants.LOG_TAG}] Emitting particle {i} at {worldPos} with color {color}");
                EmitCoverageLine(worldPos, direction, color);
            }
            
            Debug.Log($"[{Constants.LOG_TAG}] Particle system alive count AFTER: {coverageParticleSystem.particleCount}");
            Debug.Log($"[{Constants.LOG_TAG}] Test particles emitted!");
        }
        
        /// <summary>
        /// Converts reversed logarithmic NDC depth to linear depth in meters.
        /// For infinite far plane: linear_depth = (-2 * near) / (ndc - 1)
        /// </summary>
        private float ConvertNDCToLinear(float depthNDC, float nearZ)
        {
            float ndc = depthNDC * 2.0f - 1.0f;
            float denom = ndc + (-1.0f);
            if (Mathf.Abs(denom) < 0.0001f) return float.PositiveInfinity;
            return (-2.0f * nearZ) / denom;
        }
        
        /// <summary>
        /// Structure to hold local position data before world space transformation.
        /// </summary>
        private struct LocalDepthSample
        {
            public Vector3 localPos;
            public float depth;
            public int x;
            public int y;
        }
        
        /// <summary>
        /// Event handler called when depth data is ready from AsyncGPUReadback.
        /// Processes the depth data in two passes:
        /// 1. First pass: Unproject to local depth camera space and collect samples
        /// 2. Second pass: Transform to world space and emit particles
        /// </summary>
        /// <param name="depthData">Depth values in reversed logarithmic NDC format (row-major order)</param>
        /// <param name="width">Width of the depth image for this eye (eyeWidth, not full width)</param>
        /// <param name="height">Height of the depth image for this eye</param>
        /// <param name="eyeIndex">Eye index (0=left, 1=right)</param>
        /// <param name="frameDesc">Frame descriptor for this eye (passed through AsyncGPUReadback)</param>
        /// <remarks>
        /// The compute shader writes with stride = width (which is already eyeWidth).
        /// Each slice of the Texture2DArray is width x height, so the buffer stride is just width.
        /// </remarks>
        private void OnDepthDataReady(Unity.Collections.NativeArray<float> depthData, int width, int height, int eyeIndex, DepthFrameDesc frameDesc)
        {
            if (!isEnabled || !isInitialized) return;
            
            // Use the depth camera's actual position at capture time (from frame descriptor)
            // This is more accurate than pendingCameraPos which is the Unity camera from 1-2 frames ago
            Vector3 cameraPos = ovrCameraRig.centerEyeAnchor.position;
            Vector3 frameDescCameraPos = frameDesc.createPoseLocation;
            Debug.Log($"[{Constants.LOG_TAG}] Frame desc camera pos: {frameDescCameraPos}");
            Debug.Log($"[{Constants.LOG_TAG}] Camera pos: {cameraPos}");
            Debug.Log($"[{Constants.LOG_TAG}] Frame desc rotation: {frameDesc.createPoseRotation.eulerAngles}");
            Debug.Log($"[{Constants.LOG_TAG}] Camera rotation: {ovrCameraRig.centerEyeAnchor.rotation.eulerAngles}");
            
            // Sample depth at different locations to check for variation
            int centerIdx = (height/2) * width + (width/2);
            int topLeftIdx = 0;
            int bottomRightIdx = (height - 1) * width + (width - 1);
            
            // Find min/max manually (avoid LINQ overhead) - convert from NDC to linear
            float minDepthLinear = float.MaxValue;
            float maxDepthLinear = float.MinValue;
            for (int i = 0; i < depthData.Length; i++)
            {
                float depthNDC = depthData[i];
                float ndc = depthNDC * 2.0f - 1.0f;
                float denom = ndc + (-1.0f);
                if (Mathf.Abs(denom) < 0.0001f) continue;
                float linearDepth = (-2.0f * frameDesc.nearZ) / denom;
                
                if (linearDepth < minDepthLinear) minDepthLinear = linearDepth;
                if (linearDepth > maxDepthLinear) maxDepthLinear = linearDepth;
            }
            
            // Convert sample points to linear for logging
            float centerDepthLinear = ConvertNDCToLinear(depthData[centerIdx], frameDesc.nearZ);
            float topLeftDepthLinear = ConvertNDCToLinear(depthData[topLeftIdx], frameDesc.nearZ);
            float bottomRightDepthLinear = ConvertNDCToLinear(depthData[bottomRightIdx], frameDesc.nearZ);
            
            Debug.Log($"[{Constants.LOG_TAG}] Depth samples (LINEAR) - Center: {centerDepthLinear:F3}m, TopLeft: {topLeftDepthLinear:F3}m, BottomRight: {bottomRightDepthLinear:F3}m, Min: {minDepthLinear:F3}m, Max: {maxDepthLinear:F3}m");
            Debug.Log($"[{Constants.LOG_TAG}] Near/Far planes: nearZ={frameDesc.nearZ:F3}m, farZ={frameDesc.farZ:F3}m");
            
            // ===== PASS 1: Unproject to local depth camera space =====
            System.Collections.Generic.List<LocalDepthSample> localSamples = new System.Collections.Generic.List<LocalDepthSample>();
            int validDepthSamples = 0;
            
            // Track min/max directions in local space (component-wise)
            Vector3 minLocalDir = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 maxLocalDir = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            
            for (int y = 0; y < height; y += downsampleFactor)
            {
                for (int x = 0; x < width; x += downsampleFactor)
                {
                    // Get depth value from NativeArray (row-major order)
                    int index = y * width + x;
                    
                    if (index >= depthData.Length)
                        continue;
                    
                    float depthNDC = depthData[index];
                    
                    // Convert from reversed logarithmic NDC to linear depth
                    float ndc = depthNDC * 2.0f - 1.0f;
                    float denom = ndc + (-1.0f);
                    if (Mathf.Abs(denom) < 0.0001f) continue;
                    float depth = (-2.0f * frameDesc.nearZ) / denom;
                    
                    // Skip invalid depth values
                    if (depth < 0.1f || depth > 3.0f)
                        continue;
                    
                    validDepthSamples++;
                    
                    // Unproject pixel to local depth camera space
                    Vector3 localPos = UnprojectDepthPixelLocal(x, y, width, height, frameDesc, depth);
                    
                    // Calculate direction in local space (from origin to point, normalized)
                    Vector3 localDir = localPos.normalized;
                    
                    // Track min/max directions (component-wise)
                    if (localDir.x < minLocalDir.x) minLocalDir.x = localDir.x;
                    if (localDir.y < minLocalDir.y) minLocalDir.y = localDir.y;
                    if (localDir.z < minLocalDir.z) minLocalDir.z = localDir.z;
                    if (localDir.x > maxLocalDir.x) maxLocalDir.x = localDir.x;
                    if (localDir.y > maxLocalDir.y) maxLocalDir.y = localDir.y;
                    if (localDir.z > maxLocalDir.z) maxLocalDir.z = localDir.z;
                    
                    // Store local sample for second pass
                    localSamples.Add(new LocalDepthSample
                    {
                        localPos = localPos,
                        depth = depth,
                        x = x,
                        y = y
                    });
                    
                    // Debug center pixel from both eyes to compare
                    if (x == width / 2 && y == height / 2)
                    {

                        float left = frameDesc.fovLeftAngleTangent;
                        float right = frameDesc.fovRightAngleTangent;
                        float top = frameDesc.fovTopAngleTangent;
                        float down = frameDesc.fovDownAngleTangent;
                        
                        float fx = width / (right + left);
                        float fy = height / (top + down);
                        float cx = width * right / (right + left);
                        float cy = height * top / (top + down);
                        
                        float px_center = x + 0.5f;
                        float py_center = y + 0.5f;
                        py_center = height - py_center; // Undo compute shader flip
                        
                        float tanX_center = (px_center - cx) / fx;
                        float tanY_center = (py_center - cy) / fy;
                        
                        Debug.Log($"[{Constants.LOG_TAG}] Eye {eyeIndex} intrinsics:\n" +
                                  $"  FOV tangents: L={left:F3}, R={right:F3}, T={top:F3}, D={down:F3}\n" +
                                  $"  Principal point: cx={cx:F1}, cy={cy:F1} (center pixel is at {x}, {y})\n" +
                                  $"  Focal length: fx={fx:F1}, fy={fy:F1}\n" +
                                  $"  Center pixel tanX={tanX_center:F4}, tanY={tanY_center:F4} (should be ~0 for center)\n" +
                                  $"Eye {eyeIndex} center pixel ({x},{y}): depthNDC={depthNDC:F3} → linearDepth={depth:F3}m\n" +
                                  $"  Frame pose: pos={frameDesc.createPoseLocation} rot={frameDesc.createPoseRotation.eulerAngles}\n" +
                                  $"  LocalPos (depth camera space)={localPos}\n" +
                                  $"  LocalDir (normalized)={localDir}");
                    }
                }
            }
            
            // Print min/max direction statistics
            Debug.Log($"[{Constants.LOG_TAG}] Eye {eyeIndex} LOCAL SPACE direction stats:\n" +
                      $"  Min direction (component-wise): {minLocalDir}\n" +
                      $"  Max direction (component-wise): {maxLocalDir}\n" +
                      $"  Min direction (euler): {Quaternion.LookRotation(minLocalDir).eulerAngles}\n" +
                      $"  Max direction (euler): {Quaternion.LookRotation(maxLocalDir).eulerAngles}");
            
            // ===== PASS 2: Transform to world space and emit particles =====
            int particlesEmitted = 0;
            
            Quaternion rotation = frameDesc.createPoseRotation;
            Vector3 position = frameDesc.createPoseLocation;
            
            // Use depth camera pose for view direction (matches the coordinate system of worldPos)
            Vector3 depthCameraPos = position;
            
            // Debug: Log offset between depth camera and center eye (once per frame)
            Vector3 offset = cameraPos - depthCameraPos;
            Debug.Log($"[{Constants.LOG_TAG}] Eye {eyeIndex} pose offset: depthCamera={depthCameraPos}, centerEye={cameraPos}, offset={offset} (magnitude: {offset.magnitude:F3}m)");
            
            foreach (var sample in localSamples)
            {
                // Transform local position to world space
                Vector3 worldPos = position + rotation * sample.localPos;
                
                // Check if valid point (use depth camera position for distance check)
                float distance = Vector3.Distance(worldPos, depthCameraPos);
                if (distance < 0.1f || distance > 3.0f)
                    continue;
                
                // Calculate view direction (from surface to depth camera)
                Vector3 viewDir = (depthCameraPos - worldPos).normalized;
                
                // Color based on viewing angle
                //Color lineColor = DirectionToColor(viewDir);
                Color lineColor = eyeIndex == 0 ? Color.red : Color.blue;

                // Emit coverage line particle
                EmitCoverageLine(worldPos, viewDir, lineColor);
                particlesEmitted++;
            }
            
            Debug.Log($"[{Constants.LOG_TAG}] Eye {eyeIndex}: {validDepthSamples} valid depth samples, {particlesEmitted} particles emitted");
        }
        
        /// <summary>
        /// Unprojects a depth pixel to local depth camera space (before world transform).
        /// </summary>
        private Vector3 UnprojectDepthPixelLocal(int x, int y, int width, int height, DepthFrameDesc frameDesc, float depth)
        {
            // Compute camera intrinsics from FOV tangents
            float left = frameDesc.fovLeftAngleTangent;
            float right = frameDesc.fovRightAngleTangent;
            float top = frameDesc.fovTopAngleTangent;
            float down = frameDesc.fovDownAngleTangent;
            
            float fx = width / (right + left);
            float fy = height / (top + down);
            float cx = width * right / (right + left);
            float cy = height * top / (top + down);
            
            float px = x + 0.5f;
            float py = y + 0.5f;
            py = height - py; // Undo compute shader flip
            
            float tanX = (px - cx) / fx;
            float tanY = (py - cy) / fy;
            
            // Standard camera unprojection: ray direction in camera space
            // X: right (positive), Y: up (positive), Z: forward (positive)
            Vector3 rayDir = new Vector3(tanX, tanY, 1.0f);
            
            // Meta's SDK applies scale(1, 1, -1) to the view matrix for rendering
            // For unprojection, we may need the inverse transformation
            // However, the pose rotation should already account for coordinate system differences
            // If there's still an offset, it might be a pose coordinate system issue
            
            return rayDir * depth;
        }
        
        /// <summary>
        /// Unprojects a depth pixel to 3D world space using the depth frame descriptor.
        /// Uses the frame descriptor's FOV and pose information for accurate unprojection.
        /// </summary>
        private Vector3 UnprojectDepthPixel(int x, int y, int width, int height, DepthFrameDesc frameDesc, float depth)
        {
            Vector3 localPos = UnprojectDepthPixelLocal(x, y, width, height, frameDesc, depth);
            
            // Just use position and rotation, NO scale
            Quaternion rotation = frameDesc.createPoseRotation;
            Vector3 position = frameDesc.createPoseLocation;
            
            Vector3 worldPos = position + rotation * localPos;
            return worldPos;
        }
        
        /// <summary>
        /// Emits a single coverage line particle at the specified position and direction.
        /// </summary>
        private void EmitCoverageLine(Vector3 position, Vector3 direction, Color color)
        {
            emitParams.ResetPosition();
            emitParams.ResetRotation();
            emitParams.ResetVelocity();
            
            emitParams.position = position;
            emitParams.velocity = new Vector3(0.00001f, 0.00001f, 0.00001f); // No velocity for sphere meshes
            emitParams.startColor = color;
            emitParams.startSize = 0.001f; // 5cm spheres for debugging visibility
            emitParams.startLifetime = lineLifetime;
            
            coverageParticleSystem.Emit(emitParams, 1);
        }
        
        /// <summary>
        /// Converts a view direction vector to a color using horizontal angle as hue.
        /// </summary>
        private Color DirectionToColor(Vector3 viewDirection)
        {
            // Calculate horizontal angle (0-360 degrees)
            float angle = Mathf.Atan2(viewDirection.x, viewDirection.z) * Mathf.Rad2Deg;
            
            // Normalize to 0-1 for hue
            float hue = (angle + 180f) / 360f;
            
            // Convert HSV to RGB
            return Color.HSVToRGB(hue, colorSaturation, colorBrightness);
        }
        
        /// <summary>
        /// Clears all existing coverage lines.
        /// </summary>
        public void Clear()
        {
            if (coverageParticleSystem != null)
            {
                coverageParticleSystem.Clear();
                Debug.Log($"[{Constants.LOG_TAG}] CoverageLineVisualizer: Cleared all coverage lines");
            }
        }
        
        private void OnValidate()
        {
            // Clamp values in editor
            lineLength = Mathf.Max(0.01f, lineLength);
            downsampleFactor = Mathf.Max(1, downsampleFactor);
            lineLifetime = Mathf.Max(1f, lineLifetime);
            colorSaturation = Mathf.Clamp01(colorSaturation);
            colorBrightness = Mathf.Clamp01(colorBrightness);
        }
    }
}

