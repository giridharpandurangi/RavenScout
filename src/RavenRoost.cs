using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace RavenScout
{
    /// <summary>
    /// Added to the roost prefab next to its Container. Feeds and sends the scouting raven, and puts the
    /// Scout Reports it brings back into the box.
    /// </summary>
    public class RavenRoost : MonoBehaviour, TextReceiver
    {
        private const string TripsLogKey = "ravenscout_trips"; // "<day>:<trips started>"
        private const float LandingViewDistance = 80f;

        private ZNetView _nview;
        private Container _container;
        private static readonly HashSet<string> s_landingsShown = new HashSet<string>();

        /// <summary>Where the player wants to send the raven next (compass degrees, 0 = north, and metres).</summary>
        public float Heading { get; private set; }
        public float Distance { get; private set; } = 1000f;

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            _container = GetComponent<Container>();
            if (!_nview || _nview.GetZDO() == null)
            {
                return; // placement ghost
            }
            InvokeRepeating(nameof(UpdateSeat), 0.5f, 0.5f);
            InvokeRepeating(nameof(DeliverReports), 1f, 2f);
            RoostTiers.ValidateItems();
        }

        // ------------------------------------------------------------ tiers

        public int TierIndex => Zdo != null ? Mathf.Clamp(Zdo.GetInt(RoostTiers.TierKey), 0, RoostTiers.Max) : 0;

        internal RoostTier Tier => RoostTiers.Get(TierIndex);

        internal RoostTier NextTier => TierIndex < RoostTiers.Max ? RoostTiers.Get(TierIndex + 1) : null;

        /// <summary>Upgrades to the next tier, taking the materials from the player's inventory.</summary>
        public void Upgrade(Player player)
        {
            var next = NextTier;
            if (Zdo == null || !player || next == null || !EnsureOwner(player))
            {
                return;
            }
            if (!Costs.TryPay(player, next.Requirements()))
            {
                return;
            }
            Zdo.Set(RoostTiers.TierKey, next.Index);
            player.Message(MessageHud.MessageType.Center,
                $"Upgraded to {next.Name}: range x{next.RangeMultiplier:0.##}, speed x{next.SpeedMultiplier:0.##}");
            Plugin.Log.LogInfo($"'{RoostName}' upgraded to {next.Name}");
        }

        private ZDO Zdo => _nview && _nview.IsValid() ? _nview.GetZDO() : null;

        internal ScoutTrip Trip => Zdo != null ? Scouting.ReadTrip(Zdo) : null;

        public bool IsAway
        {
            get
            {
                var trip = Trip;
                return trip != null && Scouting.IsActive && trip.IsAway(Scouting.Now);
            }
        }

        public string LastReport => Zdo?.GetString(Scouting.ReportKey) ?? "";

        public string RoostName => Zdo != null ? Scouting.RoostName(Zdo) : Scouting.DefaultRoostName;

        // TextReceiver (rename dialog)
        public string GetText() => Zdo?.GetString(Scouting.NameKey) ?? "";

        public void SetText(string text)
        {
            if (Zdo == null || !EnsureOwner(Player.m_localPlayer))
            {
                return;
            }
            Zdo.Set(Scouting.NameKey, (text ?? "").Trim());
        }

        // ------------------------------------------------------------ Scout Reports

        public bool HasReportToCopy => Zdo != null && !string.IsNullOrEmpty(Zdo.GetString(Scouting.LastTextKey));

        /// <summary>Gives the player a copy of this roost's last report, for the cost in the config.</summary>
        public void CopyReport(Player player)
        {
            if (Zdo == null || !player || !Plugin.ShareReports.Value)
            {
                return;
            }
            string text = Zdo.GetString(Scouting.LastTextKey);
            if (string.IsNullOrEmpty(text))
            {
                player.Message(MessageHud.MessageType.Center, "No report to copy yet - send the raven first");
                return;
            }
            var item = ScoutReportItem.Create(text, RoostName, Zdo.GetInt(Scouting.LastDayKey), Zdo.GetString(Scouting.LastGuideKey));
            var inventory = player.GetInventory();
            if (item == null || !inventory.CanAddItem(item))
            {
                player.Message(MessageHud.MessageType.Center, "Your bag is full");
                return;
            }
            if (!Costs.TryPay(player, Costs.Parse(Plugin.CopyCost.Value)))
            {
                return;
            }
            inventory.AddItem(item);
            player.Message(MessageHud.MessageType.Center, "A copy of the report is in your bag");
        }

        /// <summary>Reports the server has written for this roost that don't fit in the box yet.</summary>
        public int PendingReports => Zdo != null ? Scouting.ReadPending(Zdo).Count : 0;

        /// <summary>Turns waiting reports into Scout Report items in the box. Only the roost's owner does this.</summary>
        private void DeliverReports()
        {
            if (Zdo == null || !_nview.IsOwner() || !Scouting.IsActive || _container.GetInventory() == null)
            {
                return;
            }
            var pending = Scouting.ReadPending(Zdo);
            if (pending.Count == 0)
            {
                return;
            }
            var inventory = _container.GetInventory();
            var left = new List<PendingReport>();
            foreach (var report in pending)
            {
                var item = ScoutReportItem.Create(report.Text, report.Roost, report.Day, report.Guide);
                if (item == null || !inventory.CanAddItem(item) || !inventory.AddItem(item))
                {
                    left.Add(report); // box full: wait for room
                }
            }
            if (left.Count != pending.Count)
            {
                Scouting.WritePending(Zdo, left);
            }
        }

        // ------------------------------------------------------------ choosing where to scout

        public void SetHeading(float degrees) => Heading = Mathf.Repeat(Mathf.Round(degrees), 360f);

        public void SetDistance(float metres) =>
            Distance = Mathf.Clamp(Mathf.Round(metres / 50f) * 50f, Plugin.MinDistance.Value, Mathf.Max(Plugin.MinDistance.Value, MaxRange));

        /// <summary>How far out the raven can fly with its next meal from this roost's tier.</summary>
        public float MaxRange => Scouting.RangeFor(NextMealSpeed * Tier.RangeMultiplier);

        /// <summary>Flight speed: the meal times the roost's tier.</summary>
        public float FlightSpeed => NextMealSpeed * Tier.SpeedMultiplier;

        /// <summary>The chosen distance, shortened to what the next meal allows.</summary>
        public float PlannedDistance => Mathf.Clamp(Distance, Plugin.MinDistance.Value, Mathf.Max(Plugin.MinDistance.Value, MaxRange));

        public Vector3 PlannedTarget => Scouting.TargetPoint(transform.position, Heading, PlannedDistance);

        public float PlannedSeconds => Scouting.TripSeconds(Scouting.DistanceXZ(transform.position, PlannedTarget), FlightSpeed);

        // ------------------------------------------------------------ food (same as Raven Post)

        public List<float> Meals => Zdo != null ? RavenFood.ReadMeals(Zdo) : new List<float>();

        /// <summary>Range and speed multiplier for the next trip: best stored meal, hungry multiplier, or 1 with food off.</summary>
        public float NextMealSpeed
        {
            get
            {
                if (!Plugin.UseFood.Value)
                {
                    return 1f;
                }
                var meals = Meals;
                return meals.Count > 0 ? meals[0] : Mathf.Max(0.05f, Plugin.HungrySpeed.Value);
            }
        }

        public bool IsHungry => Plugin.UseFood.Value && Meals.Count == 0;

        public bool RefusesToFly => IsHungry && Plugin.Hungry.Value == Plugin.HungryBehaviour.Refuse;

        public string MealsStatus
        {
            get
            {
                if (!Plugin.UseFood.Value)
                {
                    return "";
                }
                var meals = Meals;
                if (meals.Count > 0)
                {
                    return $"Meals: {meals.Count}/{Mathf.Max(1, Plugin.MaxMeals.Value)} (next {RavenFood.FormatSpeed(meals[0])})";
                }
                return RefusesToFly
                    ? "<color=#ff6060>Hungry - won't fly until fed</color>"
                    : $"<color=#ff9a40>Hungry - flies short and slow ({RavenFood.FormatSpeed(NextMealSpeed)})</color>";
            }
        }

        /// <summary>Feeds one of <paramref name="item"/> from the player's inventory. False if ravens won't eat it.</summary>
        public bool Feed(Player player, ItemDrop.ItemData item)
        {
            if (Zdo == null || !player)
            {
                return false;
            }
            float speed = RavenFood.SpeedFor(item);
            if (speed <= 0f)
            {
                return false;
            }
            if (!EnsureOwner(player))
            {
                return true;
            }
            var meals = Meals;
            int max = Mathf.Max(1, Plugin.MaxMeals.Value);
            if (meals.Count >= max)
            {
                player.Message(MessageHud.MessageType.Center, "The raven is full");
                return true;
            }
            if (!player.GetInventory().RemoveOneItem(item))
            {
                return true;
            }
            meals.Add(speed);
            RavenFood.WriteMeals(Zdo, meals);
            player.Message(MessageHud.MessageType.Center, Localization.instance.Localize(
                $"The raven ate {item.m_shared.m_name} ({RavenFood.FormatSpeed(speed)}: {Scouting.FormatDistance(Scouting.RangeFor(speed * Tier.RangeMultiplier))} range) - {meals.Count}/{max} meals"));
            return true;
        }

        public void FeedBest(Player player)
        {
            var food = RavenFood.BestFood(player.GetInventory());
            if (food == null)
            {
                player.Message(MessageHud.MessageType.Center, "You have nothing the raven will eat");
                return;
            }
            Feed(player, food);
        }

        // ------------------------------------------------------------ trips per day (off by default)

        public static bool TripsLimited => Plugin.MaxTripsPerDay.Value > 0;

        public int TripsToday
        {
            get
            {
                var parts = (Zdo?.GetString(TripsLogKey) ?? "").Split(':');
                if (parts.Length == 2 && int.TryParse(parts[0], out int day) && int.TryParse(parts[1], out int used) &&
                    day == Scouting.Day(Scouting.Now))
                {
                    return used;
                }
                return 0;
            }
        }

        public int TripsLeft => TripsLimited ? Mathf.Max(0, Plugin.MaxTripsPerDay.Value - TripsToday) : int.MaxValue;

        public string TripsStatus
        {
            get
            {
                if (!TripsLimited)
                {
                    return "";
                }
                int max = Plugin.MaxTripsPerDay.Value;
                return TripsLeft > 0
                    ? $"Trips today: {TripsToday}/{max}"
                    : $"<color=#ff6060>Tired ({max}/{max} today) - flies again tomorrow</color>";
            }
        }

        // ------------------------------------------------------------ sending

        /// <summary>
        /// In multiplayer only the roost's owner should change it. The player using a roost becomes its owner
        /// (opening it does this too); returns false if someone else has it open.
        /// </summary>
        private bool EnsureOwner(Player player)
        {
            if (Zdo == null)
            {
                return false;
            }
            if (_nview.IsOwner())
            {
                return true;
            }
            if (!_container.IsInUse() && Zdo.GetInt(ZDOVars.s_inUse) == 0)
            {
                _nview.ClaimOwnership();
            }
            if (!_nview.IsOwner())
            {
                if (player)
                {
                    player.Message(MessageHud.MessageType.Center, "Someone else is using this roost");
                }
                return false;
            }
            return true;
        }

        public void Send(Player player)
        {
            if (Zdo == null || !player || !Scouting.IsActive || !EnsureOwner(player))
            {
                return;
            }
            double now = Scouting.Now;
            var current = Trip;
            if (current != null && current.IsAway(now))
            {
                player.Message(MessageHud.MessageType.Center,
                    $"The raven is still out scouting - back in {Scouting.FormatDuration(current.ReturnAt - now)}");
                return;
            }
            if (TripsLimited && TripsLeft <= 0)
            {
                player.Message(MessageHud.MessageType.Center, "The raven is tired - it rests until tomorrow");
                return;
            }
            if (RefusesToFly)
            {
                player.Message(MessageHud.MessageType.Center, "Your raven is hungry - feed it first");
                return;
            }

            float meal = NextMealSpeed;
            float speed = FlightSpeed;
            var target = PlannedTarget;
            float distance = Scouting.DistanceXZ(transform.position, target);
            if (distance < Plugin.MinDistance.Value * 0.5f)
            {
                player.Message(MessageHud.MessageType.Center, "That way is the edge of the world");
                return;
            }
            float seconds = Scouting.TripSeconds(distance, speed);

            var trip = new ScoutTrip
            {
                SentAt = now,
                ReturnAt = now + seconds,
                From = transform.position,
                Target = target,
                Sender = player.GetPlayerName(),
                Meal = meal,
                SenderNoMap = Game.m_noMap,
                Tier = TierIndex,
            };
            Scouting.WriteTrip(Zdo, trip);
            ScoutNet.TripStarted(Zdo.m_uid);

            var meals = Meals;
            if (Plugin.UseFood.Value && meals.Count > 0)
            {
                meals.RemoveAt(0);
                RavenFood.WriteMeals(Zdo, meals);
            }
            if (TripsLimited)
            {
                Zdo.Set(TripsLogKey, $"{Scouting.Day(now)}:{TripsToday + 1}");
            }

            RavenFlight.Launch(SeatPosition, target, seconds, speed);
            ScoutNet.BroadcastTakeOff(SeatPosition, target, seconds, speed);
            UpdateSeat();

            string dir = Scouting.CompassName(Scouting.Heading(transform.position, target));
            player.Message(MessageHud.MessageType.Center,
                $"Raven flies {Scouting.FormatDistance(distance)} {dir} - back in {Scouting.FormatDuration(seconds)}");
            Plugin.Log.LogInfo($"Scout sent {distance:0} m {dir} ({Heading:0} deg) from {transform.position}, meal {RavenFood.FormatSpeed(meal)}, {seconds:0} s");
        }

        // ------------------------------------------------------------ the sitting raven

        private Transform SeatedRaven => transform.Find("RavenScout_PerchedRaven");

        /// <summary>Where the sitting raven sits (top of the post), or above the box if there is none.</summary>
        public Vector3 SeatPosition
        {
            get
            {
                var seated = SeatedRaven;
                return seated ? seated.position : transform.position + Vector3.up * 1.5f;
            }
        }

        /// <summary>The sitting raven is hidden while it's away; anyone nearby sees it land, timed to the real return.</summary>
        private void UpdateSeat()
        {
            var seated = SeatedRaven;
            if (!seated || !Scouting.IsActive)
            {
                return;
            }
            var trip = Trip;
            double now = Scouting.Now;
            bool away = trip != null && trip.IsAway(now);
            if (seated.gameObject.activeSelf == away)
            {
                seated.gameObject.SetActive(!away);
            }
            if (!away)
            {
                return;
            }

            var player = Player.m_localPlayer;
            double left = trip.ReturnAt - now;
            float lead = Mathf.Clamp((float)(trip.ReturnAt - trip.SentAt) * 0.5f, 1.5f, RavenFlight.LandingSeconds);
            if (player && left <= lead && Vector3.Distance(player.transform.position, transform.position) <= LandingViewDistance &&
                s_landingsShown.Add(trip.Key(Zdo.m_uid)))
            {
                RavenFlight.Land(SeatPosition, trip.Target, (float)System.Math.Max(0.5, left));
            }
        }

        // ------------------------------------------------------------ hover text

        public string GetExtraHoverText()
        {
            if (Zdo == null || !Scouting.IsActive)
            {
                return "";
            }
            var sb = new StringBuilder();
            sb.Append($"\n<color=orange>{RoostName}</color>  <color=grey>{Tier.Name}</color>  [<color=yellow><b>{Plugin.RenameKey.Value}</b></color>] Rename");
            var trip = Trip;
            double now = Scouting.Now;
            if (trip != null && trip.IsAway(now))
            {
                string dir = Scouting.CompassName(Scouting.Heading(trip.From, trip.Target));
                sb.Append($"\nRaven out scouting {Scouting.FormatDistance(trip.Distance)} {dir} - back in {Scouting.FormatDuration(trip.ReturnAt - now)}");
            }
            else if (trip != null && !trip.Reported)
            {
                sb.Append($"\n<color=yellow>Raven is back - its news is for {trip.Sender}</color>");
            }
            else
            {
                sb.Append($"\nRaven ready  <color=grey>range {Scouting.FormatDistance(MaxRange)}</color>");
            }
            if (Plugin.UseFood.Value)
            {
                sb.Append($"\n{MealsStatus}");
                sb.Append("\n<color=grey>Use food from your hotbar on the roost to feed</color>");
            }
            if (TripsLimited)
            {
                sb.Append($"\n{TripsStatus}");
            }
            int waiting = PendingReports;
            if (waiting > 0)
            {
                sb.Append($"\n<color=yellow>{waiting} Scout Report{(waiting == 1 ? "" : "s")} waiting - make room in the box</color>");
            }
            return sb.ToString();
        }
    }
}
