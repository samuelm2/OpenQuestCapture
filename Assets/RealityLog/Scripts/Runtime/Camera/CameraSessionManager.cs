# nullable enable

using System.Collections.Generic;
using UnityEngine;
using RealityLog.Common;

namespace RealityLog.Camera
{
    public class CameraSessionManager : MonoBehaviour
    {
        private const string CAMEAR_SESSION_MANAGER_CLASS_NAME = "com.samusynth.questcamera.core.CameraSessionManager";

        private const string REGISTER_SURFACE_PROVIDER_METHOD_NAME = "registerSurfaceProvider";
        private const string SET_CAPTURE_TEMPLATE_METHOD_NAME = "setCaptureTemplateFromString";
        private const string OPEN_CAMERA_METHOD_NAME = "openCamera";
        private const string CLOSE_METHOD_NAME = "close";

        [SerializeField] private CameraPermissionManager cameraPermissionManager = default!;
        [SerializeField] private List<SurfaceProviderBase> surfaceProviders = new();
        [SerializeField] private CameraPosition cameraPosition = CameraPosition.Left;
        [SerializeField] private CameraUseCase useCase = CameraUseCase.STILL_CAPTURE;

        public AndroidJavaObject? SessionManagerJavaInstance { get; private set; }
        
        private Coroutine? resumeCoroutine;
        private const float RESUME_DELAY = 0.5f; // Wait 0.5s before reopening to avoid rapid pause/resume cycles

# if UNITY_ANDROID
        private void OnEnable()
        {
            var cameraManagerJavaInstance = cameraPermissionManager.CameraManagerJavaInstance;

            if (cameraManagerJavaInstance == null)
            {
                Debug.Log($"[{Constants.LOG_TAG}] CameraManager not instantiated. Waiting for initialization...");
                cameraPermissionManager.CameraManagerInstantiated += OnCameraManagerInstantiated;
            }
            else
            {
                Instantiate(cameraManagerJavaInstance);
            }
        }

        private void OnDisable()
        {
            DestroyInstance();
            cameraPermissionManager.CameraManagerInstantiated -= OnCameraManagerInstantiated;
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                // App is going to background/sleep - close camera to release resources
                // Cancel any pending resume operations
                if (resumeCoroutine != null)
                {
                    StopCoroutine(resumeCoroutine);
                    resumeCoroutine = null;
                }
                
                Debug.Log($"[{Constants.LOG_TAG}] App pausing - closing camera session");
                DestroyInstance();
            }
            else
            {
                // App is resuming - delay camera reopening to avoid rapid pause/resume cycles
                // This is common during VR app startup
                if (resumeCoroutine != null)
                {
                    StopCoroutine(resumeCoroutine);
                }
                resumeCoroutine = StartCoroutine(DelayedResume());
            }
        }
        
        private System.Collections.IEnumerator DelayedResume()
        {
            Debug.Log($"[{Constants.LOG_TAG}] App resuming - waiting {RESUME_DELAY}s before reopening camera...");
            yield return new WaitForSeconds(RESUME_DELAY);
            
            Debug.Log($"[{Constants.LOG_TAG}] Reopening camera session");
            var cameraManagerJavaInstance = cameraPermissionManager.CameraManagerJavaInstance;
            if (cameraManagerJavaInstance != null)
            {
                Instantiate(cameraManagerJavaInstance);
            }
            else
            {
                Debug.LogWarning($"[{Constants.LOG_TAG}] Cannot reopen camera -- CameraManager not available");
            }
            
            resumeCoroutine = null;
        }

        private void OnCameraManagerInstantiated(AndroidJavaObject cameraManagerJavaInstance)
        {
            Debug.Log($"[{Constants.LOG_TAG}] OnCameraManagerInstantiated");
            Instantiate(cameraManagerJavaInstance);
        }

        private void Instantiate(AndroidJavaObject cameraManagerJavaInstance)
        {
            if (SessionManagerJavaInstance != null)
                return;

            if (surfaceProviders.Count == 0)
            {
                Debug.LogWarning($"[{Constants.LOG_TAG}] No Surface Provider registered.");
                return;
            }

            var metaData = cameraPosition switch
            {
                CameraPosition.Left => cameraPermissionManager.LeftCameraMetaData,
                CameraPosition.Right => cameraPermissionManager.RightCameraMetaData,
                _ => null
            };

            if (metaData == null)
            {
                return;
            }

            Debug.Log($"[{Constants.LOG_TAG}] {metaData}");

            SessionManagerJavaInstance = new AndroidJavaObject(CAMEAR_SESSION_MANAGER_CLASS_NAME);

            foreach (ISurfaceProvider provider in surfaceProviders)
            {
                var providerJavaInstance = provider.GetJavaInstance(metaData);

                if (providerJavaInstance == null)
                {
                    Debug.LogWarning($"[{Constants.LOG_TAG}] Failed to create Surface Provider AndroidJavaObject.");
                    continue;
                }

                SessionManagerJavaInstance.Call(REGISTER_SURFACE_PROVIDER_METHOD_NAME, providerJavaInstance);
            }
            SessionManagerJavaInstance.Call(SET_CAPTURE_TEMPLATE_METHOD_NAME, useCase.ToString());
            
            using (AndroidJavaClass unityPlayerClazz = new AndroidJavaClass(Constants.UNITY_PLAYER_CLASS_NAME))
            using (AndroidJavaObject currentActivity = unityPlayerClazz.GetStatic<AndroidJavaObject>(Constants.UNITY_PLAYER_CURRENT_ACTIVITY_VARIABLE_NAME))
            {
                SessionManagerJavaInstance.Call(
                    OPEN_CAMERA_METHOD_NAME,
                    currentActivity,
                    cameraManagerJavaInstance,
                    metaData.cameraId
                );
            }

            Debug.Log($"[{Constants.LOG_TAG}] Camera Session ID={metaData.cameraId} started.");
        }

        private void DestroyInstance()
        {
            SessionManagerJavaInstance?.Call(CLOSE_METHOD_NAME);
            SessionManagerJavaInstance?.Dispose();
            SessionManagerJavaInstance = null;
        }
# endif

        enum CameraPosition
        {
            Left,
            Right
        }

        enum CameraUseCase
        {
            PREVIEW,
            STILL_CAPTURE,
            RECORD,
            VIDEO_SNAPSHOT,
            ZERO_SHUTTER_LAG,
            MANUAL,
        }
    }
}