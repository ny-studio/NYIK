using System;
using UnityEngine;

namespace NYIK.Humanoid
{
    /// <summary>
    /// Estimates pelvis position from head position.
    /// Produces natural upper body lean and crouch from HMD-only tracking.
    /// </summary>
    [Serializable]
    public class PelvisEstimator
    {
        [SerializeField, Range(0f, 1f)] float m_SpineStiffness = 0.5f;
        [SerializeField] float m_PelvisHeightOffset = -0.05f;
        [SerializeField, Range(0f, 1f)] float m_BodyDropWeight = 0.8f;
        [SerializeField] float m_SmoothTime = 0.05f;

        [Header("Predictive Mode (head motion history)")]
        [Tooltip("Use head VELOCITY in addition to head pitch to distinguish " +
                 "forward lean from looking down. Reduces the false-positive " +
                 "lean trigger that happens whenever the user just looks at their feet.")]
        [SerializeField] bool m_UsePredictive = true;
        [Tooltip("Weight blend between head-pitch-only lean (0) and velocity-corroborated lean (1).")]
        [SerializeField, Range(0f, 1f)] float m_VelocityCorroboration = 0.7f;
        [Tooltip("Head velocity threshold (m/s) above which lean is corroborated.")]
        [SerializeField] float m_VelocityForwardThreshold = 0.05f;
        [Tooltip("Length of head position history kept for velocity estimation (frames).")]
        [SerializeField, Range(3, 30)] int m_HistoryLength = 10;

        float m_SpineLength;
        float m_InitialPelvisHeight;
        Vector3 m_InitialPelvisLocalPosition;
        bool m_Initialized;
        Vector3 m_SmoothedPosition;
        bool m_HasPreviousPosition;

        // Head history ring buffer (predictive mode)
        Vector3[] m_HeadPosHistory;
        float[] m_HeadHistoryDt;
        int m_HistoryIndex;
        int m_HistoryCount;

        /// <summary>
        /// Spine stiffness. Higher values make the pelvis follow head movement more
        /// (0 = fully fixed, 1 = fully follows).
        /// </summary>
        public float SpineStiffness
        {
            get => m_SpineStiffness;
            set => m_SpineStiffness = Mathf.Clamp01(value);
        }

        /// <summary>
        /// Smoothing time in seconds (frame-rate independent). 0 = no smoothing.
        /// </summary>
        public float SmoothTime
        {
            get => m_SmoothTime;
            set => m_SmoothTime = Mathf.Max(0f, value);
        }

        /// <summary>
        /// Head-to-pelvis distance calculated at initialization.
        /// </summary>
        public float SpineLength => m_SpineLength;

        /// <summary>
        /// Initialize spine length from pelvis and head positions.
        /// </summary>
        public void Initialize(Transform pelvis, Transform head)
        {
            if (pelvis == null || head == null)
                return;

            m_SpineLength = Vector3.Distance(pelvis.position, head.position);
            m_InitialPelvisHeight = pelvis.position.y;
            m_InitialPelvisLocalPosition = pelvis.localPosition;
            m_HasPreviousPosition = false;
            m_Initialized = true;

            if (m_HeadPosHistory == null || m_HeadPosHistory.Length != m_HistoryLength)
            {
                m_HeadPosHistory = new Vector3[m_HistoryLength];
                m_HeadHistoryDt = new float[m_HistoryLength];
            }
            m_HistoryIndex = 0;
            m_HistoryCount = 0;
        }

