using HarmonyLib;
using UnityEngine;

namespace SubmersedVR
{
    static class Sprint
    {
        public static void OnForwardSprintModifierChanged()
        {
            foreach (var playerMotor in Object.FindObjectsOfType<PlayerMotor>())
            {
                playerMotor.forwardSprintModifier = Settings.ForwardSprintModifier;
            }
        }

        [HarmonyPatch(typeof(PlayerMotor), "Start")]
        public static class PlayerMotor_ForwardSprintModifier
        {
            static void Postfix(PlayerMotor __instance)
            {
                __instance.forwardSprintModifier = Settings.ForwardSprintModifier;
            }
        }
    }
}
