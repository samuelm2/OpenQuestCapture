#nullable enable

using System.Collections.Generic;
using UnityEngine;
using TMPro;
using RealityLog;
using RealityLog.Common;
using RealityLog.Depth;

namespace RealityLog.UI
{
    /// <summary>
    /// Small head-locked HUD line under the recording indicator:
    ///   - share of seen voxels that are green (plus red/amber shares and the voxel count)
    ///   - a parallax warning when the wearer has been turning in place without moving
    /// The label is a world-space TextMeshPro object created at runtime as a child of the centre-eye camera,
    /// so no canvas layout changes are needed in the scene.
    /// </summary>
    public class CoverageHud : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private DepthPointCloudRenderer pointCloudRenderer = default!;
        [SerializeField] private RecordingManager recordingManager = default!;
        [Tooltip("Centre-eye camera transform the label follows")]
        [SerializeField] private Transform cameraTransform = default!;
        [Tooltip("Any TMP text in the scene; its font asset is reused so the font ships with the build")]
        [SerializeField] private TMP_Text templateText = default!;

        [Header("Layout")]
        [Tooltip("Position of the label relative to the camera, in metres (x right, y up, z forward)")]
        [SerializeField] private Vector3 localOffset = new Vector3(0f, -0.21f, 1.0f);
        [Tooltip("TMP font size; with the 0.1 object scale below, 3 = letters about 3 cm tall at 1 m")]
        [SerializeField] private float fontSize = 2.6f;
        [SerializeField] private float labelScale = 0.1f;
        [SerializeField] private float updateInterval = 0.25f;

        [Header("Parallax warning")]
        [Tooltip("Seconds of head motion history examined")]
        [SerializeField] private float windowSeconds = 4f;
        [Tooltip("Warn when the head has turned at least this many degrees of yaw within the window ...")]
        [SerializeField] private float rotationThresholdDegrees = 60f;
        [Tooltip("... while moving less than this many metres from where it was")]
        [SerializeField] private float translationThresholdMeters = 0.15f;
        [Tooltip("Minimum time the warning stays visible once shown")]
        [SerializeField] private float warningHoldSeconds = 2f;

        private TextMeshPro? label;
        private float lastUpdateTime;
        private float warningUntil = -1f;

        private struct PoseSample { public float time; public Vector3 position; public float yaw; }
        private readonly List<PoseSample> history = new();
        private const float SampleInterval = 0.1f;
        private float lastSampleTime = -1f;

        /// <summary>True while the parallax warning is being shown.</summary>
        public bool ParallaxWarningActive => Time.unscaledTime < warningUntil;

        private void Start()
        {
            if (cameraTransform == null)
            {
                Debug.LogError($"[{Constants.LOG_TAG}] CoverageHud: camera transform not assigned");
                enabled = false;
                return;
            }

            var go = new GameObject("CoverageHudLabel");
            go.transform.SetParent(cameraTransform, false);
            go.transform.localPosition = localOffset;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * labelScale;

            label = go.AddComponent<TextMeshPro>();
            if (templateText != null && templateText.font != null)
            {
                label.font = templateText.font;
            }
            label.fontSize = fontSize;
            label.alignment = TextAlignmentOptions.Top;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.richText = true;
            label.color = Color.white;
            label.outlineWidth = 0.2f;
            label.outlineColor = new Color32(0, 0, 0, 200);
            label.rectTransform.sizeDelta = new Vector2(9f, 2f); // x labelScale = 0.9 m x 0.2 m at 1 m
            label.text = string.Empty;
        }

        private void Update()
        {
            if (label == null) return;

            bool recording = recordingManager != null && recordingManager.IsRecording;
            if (!recording)
            {
                if (label.text.Length > 0) label.text = string.Empty;
                history.Clear();
                warningUntil = -1f;
                return;
            }

            SampleHeadPose();

            if (Time.unscaledTime - lastUpdateTime < updateInterval) return;
            lastUpdateTime = Time.unscaledTime;

            label.text = BuildText();
        }

        private void SampleHeadPose()
        {
            float now = Time.unscaledTime;
            if (now - lastSampleTime < SampleInterval) return;
            lastSampleTime = now;

            var fwd = cameraTransform.forward;
            float yaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            history.Add(new PoseSample { time = now, position = cameraTransform.position, yaw = yaw });
            while (history.Count > 0 && now - history[0].time > windowSeconds) history.RemoveAt(0);

            if (history.Count < 5) return;

            float rotation = 0f;
            float maxTranslation = 0f;
            var latest = history[history.Count - 1].position;
            for (int i = 1; i < history.Count; i++)
            {
                rotation += Mathf.Abs(Mathf.DeltaAngle(history[i - 1].yaw, history[i].yaw));
                float d = Vector3.Distance(history[i].position, latest);
                if (d > maxTranslation) maxTranslation = d;
            }

            if (rotation >= rotationThresholdDegrees && maxTranslation < translationThresholdMeters)
            {
                warningUntil = now + warningHoldSeconds;
            }
        }

        private string BuildText()
        {
            var grid = pointCloudRenderer != null ? pointCloudRenderer.Coverage : null;
            string line1;
            if (grid == null || !pointCloudRenderer!.CoverageMode)
            {
                line1 = "Coverage overlay off";
            }
            else if (grid.Count == 0)
            {
                line1 = "Coverage: waiting for depth...";
            }
            else
            {
                grid.CountStates(out int red, out int amber, out int green);
                int total = red + amber + green;
                int greenPct = Mathf.RoundToInt(100f * green / total);
                int amberPct = Mathf.RoundToInt(100f * amber / total);
                int redPct = Mathf.Max(0, 100 - greenPct - amberPct);
                string full = grid.IsFull ? " (cap)" : string.Empty;
                line1 = $"<color=#33F24C>{greenPct}% green</color>  <color=#FFB80D>{amberPct}% amber</color>  <color=#FF3326>{redPct}% red</color>  {total:N0} cells{full}";
            }

            if (ParallaxWarningActive)
            {
                return line1 + "\n<color=#FFB80D>← →  Step sideways: turning in place adds no parallax</color>";
            }
            return line1;
        }
    }
}
