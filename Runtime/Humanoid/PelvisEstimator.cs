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

        float m_SpineLength;
        float m_InitialPelvisHeight;
        Vector3 m_InitialPelvisLocalPosition;
        bool m_Initialized;
        Vector3 m_SmoothedPosition;
        bool m_HasPreviousPosition;

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
