# nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using RealityLog.Common;

namespace RealityLog.IO
{
    public class DepthRenderTextureExporter : IDisposable
    {
        private readonly ComputeShader computeShader;
        private readonly int kernel;

        private readonly Queue<GraphicsBuffer> bufferPool = new();
        private bool isDisposed = false;

        private readonly object bufferPoolLock = new();
        
        // Event fired when depth data is ready (after AsyncGPUReadback completes)
        // Fires separately for each eye (eyeIndex: 0=left, 1=right)
        // Parameters: (depthData, width, height, eyeIndex, frameDescriptor)
        public event Action<NativeArray<float>, int, int, int, DepthFrameDesc>? OnDepthDataReady;

        public DepthRenderTextureExporter(ComputeShader computeShader)
        {
            this.computeShader = computeShader ?? throw new ArgumentNullException(nameof(computeShader));
            kernel = this.computeShader.FindKernel("CopyRT");
        }

        public void Export(RenderTexture sourceRT, string leftDepthOutputPath, string rightDepthOutputPath, DepthFrameDesc[] frameDescriptors)
        {
            if (isDisposed)
            {
                Debug.LogError("RenderTextureExporter has been disposed.");
                return;
            }

            if (sourceRT == null || !sourceRT.IsCreated())
            {
                Debug.LogError("RenderTexture is not created or null.");
                return;
            }

            var width = sourceRT.width;
            var height = sourceRT.height;
            var depth = sourceRT.volumeDepth; // For Texture2DArray, this is the number of slices
            var pixelCount = width * height;
            
            Debug.Log($"DepthRenderTextureExporter: sourceRT dimensions = {width}x{height}x{depth} (width x height x slices)");
            
            // sourceRT is a Texture2DArray with 2 slices (left and right eyes)
            // Each slice is already width x height, so width is already the eye width
            int eyeWidth = width;  // Already eye width, not full width
            int eyeHeight = height;
            
            Debug.Log($"DepthRenderTextureExporter: Each eye = {eyeWidth}x{eyeHeight}, PNGs will be {eyeWidth}x{eyeHeight}");

            GraphicsBuffer leftEyeBuffer = GetOrCreateBuffer(pixelCount);
            GraphicsBuffer rightEyeBuffer = GetOrCreateBuffer(pixelCount);

            computeShader.SetTexture(kernel, "InputTex", sourceRT);
            computeShader.SetBuffer(kernel, "LeftEyeDepth", leftEyeBuffer);
            computeShader.SetBuffer(kernel, "RightEyeDepth", rightEyeBuffer);
            computeShader.SetInt("_Width", width);
            computeShader.SetInt("_Height", height);

            int groupsX = Mathf.CeilToInt(width / 8f);
            int groupsY = Mathf.CeilToInt(height / 8f);
            computeShader.Dispatch(kernel, groupsX, groupsY, 1);

            RequestGPUReadbackAndSave(leftEyeBuffer, leftDepthOutputPath, eyeWidth, eyeHeight, 0, frameDescriptors[0]);
            RequestGPUReadbackAndSave(rightEyeBuffer, rightDepthOutputPath, eyeWidth, eyeHeight, 1, frameDescriptors[1]);
        }

        public void Dispose()
        {
            isDisposed = true;
            ClearAllBuffers();
        }

        private GraphicsBuffer GetOrCreateBuffer(int pixelCount)
        {
            lock (bufferPoolLock)
            {
                if (bufferPool.Count > 0)
                {
                    var pooledBuffer = bufferPool.Dequeue();

                    if (pooledBuffer.count == pixelCount)
                    {
                        return pooledBuffer;
                    }
                    else
                    {
                        pooledBuffer.Dispose();
                    }
                }
            }

            return new GraphicsBuffer(GraphicsBuffer.Target.Structured, pixelCount, sizeof(float));
        }

        private void ReturnBuffer(GraphicsBuffer buffer)
        {
            lock (bufferPoolLock)
            {
                if (isDisposed)
                {
                    buffer.Dispose();
                }
                else
                {
                    bufferPool.Enqueue(buffer);
                }
            }
        }

        private void ClearAllBuffers()
        {
            lock (bufferPoolLock)
            {
                while (bufferPool.Count > 0)
                {
                    var b = bufferPool.Dequeue();
                    b.Dispose();
                }
            }
        }

