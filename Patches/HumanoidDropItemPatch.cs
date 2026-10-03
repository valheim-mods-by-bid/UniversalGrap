using HarmonyLib;

namespace UniversalGrasp.Patches
{
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.DropItem))]
    internal static class HumanoidDropItemPatch
    {
        private static void Postfix(Humanoid __instance, Inventory inventory, bool __result)
        {
            Player player = __instance as Player;
            if (__result
                && player != null
                && player == Player.m_localPlayer
                && inventory == player.GetInventory())
            {
                HandItemController.ReconcileWithInventory(player);
            }
        }
    }
}
