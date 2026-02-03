using System;
using HarmonyLib;
using MelonLoader;
using Il2CppRUMBLE.Players.Subsystems;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.InputSystem;
using UnityEngine;

[assembly: MelonInfo(typeof(ButtonLoco.ButtonLocoMod), "Stone Stick By Nano", "3.0.1", "Nano")]
[assembly: MelonGame("Buckethead Entertainment", "RUMBLE")]

namespace ButtonLoco
{
    public sealed class ButtonLocoMod : MelonMod
    {
        internal static ButtonLocoMod Instance;

        private const string HarmonyId = "nano.stonestick.buttonloco";
        private static bool _patched;
        private static bool _inputFaulted;
        private static float _lastInputFaultLogTime;
        private static float _lastDebugLogTime;
        private static bool _movementMethodsLogged;
        private static MethodInfo _sprintPoseMethod;
        private static bool _sprintPosePatched;

        private static bool _inputSystemReady;
        private static InputAction _moveAction;

        public override void OnInitializeMelon()
        {
            Instance = this;
            TryPatch();
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            if (!_patched)
            {
                TryPatch();
            }
        }

        public override void OnLateInitializeMelon()
        {
            TrySetupInputSystem();
            LogMovementMethodsOnce();
            TryPatchSprintPose();
        }

        private void TryPatch()
        {
            var moveMethod = AccessTools.Method(typeof(PlayerMovement), "Move");
            if (moveMethod == null)
            {
                LoggerInstance.Error("PlayerMovement.Move not found.");
                return;
            }

            var harmony = new HarmonyLib.Harmony(HarmonyId);
            harmony.Patch(moveMethod, prefix: new HarmonyMethod(typeof(ButtonLocoMod), nameof(PlayerMovement_Move_Prefix)));
            _patched = true;
            LoggerInstance.Msg("Stone Stick: patched PlayerMovement.Move");
        }

        private void TryPatchSprintPose()
        {
            if (_sprintPosePatched)
            {
                return;
            }

            _sprintPoseMethod = AccessTools.Method(typeof(PlayerMovement), "OnSprintingPoseInputDone");
            if (_sprintPoseMethod == null)
            {
                return;
            }

            var harmony = new HarmonyLib.Harmony(HarmonyId);
            harmony.Patch(_sprintPoseMethod, postfix: new HarmonyMethod(typeof(ButtonLocoMod), nameof(PlayerMovement_OnSprintingPoseInputDone_Postfix)));
            _sprintPosePatched = true;
            LoggerInstance.Msg("Stone Stick: patched PlayerMovement.OnSprintingPoseInputDone (log only)");
        }

        private static void PlayerMovement_Move_Prefix(PlayerMovement __instance, ref Vector2 __0)
        {
            if (Instance == null)
            {
                return;
            }

            if (__instance == null)
            {
                return;
            }

            if (_inputFaulted)
            {
                __0 = Vector2.zero;
                return;
            }

            try
            {
                __0 = Instance.GetButtonMovementVector(__instance);
            }
            catch (Exception ex)
            {
                // Guard against XR input exceptions when headset is active.
                _inputFaulted = true;
                if (Time.time - _lastInputFaultLogTime > 5f)
                {
                    _lastInputFaultLogTime = Time.time;
                    Instance.LoggerInstance.Error($"Stone Stick input disabled due to exception: {ex}");
                }
                __0 = Vector2.zero;
            }
        }

        private Vector2 GetButtonMovementVector(PlayerMovement movement)
        {
            var input = ReadMoveInput();
            if (input.sqrMagnitude > 1f)
            {
                input.Normalize();
            }

            // IMPORTANT: RUMBLE already applies camera-relative movement internally.
            // Feeding raw input avoids double-rotating the direction.
            input = ApplySprintScaling(movement, input);
            DebugLog(input);
            return input;
        }

        private static void DebugLog(Vector2 finalInput)
        {
            if (Instance == null)
            {
                return;
            }

            if (finalInput.sqrMagnitude < 0.0001f)
            {
                return;
            }

            if (Time.time - _lastDebugLogTime < 2f)
            {
                return;
            }

            _lastDebugLogTime = Time.time;
            Instance.LoggerInstance.Msg(
                $"StoneStick debug | final={finalInput}");
        }

