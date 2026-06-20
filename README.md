# RAXY Movement

Character movement core for Unity projects using `CharacterController`.

## Features

- **MovementController** — horizontal velocity smoothing, gravity, slope/steep/airborne resolve, impulse system, rotation helpers
- **GroundChecker** — sphere/ray ground detection, ground type (Flat/Slope/Steep), events, force-unground support

## Setup

1. Add `CharacterController` to your unit GameObject.
2. Add `GroundChecker` and `MovementController` (MovementController requires both).
3. Configure ground layer masks on `GroundChecker`.
4. Drive movement by calling `Set_HorizontalVelocity()` from your input/controller layer.
5. Extend `MovementController` in your game project for unit-specific behavior (dash, jump, speed modifiers, etc.).

## Dependencies

- **RAXY Utility** (`com.raxy.utility`) — gizmo helpers for ground debug drawing
- **UniTask** (`com.cysharp.unitask`) — async force-unground
- **Odin Inspector** (project plugin) — editor attributes; runtime works without Odin if attributes are stripped

## Notes

Input controllers, unit state machines, and combat movement extensions should live in your game project, not in this package.
