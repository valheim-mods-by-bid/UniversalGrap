using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System.Linq;
using UniversalGrasp.Patches;
using UnityEngine;

namespace UniversalGrasp
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    internal sealed class UniversalGraspPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "org.bepinex.plugins.bid.universalgrasp";
        public const string PluginName = "UniversalGrasp";
        public const string PluginVersion = "0.3.0";
        internal static BepInEx.Logging.ManualLogSource Log { get; private set; }

        internal static ConfigEntry<bool> Enabled { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> HandSelectionModifier { get; private set; }

        private readonly Harmony harmony = new Harmony(PluginGuid);

        private void Awake()
        {
            Log = Logger;
            Enabled = Config.Bind(
                "Client config",
                "enabled",
                true,
                "Enables modifier-click hand selection in the player inventory.");

            HandSelectionModifier = Config.Bind(
                "Client config",
                "Hand selection modifier",
                new KeyboardShortcut(KeyCode.LeftAlt),
                "Key held while clicking an inventory item to put it into a hand.");

            harmony.PatchAll();
            Logger.LogInfo($"{PluginName} {PluginVersion} loaded.");
        }

        internal static GameObject GetAttachObject(GameObject item)
        {
            GameObject attach = item.transform.Find("attach")?.gameObject;
            if (attach != null && attach.GetComponentsInChildren<Renderer>(true).Any())
            {
                return attach;
            }

            GameObject candidate = null;
            bool multipleCandidates = false;
            foreach (Transform child in item.transform)
            {
                if (child.gameObject.layer != item.layer) continue;
                if (candidate != null)
                {
                    multipleCandidates = true;
                    break;
                }

                candidate = child.gameObject;
            }

            if (!multipleCandidates && candidate != null) return candidate;

            candidate = null;
            multipleCandidates = false;
            foreach (Transform child in item.transform)
            {
                if (candidate != null)
                {
                    multipleCandidates = true;
                    break;
                }

                candidate = child.gameObject;
            }

            if (!multipleCandidates && candidate != null) return candidate;

            Log?.LogDebug($"Using prefab root as attach source: prefab={item.name}.");
            return item;
        }

        private void OnDestroy()
        {
            harmony.UnpatchSelf();
        }

        internal static bool IsHandSelectionModifierPressed()
        {
            return HandSelectionModifier != null && HandSelectionModifier.Value.IsPressed();
        }

        private void Update()
        {
            Player player = Player.m_localPlayer;
            if (ZInput.instance == null || !ZInput.GetButtonDown("Hide"))
            {
                return;
            }

            if (player != null && HandItemController.ClearVisualOnlyItems(player))
            {
                HandItemController.PlayEquipAnimation(player);
            }
        }
    }
}
