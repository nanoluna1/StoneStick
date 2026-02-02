using System;
using HarmonyLib;
using MelonLoader;
using Il2CppRUMBLE.Players.Subsystems;
using UnityEngine.InputSystem;
using UnityEngine;

[assembly: MelonInfo(typeof(ButtonLoco.ButtonLocoMod), "Stone Stick By Nano", "3.0.0", "Nano")]
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

    }
}
