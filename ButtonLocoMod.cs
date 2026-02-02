using System;
using HarmonyLib;
using MelonLoader;
using Il2CppRUMBLE.Players.Subsystems;
using UnityEngine;
using UnityEngine.XR;

[assembly: MelonInfo(typeof(ButtonLoco.ButtonLocoMod), "Stone Stick By Nano", "3.0.0", "Nano")]
[assembly: MelonGame("Buckethead Entertainment", "RUMBLE")]

namespace ButtonLoco
{
    public sealed class ButtonLocoMod : MelonMod
    {
        internal static ButtonLocoMod Instance;

        private const string HarmonyId = "nano.stonestick.buttonloco";
        private static bool _patched;

        private static Camera _cachedCamera;

        private static readonly InputFeatureUsage<bool> PrimaryButton = CommonUsages.primaryButton;
        private static readonly InputFeatureUsage<bool> SecondaryButton = CommonUsages.secondaryButton;

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

            if (__instance.IsExperiencingKnockback())
            {
                return;
            }

            __0 = Instance.GetButtonMovementVector();
        }

        private Vector2 GetButtonMovementVector()
        {
            var x = GetButton(XRNode.LeftHand, PrimaryButton);
            var y = GetButton(XRNode.LeftHand, SecondaryButton);
            var a = GetButton(XRNode.RightHand, PrimaryButton);
            var b = GetButton(XRNode.RightHand, SecondaryButton);

            var forward = 0f;
            var right = 0f;

            if (x) forward += 1f;
            if (a) forward -= 1f;
            if (b) right += 1f;
            if (y) right -= 1f;

            var input = new Vector2(right, forward);
            if (input.sqrMagnitude > 1f)
            {
                input.Normalize();
            }

            var cam = GetCamera();
            if (cam != null)
            {
                var forwardVec = cam.transform.forward;
                var rightVec = cam.transform.right;
                forwardVec.y = 0f;
                rightVec.y = 0f;

                if (forwardVec.sqrMagnitude > 0.0001f)
                {
                    forwardVec.Normalize();
                }

                if (rightVec.sqrMagnitude > 0.0001f)
                {
                    rightVec.Normalize();
                }

                var world = (forwardVec * input.y) + (rightVec * input.x);
                input = new Vector2(world.x, world.z);

                if (input.sqrMagnitude > 1f)
                {
                    input.Normalize();
                }
            }

            return input;
        }

        private static bool GetButton(XRNode node, InputFeatureUsage<bool> usage)
        {
            var device = InputDevices.GetDeviceAtXRNode(node);
            if (!device.isValid)
            {
                return false;
            }

            return device.TryGetFeatureValue(usage, out var value) && value;
        }

        private static Camera GetCamera()
        {
            if (_cachedCamera != null)
            {
                return _cachedCamera;
            }

            _cachedCamera = Camera.main;
            if (_cachedCamera == null)
            {
                _cachedCamera = UnityEngine.Object.FindObjectOfType<Camera>();
            }

            return _cachedCamera;
        }
    }
}
