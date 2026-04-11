# Contributing to NYIK

Thank you for your interest in contributing to NYIK! This guide will help you
get started.

## Development Environment

- **Unity** 2022.3 LTS or newer
- **XR Interaction Toolkit** 2.5+
- **Input System** 1.7+
- **XR Core Utilities** 2.2+

## Setting Up the Test Project

1. Create a new Unity project (3D Core template).
2. Install the required packages listed above via Package Manager.
3. Clone this repository into the project's `Assets/NYIK/` folder.
4. Open a scene with an `XROrigin` and a Humanoid avatar.
5. Add the `NYIKHumanoid` component to the avatar and press Play.

## Code Style

- Follow standard C# naming conventions.
- Use the `m_` prefix for private instance fields (e.g., `m_SpineWeight`).
- Write all comments and documentation in English.
- Keep methods short and focused; prefer small helper methods over long blocks.
- Use `[SerializeField]` for inspector-exposed private fields.
- Avoid allocations in hot paths (`Update`, solver loops).

## Branching and Pull Requests

1. Fork the repository and create a feature branch from `main`:
   ```
   git checkout -b feature/my-change
   ```
2. Make your changes with clear, descriptive commits.
3. Ensure the package imports without errors in a clean Unity project.
4. Test with at least one Humanoid avatar in Play mode.
5. Open a pull request against `main` with:
   - A summary of what changed and why.
   - Steps to reproduce or test the change.
   - Any relevant screenshots or videos.

## What to Contribute

- Bug fixes with a clear reproduction case.
- Performance improvements backed by Profiler data.
- New solver features that fit the existing architecture.
- Documentation improvements or typo fixes.
- Editor tooling (inspectors, gizmos, handles).

## Reporting Issues

Open a GitHub issue with:
- Unity version and platform.
- XR Interaction Toolkit version.
- Steps to reproduce.
- Expected vs. actual behaviour.

## License

By contributing, you agree that your contributions will be licensed under
the [MIT License](LICENSE.md).
