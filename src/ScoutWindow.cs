using System.Reflection;
using HarmonyLib;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace RavenScout
{
    /// <summary>
    /// A wood panel shown to the right of the roost's slots while it is open: pick a compass direction and a
    /// distance, feed the raven and send it. Shows the last trip's report.
    /// </summary>
    internal static class ScoutWindow
    {
        private const float Width = 320f;
        private const float Height = 620f;

        private static readonly FieldInfo s_currentContainer = AccessTools.Field(typeof(InventoryGui), "m_currentContainer");

        private static GameObject s_panel;
        private static Text s_title;
        private static Text s_status;
        private static Text s_direction;
        private static Text s_distance;
        private static Text s_info;
        private static Button s_feed;
        private static Button s_send;
        private static Text s_report;

        private static RavenRoost s_roost;
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
            s_roost = null;

            s_panel = GUIManager.Instance.CreateWoodpanel(gui.m_container, new Vector2(1f, 1f), new Vector2(1f, 1f),
                Vector2.zero, Width, Height, false);
            s_panel.name = "RavenScout_Window";
            var rect = (RectTransform)s_panel.transform;
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(12f, 0f);

            var t = s_panel.transform;
            s_title = AddText(t, "Raven Roost", -30f, 22, Gold, 36f);
            s_status = AddText(t, "", -64f, 15, Plain, 40f);
            s_status.horizontalOverflow = HorizontalWrapMode.Wrap;

            s_direction = AddText(t, "", -100f, 17, Gold, 28f);
            AddCompassButton(t, "NW", 315f, -80f, -136f);
            AddCompassButton(t, "N", 0f, 0f, -136f);
            AddCompassButton(t, "NE", 45f, 80f, -136f);
            AddCompassButton(t, "W", 270f, -80f, -176f);
            AddCompassButton(t, "E", 90f, 80f, -176f);
            AddCompassButton(t, "SW", 225f, -80f, -216f);
            AddCompassButton(t, "S", 180f, 0f, -216f);
            AddCompassButton(t, "SE", 135f, 80f, -216f);
            AddButton(t, "-15°", new Vector2(-45f, -256f), 80f, () => Turn(-15f));
            AddButton(t, "+15°", new Vector2(45f, -256f), 80f, () => Turn(15f));

            s_distance = AddText(t, "", -296f, 17, Gold, 28f);
            AddButton(t, "-500", new Vector2(-116f, -332f), 54f, () => Stretch(-500f));
            AddButton(t, "-100", new Vector2(-58f, -332f), 54f, () => Stretch(-100f));
            AddButton(t, "+100", new Vector2(0f, -332f), 54f, () => Stretch(100f));
            AddButton(t, "+500", new Vector2(58f, -332f), 54f, () => Stretch(500f));
            AddButton(t, "Max", new Vector2(116f, -332f), 54f, () => Stretch(100000f));

            s_info = AddText(t, "", -380f, 15, Plain, 52f);
            s_info.horizontalOverflow = HorizontalWrapMode.Wrap;
            s_feed = AddButton(t, "Feed", new Vector2(-72f, -428f), 130f, OnFeedClicked);
            s_send = AddButton(t, "Send raven", new Vector2(72f, -428f), 130f, OnSendClicked);

            AddText(t, "Last report", -468f, 16, Gold, 26f).alignment = TextAnchor.MiddleLeft;
            AddButton(t, "Clear food pins", new Vector2(70f, -468f), 140f, OnClearFoodPinsClicked, 28f);
            s_report = AddText(t, "", -540f, 14, Plain, 112f);
            s_report.alignment = TextAnchor.UpperLeft;
            s_report.horizontalOverflow = HorizontalWrapMode.Wrap;
            s_report.verticalOverflow = VerticalWrapMode.Truncate;

            s_panel.SetActive(false);
            return true;
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

        // ---------------------------------------------------------------- refreshing

        private static void Refresh()
        {
            s_nextRefresh = Time.time + 0.5f;
            if (!s_roost)
            {
                return;
            }

            s_title.text = s_roost.RoostName;
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
    }
}
