using HarmonyLib;

namespace RavenScout
{
    [HarmonyPatch(typeof(Container), nameof(Container.GetHoverText))]
    internal static class ContainerHoverTextPatch
    {
        private static void Postfix(Container __instance, ref string __result)
        {
            var roost = __instance.GetComponent<RavenRoost>();
            if (roost)
            {
                __result += roost.GetExtraHoverText();
            }
        }
    }

    /// <summary>Registers Raven Scout's network messages each time a world starts (host, client or dedicated server).</summary>
    [HarmonyPatch(typeof(Game), "Start")]
    internal static class GameStartPatch
    {
        private static void Postfix() => ScoutNet.Register();
    }

    /// <summary>Using a food item from the hotbar while looking at a roost feeds the raven.</summary>
    [HarmonyPatch(typeof(Container), nameof(Container.UseItem))]
    internal static class ContainerUseItemPatch
    {
        private static bool Prefix(Container __instance, Humanoid user, ItemDrop.ItemData item, ref bool __result)
        {
            var roost = __instance.GetComponent<RavenRoost>();
            if (!roost || !(user is Player player))
            {
                return true;
            }
            __result = roost.Feed(player, item);
            return false;
        }
    }

    /// <summary>Shows a Scout Report's text in its tooltip.</summary>
    [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip),
        new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int), typeof(bool) })]
    internal static class ReportTooltipPatch
    {
        private static void Postfix(ItemDrop.ItemData item, ref string __result)
        {
            if (ScoutReportItem.IsReport(item))
            {
                __result += ScoutReportItem.Tooltip(item);
            }
        }
    }

    /// <summary>Using a Scout Report (hotbar key or right-click) asks its feather for the way.</summary>
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UseItem))]
    internal static class ReportUsePatch
    {
        private static bool Prefix(Humanoid __instance, Inventory inventory, ItemDrop.ItemData item)
        {
            if (!ScoutReportItem.IsReport(item) || !(__instance is Player player) || player != Player.m_localPlayer)
            {
                return true;
            }
            // Only reports the player is carrying; one in an open chest should be taken out first.
            if (inventory != null && inventory != player.GetInventory())
            {
                return true;
            }
            FeatherGuide.Use(player, item);
            return false;
        }
    }

    /// <summary>Don't let the player deconstruct a roost while its raven is out.</summary>
    [HarmonyPatch(typeof(Container), nameof(Container.CanBeRemoved))]
    internal static class ContainerCanBeRemovedPatch
    {
        private static void Postfix(Container __instance, ref bool __result)
        {
            if (!__result)
            {
                return;
            }
            var roost = __instance.GetComponent<RavenRoost>();
            if (roost && roost.IsAway)
            {
                __result = false;
            }
        }
    }
}
