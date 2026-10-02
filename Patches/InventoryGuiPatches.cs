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

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.DropItem))]
        private static class DropItemPatch
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

        private static bool TrySetHandItem(InventoryGrid grid, ItemDrop.ItemData item, bool leftHand)
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

    internal static class HandItemController
    {
        private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> LeftItem =
            AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_leftItem");

        private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> RightItem =
            AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_rightItem");

        private static readonly AccessTools.FieldRef<Humanoid, VisEquipment> VisualEquipment =
            AccessTools.FieldRefAccess<Humanoid, VisEquipment>("m_visEquipment");

        private static readonly AccessTools.FieldRef<VisEquipment, int> VisualLeftItem =
            AccessTools.FieldRefAccess<VisEquipment, int>("m_leftItem");

        private static readonly AccessTools.FieldRef<VisEquipment, int> VisualRightItem =
            AccessTools.FieldRefAccess<VisEquipment, int>("m_rightItem");

        private static readonly AccessTools.FieldRef<Character, ZSyncAnimation> Animation =
            AccessTools.FieldRefAccess<Character, ZSyncAnimation>("m_zanim");

        internal static bool IsSelectionActive()
        {
            return UniversalGraspPlugin.Enabled != null
                && UniversalGraspPlugin.Enabled.Value
                && UniversalGraspPlugin.IsHandSelectionModifierPressed();
        }

        internal static void SetHandItem(Player player, ItemDrop.ItemData item, bool leftHand)
        {
            string prefabName = item.m_dropPrefab.name;
            int prefabHash = prefabName.GetStableHashCode();
            VisEquipment visualEquipment = VisualEquipment(player);
            int ownedQuantity = CountItems(player.GetInventory(), prefabHash);

            if (leftHand)
            {
                ItemDrop.ItemData equippedItem = LeftItem(player);
                if (equippedItem != null)
                {
                    player.UnequipItem(equippedItem, false);
                }

                int nextItem = VisualLeftItem(visualEquipment) == prefabHash ? 0 : prefabHash;
                if (nextItem != 0
                    && VisualRightItem(visualEquipment) == prefabHash
                    && ownedQuantity < 2)
                {
                    ClearHand(player, visualEquipment, leftHand: false);
                }

                int quality = nextItem == 0 ? 0 : item.m_quality;
                visualEquipment.SetLeftItem(nextItem, -1, quality);
            }
            else
            {
                ItemDrop.ItemData equippedItem = RightItem(player);
                if (equippedItem != null)
                {
                    player.UnequipItem(equippedItem, false);
                }

                int nextItem = VisualRightItem(visualEquipment) == prefabHash ? 0 : prefabHash;
                if (nextItem != 0
                    && VisualLeftItem(visualEquipment) == prefabHash
                    && ownedQuantity < 2)
                {
                    ClearHand(player, visualEquipment, leftHand: true);
                }

                int quality = nextItem == 0 ? 0 : item.m_quality;
                visualEquipment.SetRightItem(nextItem, quality);
            }

            PlayEquipAnimation(player);
        }

        internal static void ReconcileWithInventory(Player player)
        {
            VisEquipment visualEquipment = VisualEquipment(player);
            int leftHash = LeftItem(player) == null ? VisualLeftItem(visualEquipment) : 0;
            int rightHash = RightItem(player) == null ? VisualRightItem(visualEquipment) : 0;
            bool changed = false;

            if (leftHash != 0 && leftHash == rightHash)
            {
                int ownedQuantity = CountItems(player.GetInventory(), leftHash);
                if (ownedQuantity < 2)
                {
                    ClearHand(player, visualEquipment, leftHand: false);
                    rightHash = 0;
                    changed = true;
                }

                if (ownedQuantity < 1)
                {
                    ClearHand(player, visualEquipment, leftHand: true);
                    leftHash = 0;
                    changed = true;
                }
            }

            if (leftHash != 0 && CountItems(player.GetInventory(), leftHash) == 0)
            {
                ClearHand(player, visualEquipment, leftHand: true);
                changed = true;
            }

            if (rightHash != 0 && CountItems(player.GetInventory(), rightHash) == 0)
            {
                ClearHand(player, visualEquipment, leftHand: false);
                changed = true;
            }

            if (changed)
            {
                PlayEquipAnimation(player);
            }
        }

        internal static bool ClearVisualOnlyItems(Player player)
        {
            VisEquipment visualEquipment = VisualEquipment(player);
            bool cleared = false;

            if (RightItem(player) == null && VisualRightItem(visualEquipment) != 0)
            {
                visualEquipment.SetRightItem(0, 0);
                cleared = true;
            }

            if (LeftItem(player) == null && VisualLeftItem(visualEquipment) != 0)
            {
                visualEquipment.SetLeftItem(0, -1, 0);
                cleared = true;
            }

            return cleared;
        }

        internal static void PlayEquipAnimation(Player player)
        {
            Animation(player).SetTrigger("equip_hip");
        }

        private static int CountItems(Inventory inventory, int prefabHash)
        {
            int quantity = 0;
            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
            {
                if (item?.m_dropPrefab != null
                    && item.m_dropPrefab.name.GetStableHashCode() == prefabHash)
                {
                    quantity += item.m_stack;
                }
            }

            return quantity;
        }

        private static void ClearHand(Player player, VisEquipment visualEquipment, bool leftHand)
        {
            ItemDrop.ItemData equippedItem = leftHand ? LeftItem(player) : RightItem(player);
            if (equippedItem != null)
            {
                player.UnequipItem(equippedItem, false);
            }

            if (leftHand)
            {
                visualEquipment.SetLeftItem(0, -1, 0);
            }
            else
            {
                visualEquipment.SetRightItem(0, 0);
            }
        }
    }
}