        private void RequestGPUReadbackAndSave(GraphicsBuffer buffer, string outputPath, int width, int height, int eyeIndex, DepthFrameDesc frameDescriptor)
        {
            AsyncGPUReadback.Request(buffer, request =>
            {
                if (request.hasError)
                {
                    Debug.LogError("AsyncGPUReadback failed.");
                    ReturnBuffer(buffer);
                    return;
                }

                var data = request.GetData<float>();
                SaveAsRaw(data, outputPath, () => ReturnBuffer(buffer));
                
                // Also save as PNG for visual inspection
                string pngPath = outputPath.Replace(".raw", ".png");
                SaveAsPNG(data, pngPath, width, height, frameDescriptor.nearZ);
                
                // Fire event for visualization/processing (with frame descriptor)
                // Subscribers can process the data while it's still valid
                try
                {
                    OnDepthDataReady?.Invoke(data, width, height, eyeIndex, frameDescriptor);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error in OnDepthDataReady handler: {ex}");
                }

            });
        }

        private void SaveAsRaw(NativeArray<float> data, string path, Action onComplete)
        {
            Task.Run(() =>
            {
                try
                {
                    int byteLength = data.Length * sizeof(float);
                    byte[] rawBytes = new byte[byteLength];
                    Buffer.BlockCopy(data.ToArray(), 0, rawBytes, 0, byteLength);
                    File.WriteAllBytes(path, rawBytes);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Failed to save raw data: {ex}");
                }
                finally
                {
                    onComplete?.Invoke();
                }
            });
        }
        
        private void SaveAsPNG(NativeArray<float> depthData, string path, int width, int height, float nearZ)
        {
            // AsyncGPUReadback callback runs on main thread, safe to create textures
            try
            {
                int expectedSize = width * height;
                if (depthData.Length != expectedSize)
                {
                    Debug.LogWarning($"Depth data size mismatch: expected {expectedSize}, got {depthData.Length}. Using min size.");
                }
                
                int size = Math.Min(depthData.Length, expectedSize);
                Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                Color[] pixels = new Color[expectedSize];
                
                // Convert depth NDC to visible grayscale
                // Reversed log depth: close = low NDC, far = high NDC
                // For visualization, map NDC [0, 1] to grayscale with contrast adjustment
                // NOTE: The compute shader flips V (v = _Height - id.y - 1), so buffer row 0 = image bottom
                // We need to flip Y when writing to PNG to get correct orientation
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int bufferIndex = y * width + x;
                        if (bufferIndex >= size) continue;
                        
                        float depthNDC = depthData[bufferIndex];
                        
                        // Convert NDC to linear depth for better visualization
                        float ndc = depthNDC * 2.0f - 1.0f;
                        float denom = ndc + (-1.0f);
                        float linearDepth;
                        
                        if (Mathf.Abs(denom) < 0.0001f)
                        {
                            linearDepth = float.MaxValue; // Infinity
                        }
                        else
                        {
                            linearDepth = (-2.0f * nearZ) / denom;
                        }
                        
                        // Map linear depth to grayscale: 0.1m (black) to 3m (white)
                        float normalized = Mathf.Clamp01((linearDepth - 0.1f) / (3.0f - 0.1f));
                        
                        // Invalid depths (0 or infinity) show as red
                        Color pixelColor;
                        if (depthNDC < 0.001f || linearDepth > 10f)
                        {
                            pixelColor = Color.red;
                        }
                        else
                        {
                            pixelColor = new Color(normalized, normalized, normalized);
                        }
                        
                        // Flip Y: buffer row 0 (bottom) → PNG row height-1 (top)
                        int pngY = height - 1 - y;
                        int pngIndex = pngY * width + x;
                        pixels[pngIndex] = pixelColor;
                    }
                }
                
                tex.SetPixels(pixels);
                tex.Apply();
                
                byte[] pngBytes = tex.EncodeToPNG();
                File.WriteAllBytes(path, pngBytes);
                
                UnityEngine.Object.Destroy(tex);
                
                Debug.Log($"Saved depth PNG: {path}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to save depth PNG: {ex}");
            }
        }
    }
}