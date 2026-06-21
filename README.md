# NYIK — Humanoid IK for VR

[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE.md)

Open-source full-body IK for Unity Humanoid avatars in VR. Zero-config 3-point (HMD + 2 controllers), with full-body tracking (FBT) when SteamVR / SlimeVR trackers are present.

Built as an alternative to FinalIK's VRIK for projects that need:
- MIT license (no Asset Store dependency)
- SlimeVR + Vive Tracker emulation pipeline first-class
- Per-project anatomical tuning (currently tuned for `Milltina` + 10-tracker SlimeVR)
- Built-in recording-stage integration (RecordingStage package)

> Status: production-quality for the narrow use case it was tuned for (VR massage / scene recording with SlimeVR). Not yet a drop-in replacement for the FinalIK VRIK feature surface (no Grounder, no InteractionSystem, no foot pivot, fewer constraints).

---

## Quick Start

### Install

**Window > Package Manager > + > Add package from git URL:**

```
https://github.com/ny-studio/NYIK.git
```

### 3-point (HMD + 2 controllers)

```csharp
// Drop NYIKHumanoid on the avatar root (must have a Humanoid Animator).
// In the Inspector:
//   1. Assign XR Origin
//   2. Leave HMD/hand sources empty → AutoDetectTrackingSources finds them
//   3. (Optional) Tune Head/Hand Position Offsets for your avatar
```

Solve runs in LateUpdate. No further setup needed.

### Full-Body Tracking (SlimeVR / Vive Tracker)

```csharp
// 1. Drop XRInputTrackerProvider on the avatar root (or any GameObject).
//    It auto-discovers SteamVR-exposed trackers via UnityEngine.XR.InputDevices.
// 2. In the Inspector, add Mappings: serial fragment → TrackerSlotKind.
//    Substring match — 5 trailing chars of a SlimeVR serial is enough.
// 3. NYIKHumanoid auto-detects the provider via ITrackerSourceProvider and
//    switches from 3-point to FBT mode when HasFullBodyTrackers is true.
```

### Calibration

```csharp
// 1. Add FBTCalibrationHelper next to NYIKHumanoid.
//    Wire its FBTCalibrationData ScriptableObject reference.
// 2. Stand in T-pose, run [ContextMenu]"Calibrate At T-Pose".
//    TPoseValidator blocks bad calibrations by default.
// 3. [ContextMenu]"Save Calibration" persists to JSON in
//    Application.persistentDataPath so builds survive a restart.
// 4. On next session, Start() auto-loads the JSON.
```

---

## Architecture

```
NYIKHumanoid (entry point, MonoBehaviour)
├── Initialize() — auto-detects bones, tracking sources, wires sub-solvers
├── Solve() — picks 3-point path or FBT path each frame
│
├── 3-point path (SolveIK)
│   ├── SpineSolver — head → spine bones via rotation distribution
│   ├── ArmSolver × 2 — shoulder → elbow → wrist via TwoBoneIK
│   └── LegSolver × 2 — hip → knee → ankle
│
└── FBT path (FBTPipeline.Solve)
    ├── (optional) ApplyWaistPosition — Hips ← Waist tracker position
    ├── ApplyDirectRotations — bone ← slot.CalibratedRotation
    ├── DistributeTwists — forearm / shin twist along chains
    ├── ConstraintRefiner — ROM clamp + bone-length restore (2 iterations)
    └── AnatomicalRefiner (final pass) — Swing-Twist or Euler ROM clamp
```

### Key building blocks

| Component | Purpose |
|---|---|
| `ITrackerSourceProvider` | Abstraction over SteamVR/OpenXR/manual sources. Implementations: `ManualTrackerSourceProvider`, `XRInputTrackerProvider` |
| `TrackerSlot` | Per-bone tracker data + calibration offsets + effective pose override (filtering hook) |
| `OneEuroFilter` / `OneEuroQuaternionFilter` | Adaptive low-pass; smooths IMU noise without lagging fast motion |
| `FBTCalibrator` | Captures rotation + position offsets at T-pose |
| `FBTCalibrationData` | Asset (Editor) + JSON (runtime) persistence |
| `TPoseValidator` | Heuristic check that pose actually IS T-pose before calibration |
| `JointROMLimits` | Static anatomical Range-of-Motion tables — Euler for hinges, Swing-Twist for shoulders/upper limbs |
| `AnatomicalRefiner` | Clamps each bone into its joint limit |
| `ConstraintRefiner` | Iterated (ROM clamp ↔ bone-length restore) — FABRIK + SQP-lite |

---

## Inspector Cheat Sheet

`NYIKHumanoid`:

- **Bone References** — leave empty for auto-detect from Animator
- **VR Tracking Sources** — XR Origin (required), HMD / hands (optional, auto-detected if blank)
- **VR Offsets** — applied in *avatar-local meters*, scale-aware (multiplied by `transform.lossyScale`)
  - `HeadPositionOffset`: from HMD position to head bone IK target.
    e.g. `(0, -0.05, -0.15)` puts the HMD at the avatar's brow, head bone 5cm below + 15cm behind.
- **First-Person View**:
  - `HideHeadInFirstPerson` (default true): scales the head bone to ~0 during the first-person camera's render, so the user doesn't see the inside of their own avatar's head. Mirrors and other cameras render the head normally.
  - `FirstPersonCamera`: typically Camera.main / VR HMD camera.
- **Spine / Arms / Legs** — per-solver tuning

