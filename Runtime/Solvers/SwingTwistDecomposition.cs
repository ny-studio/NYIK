using UnityEngine;

namespace NYIK.Solvers
{
    /// <summary>
    /// 四元数を指定軸周りの「捻り (twist)」と
    /// 軸に垂直な「振り (swing)」に分解するユーティリティ。
    /// </summary>
    /// <remarks>
    /// 参考: https://allenchou.net/2018/05/game-math-swing-twist-interpolation-sterp/
    /// 分解の定義は q = swing * twist で、twist が axis 周りの回転、
    /// swing が axis に垂直な平面内での残差回転。
    /// </remarks>
    public static class SwingTwistDecomposition
    {
        /// <summary>
        /// 退化判定（twist 成分のノルム^2）の閾値。
        /// ベクトル成分が軸とほぼ直交する場合に twist を identity とみなす。
        /// </summary>
        private const float DegenerateEpsilon = 1e-6f;

        /// <summary>
        /// 四元数 <paramref name="q"/> を <paramref name="axis"/> 周りの twist と、
        /// 軸に直交する swing に分解する。
        /// </summary>
        /// <param name="q">分解対象の回転。</param>
        /// <param name="axis">捻り軸（自動正規化）。</param>
        /// <param name="swing">出力: 軸に垂直な振り成分。</param>
        /// <param name="twist">出力: 軸周りの捻り成分。</param>
        /// <remarks>q = swing * twist が成立する。</remarks>
        public static void Decompose(Quaternion q, Vector3 axis,
            out Quaternion swing, out Quaternion twist)
        {
            // 1. axis を正規化（ゼロ軸は退化、identity 返却）
            float axisSqr = axis.sqrMagnitude;
            if (axisSqr < DegenerateEpsilon)
            {
                swing = Quaternion.identity;
                twist = Quaternion.identity;
                return;
            }
            axis = axis / Mathf.Sqrt(axisSqr);

            // 2. q のベクトル部
            Vector3 r = new Vector3(q.x, q.y, q.z);

            // 3. axis 方向への射影
            float dot = Vector3.Dot(r, axis);
            Vector3 p = dot * axis;

            // 4. twist を構築
            twist = new Quaternion(p.x, p.y, p.z, q.w);

            // 退化ケース: r が axis と直交 (twist 部分が無い) → twist = identity
            float twistSqrNorm = twist.x * twist.x + twist.y * twist.y +
                                 twist.z * twist.z + twist.w * twist.w;
            if (twistSqrNorm < DegenerateEpsilon)
            {
                twist = Quaternion.identity;
            }
            else
            {
                // 正規化
                float n = Mathf.Sqrt(twistSqrNorm);
                twist = new Quaternion(twist.x / n, twist.y / n, twist.z / n, twist.w / n);
            }

            // 5. swing = q * Inverse(twist)
            swing = q * Quaternion.Inverse(twist);
        }

        /// <summary>
        /// <paramref name="q"/> から <paramref name="axis"/> 周りの twist 成分のみを取り出す。
        /// </summary>
        public static Quaternion ExtractTwist(Quaternion q, Vector3 axis)
        {
            Decompose(q, axis, out _, out var twist);
            return twist;
        }

        /// <summary>
        /// <paramref name="q"/> から <paramref name="axis"/> に直交する swing 成分のみを取り出す。
        /// </summary>
        public static Quaternion ExtractSwing(Quaternion q, Vector3 axis)
        {
            Decompose(q, axis, out var swing, out _);
            return swing;
        }
    }
}