        /// <summary>
        /// Average horizontal head velocity (forward component, in m/s)
        /// computed over the history buffer. Negative if moving backwards.
        /// </summary>
        float SampleForwardVelocity(Vector3 currentHeadPos, Vector3 headForward, float dt)
        {
            if (m_HeadPosHistory == null) return 0f;

            // Record current sample
            m_HeadPosHistory[m_HistoryIndex] = currentHeadPos;
            m_HeadHistoryDt[m_HistoryIndex] = dt;
            m_HistoryIndex = (m_HistoryIndex + 1) % m_HeadPosHistory.Length;
            m_HistoryCount = Mathf.Min(m_HistoryCount + 1, m_HeadPosHistory.Length);
            if (m_HistoryCount < 3) return 0f;

            // Find the oldest valid sample
            int oldestIdx = (m_HistoryIndex + m_HeadPosHistory.Length - m_HistoryCount) % m_HeadPosHistory.Length;
            Vector3 oldest = m_HeadPosHistory[oldestIdx];

            float totalDt = 0f;
            for (int k = 0; k < m_HistoryCount - 1; k++)
                totalDt += m_HeadHistoryDt[(oldestIdx + k + 1) % m_HeadPosHistory.Length];
            if (totalDt < 1e-4f) return 0f;

            Vector3 displacement = currentHeadPos - oldest;
            // Project displacement onto head's horizontal forward.
            Vector3 horizForward = headForward; horizForward.y = 0f;
            if (horizForward.sqrMagnitude < 1e-6f) return 0f;
            horizForward.Normalize();
            float forwardDisp = Vector3.Dot(displacement, horizForward);
            return forwardDisp / totalDt;
        }

        /// <summary>
        /// Estimate pelvis world position from head position and rotation.
        /// </summary>
        public Vector3 Estimate(Vector3 headPosition, Quaternion headRotation, Transform rootTransform)
        {
            if (rootTransform == null)
            {
                Debug.LogWarning("[PelvisEstimator] rootTransform is null. Returning head position as fallback.");
                return headPosition;
            }

            if (!m_Initialized)
                return rootTransform.TransformPoint(m_InitialPelvisLocalPosition);

            // Base: place pelvis directly below head by spine length
            float pelvisY = headPosition.y - m_SpineLength + m_PelvisHeightOffset;

            // Crouch support: head drops below initial height
            float headDrop = m_InitialPelvisHeight + m_SpineLength - headPosition.y;
            if (headDrop > 0f)
            {
                pelvisY = Mathf.Lerp(
                    m_InitialPelvisHeight,
                    pelvisY,
                    m_BodyDropWeight
                );
            }

            // Horizontal position: follow head with forward lean offset
            Vector3 headForward = headRotation * Vector3.forward;
            headForward.y = 0f;
            // Guard: when looking straight up/down, headForward becomes zero
            if (headForward.sqrMagnitude < 0.0001f)
                headForward = rootTransform.forward;
            else
                headForward.Normalize();

            float forwardLean = Vector3.Dot(headRotation * Vector3.down, Vector3.forward);
            forwardLean = Mathf.Clamp(forwardLean, 0f, 1f);

            // Predictive correction: the pitch-only metric fires whenever the
            // user just LOOKS down. Cross-check with forward head velocity —
            // a genuine lean moves the head forward in world space; just
            // looking down doesn't. Attenuate forwardLean when velocity
            // contradicts the pitch reading.
            if (m_UsePredictive && forwardLean > 0.01f)
            {
                Vector3 headFwdHorizontal = headRotation * Vector3.forward;
                float forwardVel = SampleForwardVelocity(headPosition, headFwdHorizontal, Time.deltaTime);
                float velCorroboration = Mathf.Clamp01(forwardVel / Mathf.Max(1e-4f, m_VelocityForwardThreshold));
                // Blend: with full m_VelocityCorroboration, no velocity → no lean.
                float corroboratedLean = forwardLean * Mathf.Lerp(1f, velCorroboration, m_VelocityCorroboration);
                forwardLean = corroboratedLean;
            }

            float pelvisBackOffset = forwardLean * m_SpineLength * (1f - m_SpineStiffness);

            Vector3 rawPosition = new Vector3(
                headPosition.x - headForward.x * pelvisBackOffset,
                pelvisY,
                headPosition.z - headForward.z * pelvisBackOffset
            );

            // Frame-rate independent smoothing to suppress HMD jitter
            if (!m_HasPreviousPosition)
            {
                m_SmoothedPosition = rawPosition;
                m_HasPreviousPosition = true;
            }
            else if (m_SmoothTime > 0f)
            {
                float dt = Time.deltaTime;
                float alpha = dt > 0f ? 1f - Mathf.Exp(-dt / m_SmoothTime) : 1f;
                m_SmoothedPosition = Vector3.Lerp(m_SmoothedPosition, rawPosition, alpha);
            }
            else
            {
                m_SmoothedPosition = rawPosition;
            }

            return m_SmoothedPosition;
        }
    }
}
