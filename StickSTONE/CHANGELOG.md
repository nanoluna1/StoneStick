# Changelog

## 2.1.1
- Reworked locomotion to be physics-rigidbody driven with XR/rig hard-lock to eliminate joystick drift.
- Camera/Headset yaw-only movement to fix vertical drift on left/right/back inputs.
- Preserve VR-to-physics offset when syncing positions to prevent boundary/collider misalignment.
- Auto-detect walk/run speeds from game components; sprint multiplier derives from native run/walk ratio.
- Relaxed sprint pose detection for easier activation.
- Reduced debug spam (caps repeating debug logs to a couple of entries per session).
- Improved rig readiness checks and controller/headset discovery.

## 2.0.3
- Swapped all MelonLogger calls to LoggerInstance.

## 2.0.2
- Added sprinting support.