        private static void PlayerMovement_OnSprintingPoseInputDone_Postfix(PlayerMovement __instance)
        {
            if (Instance == null || __instance == null)
            {
                return;
            }

            try
            {
                var sprintSource = ReadPropertyValue(__instance, "sprintingInputSource");
                var sprintFactor = ReadPropertyValue(__instance, "currentSprintFactor");
                var activeMoveType = ReadPropertyValue(__instance, "activeMovementType");
                var moveVelTarget = ReadPropertyValue(__instance, "movementVelocityTarget");
                var desiredVel = ReadPropertyValue(__instance, "desiredMovementVelocity");
                var maxSprintLen = ReadPropertyValue(__instance, "maxSprintVectorLength");
                var sprintAccel = ReadPropertyValue(__instance, "sprintAccelerationRate");
                Instance.LoggerInstance.Msg(
                    $"StoneStick sprint log | inputSource={sprintSource ?? "n/a"} currentSprintFactor={sprintFactor ?? "n/a"} " +
                    $"activeMovementType={activeMoveType ?? "n/a"} movementVelocityTarget={moveVelTarget ?? "n/a"} " +
                    $"desiredMovementVelocity={desiredVel ?? "n/a"} maxSprintVectorLength={maxSprintLen ?? "n/a"} " +
                    $"sprintAccelerationRate={sprintAccel ?? "n/a"}");
            }
            catch (Exception ex)
            {
                Instance.LoggerInstance.Warning($"StoneStick sprint log failed: {ex.Message}");
            }
        }

        private static object ReadPropertyValue(PlayerMovement movement, string propName)
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var prop = movement.GetType().GetProperty(propName, flags);
            if (prop == null)
            {
                return null;
            }

            try
            {
                return prop.GetValue(movement);
            }
            catch
            {
                return null;
            }
        }

        private static Vector2 ApplySprintScaling(PlayerMovement movement, Vector2 input)
        {
            if (movement == null || input.sqrMagnitude < 0.0001f)
            {
                return input;
            }

            try
            {
                var sprintObj = ReadPropertyValue(movement, "currentSprintFactor");
                if (sprintObj == null)
                {
                    return input;
                }

                var sprintFactor = Convert.ToSingle(sprintObj);
                if (sprintFactor <= 1.01f)
                {
                    return input;
                }

                var maxObj = ReadPropertyValue(movement, "maxSprintVectorLength");
                if (maxObj != null)
                {
                    var maxLen = Convert.ToSingle(maxObj);
                    if (maxLen > 0f && sprintFactor > maxLen)
                    {
                        sprintFactor = maxLen;
                    }
                }

                return input * sprintFactor;
            }
            catch
            {
                return input;
            }
        }

        private Vector2 ReadMoveInput()
        {
            if (!_inputSystemReady)
            {
                TrySetupInputSystem();
            }

            if (_inputSystemReady && _moveAction != null)
            {
                try
                {
                    return _moveAction.ReadValue<Vector2>();
                }
                catch
                {
                    _inputSystemReady = false;
                }
            }

            return Vector2.zero;
        }

        private void TrySetupInputSystem()
        {
            if (_inputSystemReady)
            {
                return;
            }

            try
            {
                _moveAction = new InputAction(
                    name: "StoneStickMove",
                    type: InputActionType.Value,
                    expectedControlType: "Vector2");

                // OpenXR common usages for face buttons.
                _moveAction.AddCompositeBinding("2DVector")
                    .With("Up", "<XRController>{LeftHand}/primaryButton")
                    .With("Left", "<XRController>{LeftHand}/secondaryButton")
                    .With("Down", "<XRController>{RightHand}/primaryButton")
                    .With("Right", "<XRController>{RightHand}/secondaryButton");

                _moveAction.Enable();
                _inputSystemReady = true;
            }
            catch (Exception ex)
            {
                _inputSystemReady = false;
                LoggerInstance.Warning($"Stone Stick input system init failed: {ex.Message}");
            }
        }

        private static void LogMovementMethodsOnce()
        {
            if (_movementMethodsLogged || Instance == null)
            {
                return;
            }

            _movementMethodsLogged = true;
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var methods = typeof(PlayerMovement).GetMethods(flags);
                var hits = new List<string>();
                foreach (var method in methods)
                {
                    var name = method.Name;
                    var lower = name.ToLowerInvariant();
                    if (lower.Contains("run") || lower.Contains("sprint") || lower.Contains("dash")
                        || lower.Contains("jump") || lower.Contains("move") || lower.Contains("locom"))
                    {
                        hits.Add(name);
                    }
                }

                hits.Sort(StringComparer.Ordinal);
                if (hits.Count > 0)
                {
                    Instance.LoggerInstance.Msg("StoneStick: PlayerMovement methods of interest:");
                    foreach (var name in hits)
                    {
                        Instance.LoggerInstance.Msg($" - {name}");
                    }
                }
                else
                {
                    Instance.LoggerInstance.Msg("StoneStick: no obvious sprint/move methods found in PlayerMovement.");
                }
            }
            catch (Exception ex)
            {
                Instance.LoggerInstance.Warning($"StoneStick: method scan failed: {ex.Message}");
            }
        }

    }
}
