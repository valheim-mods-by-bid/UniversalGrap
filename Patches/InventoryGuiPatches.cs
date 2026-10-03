using HarmonyLib;

namespace UniversalGrasp.Patches
{
    internal static class InventoryGuiPatches
    {
        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnSelectedItem))]
        private static class OnSelectedItemPatch
        {
            private static bool Prefix(InventoryGrid __0, ItemDrop.ItemData __1)
            {
                return !TrySetHandItem(__0, __1, leftHand: true);
            }
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnRightClickItem))]
        private static class OnRightClickItemPatch
        {
            private static bool Prefix(InventoryGrid __0, ItemDrop.ItemData __1)
            {
                return !TrySetHandItem(__0, __1, leftHand: false);
            }
        }

        private static bool TrySetHandItem(
            InventoryGrid grid,
            ItemDrop.ItemData item,
            bool leftHand)
        {
            if (!HandItemController.IsSelectionActive())
            {
                return false;
            }

            Player player = Player.m_localPlayer;
            if (player == null || grid == null || item == null || item.m_dropPrefab == null)
            {
                return false;
            }

            if (grid.GetInventory() != player.GetInventory())
            {
                return false;
            }

            HandItemController.SetHandItem(player, item, leftHand);
            return true;
        }
    }
}
