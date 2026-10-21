using System.Collections.Generic;
using System.Linq;

namespace RavenScout
{
    /// <summary>An item and how many of it, from a config cost like "Bronze:5,BronzeNails:10".</summary>
    internal class Cost
    {
        public string Prefab;
        public int Amount;
    }

    /// <summary>Reading item costs from the config and paying them from a player's inventory.</summary>
    internal static class Costs
    {
        public static List<Cost> Parse(string text)
        {
            var list = new List<Cost>();
            foreach (var part in (text ?? "").Split(','))
            {
                var bits = part.Split(':');
                if (bits.Length == 2 && bits[0].Trim().Length > 0 && int.TryParse(bits[1].Trim(), out int amount) && amount > 0)
                {
                    list.Add(new Cost { Prefab = bits[0].Trim(), Amount = amount });
                }
            }
            return list;
        }

        public static ItemDrop.ItemData ItemData(string prefabName)
        {
            if (!ObjectDB.instance)
            {
                return null;
            }
            var prefab = ObjectDB.instance.GetItemPrefab(prefabName);
            var drop = prefab ? prefab.GetComponent<ItemDrop>() : null;
            return drop ? drop.m_itemData : null;
        }

        public static string DisplayName(string prefabName)
        {
            var data = ItemData(prefabName);
            return data != null ? Localization.instance.Localize(data.m_shared.m_name) : prefabName;
        }

        /// <summary>Cost items this game version doesn't have.</summary>
        public static List<string> Missing(List<Cost> costs) => costs.Where(c => ItemData(c.Prefab) == null).Select(c => c.Prefab).ToList();

        /// <summary>"5 Bronze, 10 Bronze Nails".</summary>
        public static string Describe(List<Cost> costs) =>
            costs.Count == 0 ? "free" : string.Join(", ", costs.Select(c => $"{c.Amount} {DisplayName(c.Prefab)}"));

        /// <summary>
        /// Takes the cost from the player's inventory if they carry all of it. Otherwise tells them what's
        /// missing and takes nothing.
        /// </summary>
        public static bool TryPay(Player player, List<Cost> costs)
        {
            var missing = Missing(costs);
            if (missing.Count > 0)
            {
                player.Message(MessageHud.MessageType.Center, $"Needs items this game doesn't have ({string.Join(", ", missing)}) - see the config");
                return false;
            }
            var inventory = player.GetInventory();
            foreach (var c in costs)
            {
                var data = ItemData(c.Prefab);
                if (inventory.CountItems(data.m_shared.m_name) < c.Amount)
                {
                    player.Message(MessageHud.MessageType.Center, $"Not enough {DisplayName(c.Prefab)} ({c.Amount} needed)");
                    return false;
                }
            }
            foreach (var c in costs)
            {
                inventory.RemoveItem(ItemData(c.Prefab).m_shared.m_name, c.Amount);
            }
            return true;
        }
    }
}
