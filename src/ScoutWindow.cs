using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace RavenScout
{
    /// <summary>
    /// A wood panel shown to the right of the roost's slots while it is open, with two tabs: Scout (pick a
    /// compass direction and a distance, feed and send the raven, last report) and Roost (tier and upgrade).
    /// </summary>
    internal static class ScoutWindow
    {
        private const float Width = 320f;
        private const float Height = 660f;
        private const int CostLines = 4;

        private enum Tab { Scout, Roost }

        private static readonly FieldInfo s_currentContainer = AccessTools.Field(typeof(InventoryGui), "m_currentContainer");

        private static GameObject s_panel;
        private static Text s_title;
        private static Button s_scoutTab;
        private static Button s_roostTab;

        // Scout tab
        private static GameObject s_scoutView;
        private static Text s_status;
        private static Text s_direction;
        private static Text s_distance;
        private static Text s_info;
        private static Button s_feed;
        private static Button s_send;
        private static Text s_report;
        private static Button s_copy;

        // Roost tab
        private static GameObject s_roostView;
        private static Text s_tierHeader;
        private static Text s_tierStats;
        private static Text s_nextHeader;
        private static readonly List<Text> s_costLines = new List<Text>();
        private static Button s_upgrade;

        private static RavenRoost s_roost;
        private static Tab s_tab = Tab.Scout;
        private static float s_nextRefresh;

        private static readonly Color Gold = new Color(1f, 0.83f, 0.35f);
        private static readonly Color Plain = new Color(0.9f, 0.9f, 0.85f);
        private static readonly Color Faded = new Color(0.65f, 0.63f, 0.6f);

        public static void Tick()
        {
            var roost = OpenRoost();
            if (!roost)
            {
                if (s_panel && s_panel.activeSelf)
                {
                    s_panel.SetActive(false);
                }
                s_roost = null;
                return;
            }

            // Logging out destroys the game's UI along with our panel; Build() then makes a fresh one.
            if (!s_panel && !Build())
            {
                return;
            }
            if (roost != s_roost)
            {
                s_roost = roost;
                s_tab = Tab.Scout;
                s_nextRefresh = 0f;
            }
            if (!s_panel.activeSelf)
            {
                s_panel.SetActive(true);
            }
            if (Time.time >= s_nextRefresh)
            {
                Refresh();
            }
        }

        private static RavenRoost OpenRoost()
        {
            var gui = InventoryGui.instance;
            if (!gui || !InventoryGui.IsVisible() || !Scouting.IsActive)
            {
                return null;
            }
            var container = s_currentContainer.GetValue(gui) as Container;
            return container ? container.GetComponent<RavenRoost>() : null;
        }

        // ---------------------------------------------------------------- building

        private static bool Build()
        {
            var gui = InventoryGui.instance;
            if (!gui || !gui.m_container || GUIManager.Instance == null || !GUIManager.Instance.AveriaSerifBold)
            {
                return false;
            }
            // The old panel's controls were destroyed with it.
            s_costLines.Clear();
            s_roost = null;

            s_panel = GUIManager.Instance.CreateWoodpanel(gui.m_container, new Vector2(1f, 1f), new Vector2(1f, 1f),
                Vector2.zero, Width, Height, false);
            s_panel.name = "RavenScout_Window";
            var rect = (RectTransform)s_panel.transform;
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(12f, 0f);

            var root = s_panel.transform;
            s_title = AddText(root, "Raven Roost", -30f, 22, Gold, 36f);
            s_scoutTab = AddButton(root, "Scout", new Vector2(-62f, -68f), 116f, () => SwitchTab(Tab.Scout));
            s_roostTab = AddButton(root, "Roost", new Vector2(62f, -68f), 116f, () => SwitchTab(Tab.Roost));

            BuildScoutView();
            BuildRoostView();

            s_panel.SetActive(false);
            return true;
        }

        private static GameObject AddView(string name)
        {
            var view = new GameObject(name, typeof(RectTransform));
            view.transform.SetParent(s_panel.transform, false);
            var r = (RectTransform)view.transform;
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero;
            r.offsetMax = Vector2.zero;
            return view;
        }

        private static void BuildScoutView()
        {
            s_scoutView = AddView("ScoutView");
            var t = s_scoutView.transform;
            const float top = -40f; // below the tabs

            s_status = AddText(t, "", top - 64f, 15, Plain, 40f);
            s_status.horizontalOverflow = HorizontalWrapMode.Wrap;

            s_direction = AddText(t, "", top - 100f, 17, Gold, 28f);
            AddCompassButton(t, "NW", 315f, -80f, top - 136f);
            AddCompassButton(t, "N", 0f, 0f, top - 136f);
            AddCompassButton(t, "NE", 45f, 80f, top - 136f);
            AddCompassButton(t, "W", 270f, -80f, top - 176f);
            AddCompassButton(t, "E", 90f, 80f, top - 176f);
            AddCompassButton(t, "SW", 225f, -80f, top - 216f);
            AddCompassButton(t, "S", 180f, 0f, top - 216f);
            AddCompassButton(t, "SE", 135f, 80f, top - 216f);
            AddButton(t, "-15°", new Vector2(-45f, top - 256f), 80f, () => Turn(-15f));
            AddButton(t, "+15°", new Vector2(45f, top - 256f), 80f, () => Turn(15f));

            s_distance = AddText(t, "", top - 296f, 17, Gold, 28f);
            AddButton(t, "-500", new Vector2(-116f, top - 332f), 54f, () => Stretch(-500f));
            AddButton(t, "-100", new Vector2(-58f, top - 332f), 54f, () => Stretch(-100f));
            AddButton(t, "+100", new Vector2(0f, top - 332f), 54f, () => Stretch(100f));
            AddButton(t, "+500", new Vector2(58f, top - 332f), 54f, () => Stretch(500f));
            AddButton(t, "Max", new Vector2(116f, top - 332f), 54f, () => Stretch(100000f));

            s_info = AddText(t, "", top - 380f, 15, Plain, 52f);
            s_info.horizontalOverflow = HorizontalWrapMode.Wrap;
            s_feed = AddButton(t, "Feed", new Vector2(-72f, top - 428f), 130f, OnFeedClicked);
            s_send = AddButton(t, "Send raven", new Vector2(72f, top - 428f), 130f, OnSendClicked);

            AddText(t, "Last report", top - 468f, 16, Gold, 26f).alignment = TextAnchor.MiddleLeft;
            s_copy = AddButton(t, "Copy", new Vector2(8f, top - 468f), 76f, OnCopyClicked, 28f);
            AddButton(t, "Clear food", new Vector2(100f, top - 468f), 104f, OnClearFoodPinsClicked, 28f);
            s_report = AddText(t, "", top - 540f, 14, Plain, 112f);
            s_report.alignment = TextAnchor.UpperLeft;
            s_report.horizontalOverflow = HorizontalWrapMode.Wrap;
            s_report.verticalOverflow = VerticalWrapMode.Truncate;
        }

        private static void BuildRoostView()
        {
            s_roostView = AddView("RoostView");
            var t = s_roostView.transform;
            s_tierHeader = AddText(t, "", -122f, 20, Gold, 30f);
            s_tierStats = AddText(t, "", -190f, 15, Plain, 100f);
            s_tierStats.horizontalOverflow = HorizontalWrapMode.Wrap;

            s_nextHeader = AddText(t, "", -290f, 16, Gold, 50f);
            s_nextHeader.horizontalOverflow = HorizontalWrapMode.Wrap;
            float y = -330f;
            for (int i = 0; i < CostLines; i++)
            {
                s_costLines.Add(AddText(t, "", y, 15, Plain, 24f));
                y -= 24f;
            }
            s_upgrade = AddButton(t, "Upgrade", new Vector2(0f, -460f), 160f, OnUpgradeClicked);
        }

        private static void AddCompassButton(Transform parent, string label, float degrees, float x, float y) =>
            AddButton(parent, label, new Vector2(x, y), 70f, () => { if (s_roost) { s_roost.SetHeading(degrees); Refresh(); } });

        private static Text AddText(Transform parent, string value, float y, int size, Color color, float height)
        {
            var go = GUIManager.Instance.CreateText(value, parent, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, y), GUIManager.Instance.AveriaSerifBold, size, color, true, Color.black, Width - 40f, height, false);
            var text = go.GetComponent<Text>();
            text.alignment = TextAnchor.MiddleCenter;
            return text;
        }

        private static Button AddButton(Transform parent, string label, Vector2 pos, float width, UnityAction onClick, float height = 34f)
        {
            var go = GUIManager.Instance.CreateButton(label, parent, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                pos, width, height);
            var button = go.GetComponent<Button>();
            button.onClick.AddListener(onClick);
            return button;
        }

        private static void SetLabel(Button button, string label)
        {
            var text = button.GetComponentInChildren<Text>();
            if (text)
            {
                text.text = label;
            }
        }

        private static void SwitchTab(Tab tab)
        {
            s_tab = tab;
            Refresh();
        }

        // ---------------------------------------------------------------- refreshing

        private static void Refresh()
        {
            s_nextRefresh = Time.time + 0.5f;
            if (!s_roost)
            {
                return;
            }

            s_title.text = s_roost.RoostName;
            SetLabel(s_scoutTab, s_tab == Tab.Scout ? "> Scout" : "Scout");
            SetLabel(s_roostTab, s_tab == Tab.Roost ? "> Roost" : "Roost");
            s_scoutView.SetActive(s_tab == Tab.Scout);
            s_roostView.SetActive(s_tab == Tab.Roost);
            if (s_tab == Tab.Scout)
            {
                RefreshScout();
            }
            else
            {
                RefreshRoost();
            }
        }

        private static void RefreshScout()
        {
            var trip = s_roost.Trip;
            double now = Scouting.Now;
            bool away = trip != null && trip.IsAway(now);
            if (away)
            {
                string dir = Scouting.CompassName(Scouting.Heading(trip.From, trip.Target));
                s_status.text = $"<color=#ff9a40>Out scouting {Scouting.FormatDistance(trip.Distance)} {dir}</color> - back in {Scouting.FormatDuration(trip.ReturnAt - now)}";
            }
            else if (trip != null && !trip.Reported)
            {
                s_status.text = $"<color=yellow>Back - its news is for {trip.Sender}</color>";
            }
            else
            {
                s_status.text = "Ready to scout";
            }

            float heading = s_roost.Heading;
            s_direction.text = $"Direction: {heading:0}° {Scouting.CompassName(heading)}";

            float max = s_roost.MaxRange;
            float planned = Scouting.DistanceXZ(s_roost.transform.position, s_roost.PlannedTarget);
            s_distance.text = $"Distance: {Scouting.FormatDistance(planned)}  <color=#a8a49a>(max {Scouting.FormatDistance(max)})</color>";

            string info = $"Trip: {Scouting.FormatDuration(s_roost.PlannedSeconds)} there and back";
            if (Plugin.UseFood.Value)
            {
                info += $"\n{s_roost.MealsStatus}";
            }
            if (RavenRoost.TripsLimited)
            {
                info += $"\n{s_roost.TripsStatus}";
            }
            string brings = Brings();
            if (brings.Length > 0)
            {
                info += $"\n<color=#a8a49a>{brings}</color>";
            }
            s_info.text = info;

            s_feed.gameObject.SetActive(Plugin.UseFood.Value);
            s_send.interactable = !away && !s_roost.RefusesToFly && s_roost.TripsLeft > 0;
            SetLabel(s_send, away ? "Raven away" : "Send raven");

            string report = s_roost.LastReport;
            s_report.text = string.IsNullOrEmpty(report) ? "<color=#a8a49a>No trips yet.</color>" : report;
            s_copy.gameObject.SetActive(Plugin.ShareReports.Value);
            s_copy.interactable = s_roost.HasReportToCopy;
        }

        private static void RefreshRoost()
        {
            var tier = s_roost.Tier;
            s_tierHeader.text = tier.Name;
            s_tierStats.text = $"{tier.Biome}\nRange x{tier.RangeMultiplier:0.##}  ({Scouting.FormatDistance(s_roost.MaxRange)} with the next meal)\n" +
                               $"Speed x{tier.SpeedMultiplier:0.##}   Sight x{tier.SightMultiplier:0.##}\n{Sees(tier.Index)}";

            var next = s_roost.NextTier;
            if (next == null)
            {
                s_nextHeader.text = "Highest tier reached";
                foreach (var line in s_costLines)
                {
                    line.text = "";
                }
                s_upgrade.gameObject.SetActive(false);
                return;
            }

            s_nextHeader.text = $"Next: {next.Name}\n<color=#a8a49a>range x{next.RangeMultiplier:0.##}, speed x{next.SpeedMultiplier:0.##}" +
                                $"{NewSight(tier, next)}</color>";
            var costs = next.Requirements();
            var inventory = Player.m_localPlayer ? Player.m_localPlayer.GetInventory() : null;
            bool canAfford = Costs.Missing(costs).Count == 0;
            for (int i = 0; i < s_costLines.Count; i++)
            {
                if (i >= costs.Count)
                {
                    s_costLines[i].text = "";
                    continue;
                }
                var data = Costs.ItemData(costs[i].Prefab);
                int have = data != null && inventory != null ? inventory.CountItems(data.m_shared.m_name) : 0;
                bool enough = data != null && have >= costs[i].Amount;
                canAfford &= enough;
                string color = data == null ? "#ff6060" : enough ? "#a0e080" : "#ff9a40";
                s_costLines[i].text = data == null
                    ? $"<color={color}>{costs[i].Prefab} (not in this game - see config)</color>"
                    : $"<color={color}>{Costs.DisplayName(costs[i].Prefab)}: {have}/{costs[i].Amount}</color>";
            }
            s_upgrade.gameObject.SetActive(true);
            s_upgrade.interactable = canAfford;
        }

        /// <summary>What a raven from this tier spots, beyond places.</summary>
        private static string Sees(int tier)
        {
            var extra = new List<string>();
            if (RoostTiers.SpotsOre(tier))
            {
                extra.Add("ore");
            }
            if (RoostTiers.SpotsFood(tier))
            {
                extra.Add("wild food");
            }
            return extra.Count == 0 ? "Spots places" : $"Spots places and {string.Join(" and ", extra)}";
        }

        private static string NewSight(RoostTier now, RoostTier next)
        {
            var news = new List<string>();
            if (!RoostTiers.SpotsOre(now.Index) && RoostTiers.SpotsOre(next.Index))
            {
                news.Add("spots ore");
            }
            if (!RoostTiers.SpotsFood(now.Index) && RoostTiers.SpotsFood(next.Index))
            {
                news.Add("spots wild food");
            }
            if (next.SightMultiplier > now.SightMultiplier + 0.001f)
            {
                news.Add("sees wider");
            }
            return news.Count == 0 ? "" : ", " + string.Join(", ", news);
        }

        /// <summary>What the raven will bring back, when that isn't simply map pins.</summary>
        private static string Brings()
        {
            if (Scouting.WorldHasNoMap)
            {
                return "No-map world: it brings a Scout Report";
            }
            switch (Plugin.NoMapMode.Value)
            {
                case Plugin.NoMapBehaviour.ReportOnly:
                    return "It brings a Scout Report (no map changes)";
                case Plugin.NoMapBehaviour.Both:
                    return "Map pins and a Scout Report";
                default:
                    return Game.m_noMap && Plugin.NoMapMode.Value == Plugin.NoMapBehaviour.Auto ? "Your map is off: it brings a Scout Report" : "";
            }
        }

        // ---------------------------------------------------------------- buttons

        private static void Turn(float degrees)
        {
            if (s_roost)
            {
                s_roost.SetHeading(s_roost.Heading + degrees);
                Refresh();
            }
        }

        private static void Stretch(float metres)
        {
            if (s_roost)
            {
                s_roost.SetDistance(s_roost.PlannedDistance + metres);
                Refresh();
            }
        }

        private static void OnFeedClicked()
        {
            var player = Player.m_localPlayer;
            if (s_roost && player)
            {
                s_roost.FeedBest(player);
                Refresh();
            }
        }

        private static void OnCopyClicked()
        {
            var player = Player.m_localPlayer;
            if (s_roost && player)
            {
                s_roost.CopyReport(player);
            }
        }

        private static void OnClearFoodPinsClicked()
        {
            var player = Player.m_localPlayer;
            if (player)
            {
                int removed = ScoutMap.ClearFoodPins();
                player.Message(MessageHud.MessageType.Center,
                    removed == 0 ? "No food pins on your map" : $"Removed {removed} food pin{(removed == 1 ? "" : "s")} from your map");
            }
        }

        private static void OnSendClicked()
        {
            var player = Player.m_localPlayer;
            if (s_roost && player)
            {
                s_roost.Send(player);
                Refresh();
            }
        }

        private static void OnUpgradeClicked()
        {
            var player = Player.m_localPlayer;
            if (s_roost && player)
            {
                s_roost.Upgrade(player);
                Refresh();
            }
        }
    }
}
