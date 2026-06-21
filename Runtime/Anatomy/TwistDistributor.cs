using UnityEngine;
using NYIK.Solvers;

namespace NYIK.Anatomy
{
    /// <summary>
    /// 親骨と孫骨の間に捻り差分がある時、中間の骨
    /// (lower arm, lower leg, spine middle) に一部の捻りを分配して
    /// 自然な見た目にするユーティリティ。
    /// </summary>
    /// <remarks>
    /// VRIK の TwistRelaxer 相当機能。Swing-Twist 分解は
    /// <see cref="SwingTwistDecomposition"/> を利用。
    /// </remarks>
    public static class TwistDistributor
    {
        /// <summary>
        /// 前腕の twist 分配。
        /// <paramref name="upperArm"/> と <paramref name="hand"/> の twist 差分を、
        /// <paramref name="lowerArm"/> に <paramref name="distributionRatio"/> の比率で適用する。
        /// </summary>
        /// <param name="upperArm">上腕ボーン。</param>
        /// <param name="lowerArm">前腕ボーン (分配対象)。</param>
        /// <param name="hand">手ボーン。</param>
        /// <param name="distributionRatio">0..1。1 で全捻りを lowerArm に転写。</param>
        public static void DistributeForearm(
            Transform upperArm, Transform lowerArm, Transform hand,
            float distributionRatio = 0.5f)
        {
            if (upperArm == null || lowerArm == null || hand == null)
            {
                Debug.LogWarning("[TwistDistributor] DistributeForearm: null transform.");
                return;
            }

            // 1. 前腕の長手方向（ワールド軸）
            Vector3 armAxisWorld = lowerArm.position - upperArm.position;
            if (armAxisWorld.sqrMagnitude < 1e-8f) return;
            armAxisWorld.Normalize();

            // 2~4. upperArm ローカル空間における relative rotation を求める
            Quaternion upperArmRot = upperArm.rotation;
            Quaternion handRot = hand.rotation;
            Quaternion relativeRot = Quaternion.Inverse(upperArmRot) * handRot;

            // axis を upperArm のローカル座標系に変換
            Vector3 axisLocal = Quaternion.Inverse(upperArmRot) * armAxisWorld;

            // 5. twist 抽出
            Quaternion twist = SwingTwistDecomposition.ExtractTwist(relativeRot, axisLocal);

            // 6. 部分 twist を lowerArm.localRotation に適用
            Quaternion partialTwist = Quaternion.SlerpUnclamped(
                Quaternion.identity, twist, Mathf.Clamp01(distributionRatio));
            lowerArm.localRotation = lowerArm.localRotation * partialTwist;
        }

        /// <summary>
        /// 脛 (shin) の twist 分配。<see cref="DistributeForearm"/> と同じロジックで脚版。
        /// </summary>
        /// <param name="upperLeg">大腿ボーン。</param>
        /// <param name="lowerLeg">脛ボーン (分配対象)。</param>
        /// <param name="foot">足ボーン。</param>
        /// <param name="distributionRatio">0..1。1 で全捻りを lowerLeg に転写。</param>
        public static void DistributeShin(
            Transform upperLeg, Transform lowerLeg, Transform foot,
            float distributionRatio = 0.5f)
        {
            if (upperLeg == null || lowerLeg == null || foot == null)
            {
                Debug.LogWarning("[TwistDistributor] DistributeShin: null transform.");
                return;
            }

            // 脚の長手方向
            Vector3 legAxisWorld = lowerLeg.position - upperLeg.position;
            if (legAxisWorld.sqrMagnitude < 1e-8f) return;
            legAxisWorld.Normalize();

            Quaternion upperLegRot = upperLeg.rotation;
            Quaternion footRot = foot.rotation;
            Quaternion relativeRot = Quaternion.Inverse(upperLegRot) * footRot;

            Vector3 axisLocal = Quaternion.Inverse(upperLegRot) * legAxisWorld;

            Quaternion twist = SwingTwistDecomposition.ExtractTwist(relativeRot, axisLocal);

            Quaternion partialTwist = Quaternion.SlerpUnclamped(
                Quaternion.identity, twist, Mathf.Clamp01(distributionRatio));
            lowerLeg.localRotation = lowerLeg.localRotation * partialTwist;
        }

        /// <summary>
        /// 脊椎チェーンに対する twist 分配。
        /// <paramref name="rootBone"/> (例: Hips) と <paramref name="topBone"/> (例: Chest/Neck) の
        /// twist 差分を、<paramref name="spineChain"/> の各骨に均等分配する。
        /// </summary>
        /// <param name="spineChain">中間 spine bone 群 (parent → child 順)。例: [Spine, Chest, UpperChest]</param>
        /// <param name="rootBone">基準となる根本ボーン (例: Hips)。</param>
        /// <param name="topBone">上端ボーン (例: Neck or Head)。</param>
        public static void DistributeSpine(
            Transform[] spineChain,
            Transform rootBone,
            Transform topBone)
        {
            if (rootBone == null || topBone == null)
            {
                Debug.LogWarning("[TwistDistributor] DistributeSpine: null root/top bone.");
                return;
            }
            if (spineChain == null || spineChain.Length == 0)
            {
                Debug.LogWarning("[TwistDistributor] DistributeSpine: empty spine chain.");
                return;
            }

            // 1. チェーン軸 (ワールド)
            Vector3 chainAxisWorld = topBone.position - rootBone.position;
            if (chainAxisWorld.sqrMagnitude < 1e-8f) return;
            chainAxisWorld.Normalize();

            // 2. root ローカル空間での相対回転と twist 抽出
            Quaternion rootRot = rootBone.rotation;
            Quaternion topRot = topBone.rotation;
            Quaternion relativeRot = Quaternion.Inverse(rootRot) * topRot;

            Vector3 axisLocal = Quaternion.Inverse(rootRot) * chainAxisWorld;

            Quaternion twist = SwingTwistDecomposition.ExtractTwist(relativeRot, axisLocal);

            // 3. 各骨への均等分配率
            float perBoneRatio = 1f / spineChain.Length;
            Quaternion perBoneTwist = Quaternion.SlerpUnclamped(
                Quaternion.identity, twist, perBoneRatio);

            // 4. 累積で適用 (parent → child の順)
            for (int i = 0; i < spineChain.Length; i++)
            {
                var bone = spineChain[i];
                if (bone == null)
                {
                    Debug.LogWarning($"[TwistDistributor] DistributeSpine: spineChain[{i}] is null, skipping.");
                    continue;
                }
                bone.localRotation = bone.localRotation * perBoneTwist;
            }
        }
    }
}
