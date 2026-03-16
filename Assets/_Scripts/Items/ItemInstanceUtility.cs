using Unity.Collections;

namespace HuntersAndCollectors.Items
{
    /// <summary>
    /// Shared helpers for keeping the concrete ItemInstance payload and the bridge ItemInstanceData
    /// payload in sync across crafting, inventory, persistence, storage, and world drops.
    /// </summary>
    public static class ItemInstanceUtility
    {
        public static void MirrorRuntimeFields(ref ItemInstanceData data, in Inventory.ItemInstance instance)
        {
            data.InstanceId = instance.InstanceId;
            data.RolledDamage = instance.RolledDamage;
            data.RolledDefence = instance.RolledDefence;
            data.RolledSwingSpeed = instance.RolledSwingSpeed;
            data.RolledMovementSpeed = instance.RolledMovementSpeed;
            data.RolledCastSpeed = instance.RolledCastSpeed;
            data.RolledBlockValue = instance.RolledBlockValue;
            data.MaxDurability = instance.MaxDurability;
            data.CurrentDurability = instance.CurrentDurability;
        }

        public static ItemInstanceData CreateFromInstance(in Inventory.ItemInstance instance, FixedString64Bytes craftedBy)
        {
            ItemInstanceData data = default;
            data.CraftedBy = craftedBy;
            MirrorRuntimeFields(ref data, instance);
            return data;
        }
    }
}