`XRInputTrackerProvider`:

- **Mappings** — `Serial Or Name` (substring match) → `Kind` (TrackerSlotKind enum) → optional `TrustOverride`
- **OneEuro Filter** — enable + `MinCutoff` (0.5–3 Hz) + `Beta` (0.001–0.05). On by default. Critical for SlimeVR.

`FBTCalibrationHelper`:

- `RequireValidTPose` (default true): TPoseValidator blocks calibration on bad pose.
- `[ContextMenu]` "Calibrate At T-Pose", "Force Calibrate (Skip T-pose Check)", "Save Calibration", "Load Calibration", "Quick Reset (Rotation Only)".

---

## What's Different from FinalIK VRIK

| Feature | NYIK | FinalIK VRIK |
|---|---|---|
| Open source | ✅ MIT | ❌ paid asset |
| 3-point HMD + controllers | ✅ | ✅ |
| Full-body via Vive Tracker / SlimeVR | ✅ | ✅ |
| OneEuro filter | ✅ | ⚠️ external |
| First-person head hide | ✅ via head bone scaling | ✅ via layer mask |
| Calibration JSON persistence | ✅ runtime | ❌ Editor only |
| T-pose auto-validation | ✅ | ❌ |
| Grounder (foot grounding) | ❌ | ✅ |
| Interaction System (grab) | ❌ | ✅ |
| Foot pivot | ❌ | ✅ |
| Stretch / Bend goals | ⚠️ partial | ✅ |
| Years of edge-case hardening | <1 | 10+ |

**Use NYIK when**: this project's narrow use case is your use case (SlimeVR + Mocap recording + Humanoid avatar).
**Use FinalIK when**: you need Grounder, InteractionSystem, or production-grade edge case coverage.

---

## Filter Tuning

`XRInputTrackerProvider` and `ManualTrackerSourceProvider` both use OneEuroFilter via the `TrackerSlot.SetEffective` hook.

| Parameter | Effect | Typical |
|---|---|---|
| `MinCutoff` (Hz) | Lower = smoother at rest, more lag | 0.5–2.0 (SlimeVR: 1.0) |
| `Beta` | Higher = follows fast motion more closely | 0.001–0.05 (SlimeVR: 0.007) |

Tune for your tracker: more noise → lower MinCutoff. More lag complaints → raise Beta.

---

## Calibration Workflow

1. SteamVR + SlimeVR Server running, trackers in Vive Tracker emulation mode.
2. OpenXR HTC VIVE Tracker Profile enabled (Project Settings → XR Plug-in Management).
3. Hit Play.
4. Stand in T-pose. Right-click `FBTCalibrationHelper` → "Calibrate At T-Pose".
   - TPoseValidator checks pose validity. Bad pose → calibration aborts with a warning listing what's wrong.
   - Force override available via "Force Calibrate (Skip T-pose Check)".
5. Right-click → "Save Calibration".
   - Writes the ScriptableObject (Editor only) AND a JSON file at `Application.persistentDataPath/nyik_calibration.json`.
6. Next session: `FBTCalibrationHelper.Start()` auto-loads the JSON. No manual step needed.

---

## Debugging

### Tracker detection

`XRInputTrackerProvider` → `Log Detected Devices` ON → Console shows every device on Play. Use the printed serials to fill Mappings.

### IK validation

`IKValidator` runs at `NYIKHumanoid.Initialize()` and prints warnings for missing bones, scale issues, etc.

### Pose preview (Edit mode)

`NYIKTestTargets` exposes Transform handles for head / hands / feet. Drag in Scene view, IK solves in real time without entering Play.

### Calibration verification

`FBTCalibrationHelper.Quick Reset (Rotation Only)` — re-syncs rotations only, useful when SlimeVR yaw drifts mid-session.

---

## Tests

`Packages/com.nystudio.nyik/Tests/Editor/` — runs via Unity Test Runner → Edit Mode.

Covers:
- `OneEuroFilter` numerical correctness (convergence, noise rejection, hemisphere flip)
- `SwingTwistDecomposition` (identity, pure swing, pure twist, recomposition)
- `TPoseValidator` (positive + 3 failure modes)
- `FBTCalibrationData` JSON round-trip

All tests are pure CPU — no Animator, no VR hardware needed.

---

## Known Limitations

- **No Grounder** — feet do not snap to ground colliders.
- **No InteractionSystem** — no grab/release with rigid bodies.
- **No procedural foot pivot** — turning the body doesn't rotate the foot naturally.
- **Per-avatar swing-twist axes are heuristic** — assumes bone local Y is the long axis. Adjust `JointROMLimits.GetSwingTwist` for atypical rigs.
- **Scale change at runtime** — call `NYIKHumanoid.ApplyOffsets()` after `transform.localScale` changes.
- **Head bone scaling for first-person hide** — requires the avatar's face mesh to be skinned to the head bone. Some avatars rig eyes/jaw to separate bones and would need extra handling.

---

## Roadmap

Short list:

- [ ] Grounder integration (raycast-based foot snap)
- [ ] Per-avatar swing-twist axis configuration (Inspector override)
- [ ] CalibrationData profiles (per-performer)
- [ ] Migrate ROM tables to ScriptableObject so anatomy can be tuned without recompile
- [ ] Performance profiling pass (LateUpdate budget under 0.3ms target on Quest 3 Link)

---

## License

MIT — see LICENSE.md
