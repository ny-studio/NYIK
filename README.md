# NYIK - Humanoid IK for VR

[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE.md)

Free, open-source full-body IK for VR avatars in Unity.
**HMD + 2 controllers only. Zero configuration. One component.**

> Drop-in alternative to FinalIK's VRIK — no license fee, no black box.

## Quick Start

### Install

**Window > Package Manager > + > Add package from git URL:**

```
https://github.com/ny-studio/NYIK.git
```

### Use (VR)

1. Set up an XR scene with `XROrigin` (HMD + controllers)
2. Add `NYIKHumanoid` to your Humanoid avatar
3. Play

That's it. Bones and tracking sources are auto-detected.

### Use (Without VR)

NYIK works without VR hardware. Drive IK targets from any source via code:

```csharp
var ik = GetComponent<NYIKHumanoid>();
ik.Initialize();

ik.HeadTarget.SetDirectly(headPos, headRot);
ik.LeftHandTarget.SetDirectly(lHandPos, lHandRot);
ik.RightHandTarget.SetDirectly(rHandPos, rHandRot);
ik.SolveManual();
```

Or use the `NYIKTestTargets` component to move IK targets in the Scene view during Edit mode — no Play mode or VR required.

## Features

- **Zero-config** — Bones auto-detected from Humanoid Animator, XR sources auto-detected from XROrigin
- **VR-optional** — Works with any input source via `SetDirectly()` / `SolveManual()` API
- **Rotation distribution spine** — Deterministic 1-pass solver, resets every frame (no drift)
- **Spine twist** — Head yaw distributed across spine bones for natural turning
- **Pelvis estimation** — Position inferred from HMD with crouch and lean support
- **Shoulder activation** — Shoulder rotates when hand reaches beyond arm length
- **Auto calibration** — HMD/controller rotation offsets calculated automatically on first frame
- **Frame-rate independent** — Exponential decay smoothing works consistently at any FPS
- **Editor testing** — `NYIKTestTargets` component for testing IK in Edit mode without VR
- **Validation** — Built-in setup validator warns about missing bones and hierarchy issues

## Components

| Component | Description |
|---|---|
| `NYIKHumanoid` | Main IK component. Attach to a Humanoid avatar for full-body IK. |
| `NYIKTestTargets` | Edit mode IK testing. Creates draggable targets in Scene view (no VR needed). |
| `VRIKSetup` | Optional. Override auto-detected tracking sources and offsets. |
| `VRCalibration` | Optional. T-pose calibration for avatar scaling and body proportions. |

## Architecture

```
NYIKHumanoid
├── SpineSolver          Rotation distribution (pelvis → neck → head)
│   └── PelvisEstimator  Head-based pelvis position with EMA smoothing
├── ArmSolver x2         Shoulder rotation + TwoBoneIK
│   └── TwoBoneIKSolver  Analytical 2-bone IK
└── LegSolver x2         TwoBoneIK with ground anchoring
    └── TwoBoneIKSolver
```

**Solve order:** Tracking → Spine → Arms → Legs

## API

```csharp
var ik = GetComponent<NYIKHumanoid>();

// Weight
ik.Weight = 0.5f;

// Recalibrate rotation offsets
ik.Recalibrate();

// Sub-solver access
ik.Spine.TwistWeight = 0.5f;
ik.Spine.PelvisEstimator.SpineStiffness = 0.5f;
ik.LeftArm.ShoulderRotationWeight = 0.5f;

// Custom foot target (for locomotion)
ik.LeftLeg.FootTargetPosition = worldPos;

// Manual solve without VR
ik.HeadTarget.SetDirectly(pos, rot);
ik.SolveManual();
```

## Editor Testing

Add `NYIKTestTargets` to test IK in Edit mode without VR:

1. Add Component > **NYIK/Test Targets** on your avatar
2. Target objects (head, hands, feet) are created at bone positions
3. Move targets with Scene view handles — avatar follows in real-time

No Play mode required.

## Roadmap

- [ ] Locomotion (step detection, foot placement)
- [ ] Additional tracker support (waist, feet, elbows, knees)
- [ ] Finger IK
- [ ] Look-at solver
- [ ] Sample scenes

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for development setup and guidelines.

## License

[MIT](LICENSE.md) - Copyright (c) 2026 NYStudio
