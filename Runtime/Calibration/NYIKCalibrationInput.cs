using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using NYIK.Humanoid;

namespace NYIK.Calibration
{
    /// <summary>
    /// VR 入力でキャリブを発火する。既定ジェスチャは「両手グリップ同時長押し」。
    /// ヘッドセット着用中（一人での収録時）に、Inspector の ContextMenu へ手が届かなくても
    /// 中立姿勢のままハンド回転キャリブ（必要なら身長スケールも）をやり直せる。
    ///
    /// グリップは <c>InputSystem</c> でプログラム的にバインド（<c>&lt;XRController&gt;{LeftHand}/gripPressed</c>）
    /// するのでシーンへの InputAction アセット配線は不要。発火時は両手にハプティクスでフィードバック。
    ///
    /// 使い方: このコンポーネントをアバター（NYIKHumanoid と同じ階層）に付けるだけ。参照は自動検出。
    /// Play 中に「両手グリップを <see cref="_holdSeconds"/> 秒同時に握る」とキャリブが走る。
    /// </summary>
    [AddComponentMenu("NYIK/VR/NYIK Calibration Input")]
    public sealed class NYIKCalibrationInput : MonoBehaviour
    {
        [Tooltip("CalibrateHands を持つ NYIKHumanoid。空なら自動検出（self/parent/children）。")]
        [SerializeField] private NYIKHumanoid _nyikHumanoid;
        [Tooltip("身長スケール等も併せて呼ぶための FBTCalibrationHelper（任意）。空なら自動検出。")]
        [SerializeField] private FBTCalibrationHelper _calibrationHelper;

        [Header("Gesture: 両手グリップ同時長押し")]
        [Tooltip("両手グリップを同時に握り続ける秒数。誤爆を避けるため少し長め。")]
        [SerializeField] private float _holdSeconds = 1.5f;

        [Header("発火時に何をやり直すか")]
        [Tooltip("ハンド回転キャリブ（今のコントローラ向き↔手バインド）をやり直す。")]
        [SerializeField] private bool _calibrateHands = true;
        [Tooltip("身長スケールもやり直す。直立して呼ぶこと（FBTCalibrationHelper 経由）。")]
        [SerializeField] private bool _calibrateScale;

        [Header("Feedback")]
        [Tooltip("発火時に両手へハプティクス。")]
        [SerializeField] private bool _hapticOnFire = true;

        private InputAction _leftGrip;
        private InputAction _rightGrip;
        private float _heldTime;
        private bool _fired;

        private void Reset() => AutoDetect();

        private void Awake()
        {
            if (_nyikHumanoid == null || _calibrationHelper == null) AutoDetect();
        }

        private void AutoDetect()
        {
            if (_nyikHumanoid == null)
            {
                _nyikHumanoid = GetComponentInParent<NYIKHumanoid>();
                if (_nyikHumanoid == null) _nyikHumanoid = GetComponentInChildren<NYIKHumanoid>();
            }
            if (_calibrationHelper == null)
            {
                _calibrationHelper = GetComponentInParent<FBTCalibrationHelper>();
                if (_calibrationHelper == null) _calibrationHelper = GetComponentInChildren<FBTCalibrationHelper>();
            }
        }

        private void OnEnable()
        {
            // プログラム的バインド（シーンの InputAction アセット不要）。
            _leftGrip = new InputAction("NYIK_LeftGrip", InputActionType.Button,
                "<XRController>{LeftHand}/gripPressed");
            _rightGrip = new InputAction("NYIK_RightGrip", InputActionType.Button,
                "<XRController>{RightHand}/gripPressed");
            _leftGrip.Enable();
            _rightGrip.Enable();
            _heldTime = 0f;
            _fired = false;
        }

        private void OnDisable()
        {
            _leftGrip?.Disable();
            _leftGrip?.Dispose();
            _leftGrip = null;
            _rightGrip?.Disable();
            _rightGrip?.Dispose();
            _rightGrip = null;
        }

        private void Update()
        {
            bool bothHeld = _leftGrip != null && _rightGrip != null
                            && _leftGrip.IsPressed() && _rightGrip.IsPressed();

            if (!bothHeld)
            {
                // どちらか離したらリセット（離して再び握るまで再発火しない）。
                _heldTime = 0f;
                _fired = false;
                return;
            }

            _heldTime += Time.deltaTime;
            if (_heldTime >= _holdSeconds && !_fired)
            {
                _fired = true;          // 1 回の長押しで 1 回だけ発火
                Fire();
            }
        }

        private void Fire()
        {
            if (_calibrateScale)
            {
                if (_calibrationHelper != null) _calibrationHelper.CalibrateScale();
                else Debug.LogWarning("[NYIKCalibrationInput] FBTCalibrationHelper 未割当でスケールキャリブ不可。", this);
            }

            if (_calibrateHands)
            {
                if (_calibrationHelper != null) _calibrationHelper.CalibrateHands();
                else if (_nyikHumanoid != null) _nyikHumanoid.CalibrateHands();
                else Debug.LogWarning("[NYIKCalibrationInput] NYIKHumanoid 未割当でハンドキャリブ不可。", this);
            }

            if (_hapticOnFire)
            {
                Pulse(XRNode.LeftHand);
                Pulse(XRNode.RightHand);
            }

            Debug.Log("[NYIKCalibrationInput] Recalibrated (両手グリップ同時長押し)。" +
                      $" hands={_calibrateHands} scale={_calibrateScale}", this);
        }

        private static void Pulse(XRNode node)
        {
            var device = InputDevices.GetDeviceAtXRNode(node);
            if (device.isValid &&
                device.TryGetHapticCapabilities(out var caps) && caps.supportsImpulse)
                device.SendHapticImpulse(0u, 0.5f, 0.15f);
        }
    }
}
