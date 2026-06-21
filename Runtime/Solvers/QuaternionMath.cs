using UnityEngine;

namespace NYIK.Solvers
{
    /// <summary>
    /// Numerical helpers for quaternion math used by NYIK solvers.
    ///
    /// Why not just use <see cref="Quaternion.FromToRotation"/>?
    /// Unity's implementation is numerically unstable near 180° (antipodal
    /// input vectors): the rotation axis is undefined and a degenerate axis
    /// is picked silently, causing visible jitter when an arm or spine
    /// briefly aligns with its target's reverse direction. NYIK's IK loops
    /// hit this case routinely.
    ///
    /// <see cref="FromToRotationStable"/> picks a deterministic perpendicular
    /// in the degenerate case (using a stable basis vector), so the resulting
    /// rotation is reproducible frame to frame.
    /// </summary>
    public static class QuaternionMath
    {
        private const float NearOppositeThreshold = -0.9999f; // dot product
        private const float DegenerateEpsilon = 1e-6f;

        /// <summary>
        /// Numerically stable analogue of <see cref="Quaternion.FromToRotation"/>.
        /// Input directions need not be normalized — they will be normalized
        /// internally. Returns identity for zero-length inputs.
        /// </summary>
        public static Quaternion FromToRotationStable(Vector3 from, Vector3 to)
        {
            float fromMagSq = from.sqrMagnitude;
            float toMagSq = to.sqrMagnitude;
            if (fromMagSq < DegenerateEpsilon || toMagSq < DegenerateEpsilon)
                return Quaternion.identity;

            Vector3 f = from / Mathf.Sqrt(fromMagSq);
            Vector3 t = to / Mathf.Sqrt(toMagSq);
            float dot = Vector3.Dot(f, t);

            // Antipodal case: any perpendicular axis is a valid 180° rotation,
            // but Unity's built-in picks one non-deterministically. Choose a
            // stable perpendicular instead.
            if (dot < NearOppositeThreshold)
            {
                Vector3 axis = StablePerpendicular(f);
                return new Quaternion(axis.x, axis.y, axis.z, 0f);
            }

            // Normal case: axis = f × t, angle from dot product.
            Vector3 cross = Vector3.Cross(f, t);
            float w = 1f + dot;
            float n = Mathf.Sqrt(2f * w);
            float inv = 1f / n;
            return new Quaternion(cross.x * inv, cross.y * inv, cross.z * inv, w * inv);
        }

        /// <summary>
        /// Return a unit vector perpendicular to <paramref name="v"/> using a
        /// stable basis pick. Avoids the degenerate case where v is parallel
        /// to the chosen reference axis.
        /// </summary>
        public static Vector3 StablePerpendicular(Vector3 v)
        {
            // Pick the world axis least aligned with v.
            Vector3 a = Mathf.Abs(v.x) < 0.9f ? Vector3.right : Vector3.up;
            Vector3 perp = Vector3.Cross(v, a);
            float perpSqr = perp.sqrMagnitude;
            if (perpSqr < DegenerateEpsilon)
            {
                // Fallback (shouldn't happen with the 0.9 pick above)
                perp = Vector3.Cross(v, Vector3.forward);
                perpSqr = perp.sqrMagnitude;
                if (perpSqr < DegenerateEpsilon) return Vector3.up;
            }
            return perp / Mathf.Sqrt(perpSqr);
        }

        /// <summary>
        /// Slerp clamped to safe quaternion inputs. Handles `t` outside [0,1]
        /// by clamping and ensures the quaternions are in the same hemisphere
        /// (otherwise Slerp takes the long path).
        /// </summary>
        public static Quaternion SafeSlerp(Quaternion a, Quaternion b, float t)
        {
            if (Quaternion.Dot(a, b) < 0f)
                b = new Quaternion(-b.x, -b.y, -b.z, -b.w);
            return Quaternion.SlerpUnclamped(a, b, Mathf.Clamp01(t));
        }
    }
}
