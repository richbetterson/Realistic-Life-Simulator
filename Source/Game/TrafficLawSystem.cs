// Realistic Life Simulator - traffic law detection for the player.
// Runs only while the player drives a road vehicle. Light checks every 250 ms, road
// lookups (street name, zone, road node) once per second.
//
// Detection methods (GTA has no traffic-law data, so some are heuristics):
//  * Speeding     - road type from street name, zone and road-node flags; sustained 3 s.
//  * Red light    - entering a traffic-light junction at speed while AI cars travelling the
//                   same direction beside/behind the player are stopped and waiting.
//  * Stop / yield - passing a give-way junction without slowing below 12 km/h.
//  * Wrong way    - two-way road: driving on the left of the centre line for 4 s;
//                   one-way road: several AI cars coming towards the player in the same road.
//  * Headlights   - driving at night with lights off for 15 s.
//  * Reckless     - three or more moving violations within 90 s, or 50+ km/h over the limit.
// Fines: Witnessed (default) = only when a police officer can see it.
// Police pull-overs are added in Phase 3.

using System;
using System.Collections.Generic;
using System.Globalization;
using GTA;
using GTA.Math;
using GTA.Native;
using RealisticLifeSimulator.Core;

namespace RealisticLifeSimulator.Game
{
    public sealed class TrafficLawSystem : IModule
    {
        private readonly RlsMain main;
        private SpeedLimitTable limits;

        // road info (refreshed every second)
        private int nextRoadCheck;
        private int currentLimit;
        private RoadCategory currentCategory;
        private string currentStreet = "";
        private string currentZone = "";
        private int nodeFlags;
        private bool roadKnown;

        // speeding episode
        private int overSince = -1, underSince = -1;
        private int maxOver;
        private bool speedingReported;

        // junctions
        private Vector3 lastLightJunction, lastGiveWayJunction;
        private readonly float[] speedHistory = new float[16]; // last 4 s at 250 ms
        private int historyIndex;

        // wrong way / headlights
        private int wrongWaySince = -1;
        private int lightsOffSince = -1;

        // cooldowns and recent violations
        private readonly Dictionary<ViolationType, int> cooldownUntil = new Dictionary<ViolationType, int>();
        private readonly List<int> recentMoving = new List<int>();

        public TrafficLawSystem(RlsMain main)
        {
            this.main = main;
        }

        public string Name { get { return "Traffic"; } }
        public int IntervalMs { get { return 250; } }

        public int CurrentLimit { get { return roadKnown ? currentLimit : 0; } }
        public RoadCategory CurrentCategory { get { return currentCategory; } }
        public bool IsSpeeding { get; private set; }

        public void Start()
        {
            limits = SpeedLimitTable.LoadOrCreate(main.Paths.SpeedLimitsFile);
        }

        public void Shutdown() { }

        public void Update()
        {
            RlsSettings set = main.Config.Settings;
            Ped p = GTA.Game.Player.Character;
            Vehicle v = p != null && p.Exists() ? p.CurrentVehicle : null;
            if (!set.TrafficEnabled || v == null || !v.Exists() || v.Driver != p || GTA.Game.IsPaused || N.InMission())
            {
                ResetEpisode();
                roadKnown = false;
                IsSpeeding = false;
                return;
            }
            int cls = (int)v.ClassType;
            if (cls == 13 || cls == 14 || cls == 15 || cls == 16 || cls == 21) { roadKnown = false; return; }
            // Emergency vehicles on duty are exempt.
            if (cls == 18 && N.Call<bool>(N.IS_VEHICLE_SIREN_ON, v)) { ResetEpisode(); IsSpeeding = false; return; }

            int now = Environment.TickCount;
            Vector3 pos = v.Position;
            float kmh = v.Speed * 3.6f;
            DriverProfile prof = main.CurrentProfile();
            prof.CleanKm += v.Speed * 0.25 / 1000.0;
            if (prof.CleanKm >= 15) { prof.CleanKm -= 15; prof.Reputation = Math.Min(100, prof.Reputation + 1); }
            speedHistory[historyIndex] = kmh;
            historyIndex = (historyIndex + 1) % speedHistory.Length;

            if (unchecked(now - nextRoadCheck) >= 0)
            {
                nextRoadCheck = now + 1000;
                RefreshRoad(pos);
                if (set.DetectWrongWay) CheckWrongWay(v, pos, kmh, now);
                if (set.DetectHeadlights) CheckHeadlights(v, kmh, now);
                if (set.DetectFailureToYield) CheckYield(v, kmh, now);
            }

            if (set.DetectSpeeding) CheckSpeeding(v, kmh, now, set);
            if (set.DetectRedLights || set.DetectStopSigns) CheckJunction(v, pos, kmh, now, set);
        }

        // ------------------------------------------------------------ road info

        private void RefreshRoad(Vector3 pos)
        {
            OutputArgument street = new OutputArgument(), crossing = new OutputArgument();
            N.Call(N.GET_STREET_NAME_AT_COORD, pos.X, pos.Y, pos.Z, street, crossing);
            int streetHash = street.GetResult<int>();
            currentStreet = streetHash != 0 ? (N.Call<string>(N.GET_STREET_NAME_FROM_HASH_KEY, streetHash) ?? "") : "";
            currentZone = N.Call<string>(N.GET_NAME_OF_ZONE, pos.X, pos.Y, pos.Z) ?? "";

            OutputArgument density = new OutputArgument(), flags = new OutputArgument();
            nodeFlags = N.Call<bool>(N.GET_VEHICLE_NODE_PROPERTIES, pos.X, pos.Y, pos.Z, density, flags) ? flags.GetResult<int>() : 0;

            if ((nodeFlags & TrafficRules.NodeOffRoad) != 0)
            {
                roadKnown = false; // off-road: no limit enforced
                return;
            }
            RoadCategory cat;
            currentLimit = limits.Classify(currentStreet, currentZone, nodeFlags, out cat);
            currentCategory = cat;
            roadKnown = true;
        }

        // ------------------------------------------------------------ speeding

        private void CheckSpeeding(Vehicle v, float kmh, int now, RlsSettings set)
        {
            if (!roadKnown) { IsSpeeding = false; return; }
            int threshold = currentLimit + set.SpeedToleranceKmh;
            IsSpeeding = kmh > currentLimit + 2;

            if (kmh > threshold)
            {
                underSince = -1;
                if (overSince < 0) { overSince = now; maxOver = 0; }
                maxOver = Math.Max(maxOver, (int)(kmh - currentLimit));
                if (!speedingReported && unchecked(now - overSince) >= 3000)
                {
                    speedingReported = true;
                    string details = L.F("{0:0} km/h in a {1} km/h zone ({2})", "{0:0} км/ч при ограничении {1} км/ч ({2})",
                        currentLimit + maxOver, currentLimit, TrafficRules.CategoryName(currentCategory));
                    Report(ViolationType.Speeding, maxOver, details, now, 60000);
                    if (maxOver >= 50) Report(ViolationType.Reckless, 0,
                        L.F("{0} km/h over the limit", "Превышение на {0} км/ч", maxOver), now, 120000);
                }
            }
            else if (overSince >= 0)
            {
                if (underSince < 0) underSince = now;
                if (unchecked(now - underSince) > 5000) ResetEpisode();
            }
        }

        private void ResetEpisode()
        {
            overSince = -1;
            underSince = -1;
            maxOver = 0;
            speedingReported = false;
        }

        // ------------------------------------------------------------ junctions

        private void CheckJunction(Vehicle v, Vector3 pos, float kmh, int now, RlsSettings set)
        {
            bool light = (nodeFlags & TrafficRules.NodeTrafficLight) != 0;
            bool giveWay = (nodeFlags & TrafficRules.NodeGiveWay) != 0;
            if (!light && !giveWay) return;

            OutputArgument nodePosArg = new OutputArgument();
            if (!N.Call<bool>(N.GET_CLOSEST_VEHICLE_NODE, pos.X, pos.Y, pos.Z, nodePosArg, 1, 3f, 0)) return;
            Vector3 node = nodePosArg.GetResult<Vector3>();
            if (N.Dist2D(node, pos) > 9f) return;

            if (light && set.DetectRedLights && kmh > 15f && N.Dist2D(node, lastLightJunction) > 30f)
            {
                lastLightJunction = node;
                int waiting = CountWaitingCars(v);
                if (waiting > 0)
                {
                    Report(ViolationType.RedLight, 0,
                        L.F("{0} car(s) were stopped at the light", "Другие машины стояли на светофоре: {0}", waiting), now, 20000);
                }
            }

            if (giveWay && !light && set.DetectStopSigns && kmh > 5f && N.Dist2D(node, lastGiveWayJunction) > 30f)
            {
                lastGiveWayJunction = node;
                float min = float.MaxValue;
                foreach (float s in speedHistory) min = Math.Min(min, s);
                if (min > 12f)
                {
                    Report(ViolationType.StopSign, 0,
                        L.F("Lowest speed before the junction: {0:0} km/h", "Минимальная скорость перед перекрёстком: {0:0} км/ч", min), now, 30000);
                }
            }
        }

        // AI cars heading the same way as the player, stopped next to or just behind it.
        private static int CountWaitingCars(Vehicle player)
        {
            Vector3 fwd = player.ForwardVector;
            Vector3 pos = player.Position;
            int count = 0;
            foreach (Vehicle o in World.GetNearbyVehicles(pos, 22f))
            {
                if (o == null || !o.Exists() || o == player || o.Speed > 0.5f) continue;
                Ped d = o.Driver;
                if (d == null || !d.Exists() || d.IsPlayer) continue;
                Vector3 of = o.ForwardVector;
                if (of.X * fwd.X + of.Y * fwd.Y < 0.85f) continue; // same direction (within ~30 deg)
                Vector3 rel = o.Position - pos;
                float along = rel.X * fwd.X + rel.Y * fwd.Y;
                float side = Math.Abs(rel.X * fwd.Y - rel.Y * fwd.X);
                if (along > -14f && along < 6f && side < 9f) count++;
            }
            return count;
        }

        // ------------------------------------------------------------ wrong way

        private void CheckWrongWay(Vehicle v, Vector3 pos, float kmh, int now)
        {
            bool wrong = false;
            if (roadKnown && kmh > 20f && (nodeFlags & (TrafficRules.NodeJunction | TrafficRules.NodeOffRoad)) == 0)
            {
                OutputArgument p1 = new OutputArgument(), p2 = new OutputArgument(), lf = new OutputArgument(),
                               lb = new OutputArgument(), width = new OutputArgument();
                if (N.Call<bool>(N.GET_CLOSEST_ROAD, pos.X, pos.Y, pos.Z, 1f, 1, p1, p2, lf, lb, width, false))
                {
                    Vector3 a = p1.GetResult<Vector3>(), b = p2.GetResult<Vector3>();
                    int lanesF = lf.GetResult<int>(), lanesB = lb.GetResult<int>();
                    Vector3 seg = b - a;
                    float len = (float)Math.Sqrt(seg.X * seg.X + seg.Y * seg.Y);
                    if (len > 1f)
                    {
                        float sx = seg.X / len, sy = seg.Y / len;
                        Vector3 fwd = v.ForwardVector;
                        float dot = fwd.X * sx + fwd.Y * sy;
                        if (lanesF > 0 && lanesB > 0 && Math.Abs(dot) > 0.7f)
                        {
                            // Right-hand traffic: you must be right of the centre line in your direction.
                            float tx = dot > 0 ? sx : -sx, ty = dot > 0 ? sy : -sy;
                            float rx = pos.X - a.X, ry = pos.Y - a.Y;
                            float left = tx * ry - ty * rx; // > 0 = left of the line
                            wrong = left > 2.0f && left < 12f;
                        }
                        else if (lanesF == 0 || lanesB == 0)
                        {
                            wrong = OncomingCars(v, pos) >= 2;
                        }
                    }
                }
            }

            if (!wrong) { wrongWaySince = -1; return; }
            if (wrongWaySince < 0) { wrongWaySince = now; return; }
            if (unchecked(now - wrongWaySince) >= 4000)
            {
                wrongWaySince = -1;
                Report(ViolationType.WrongWay, 0, string.IsNullOrEmpty(currentStreet) ? "" : currentStreet, now, 60000);
            }
        }

        private static int OncomingCars(Vehicle player, Vector3 pos)
        {
            Vector3 fwd = player.ForwardVector;
            int n = 0;
            foreach (Vehicle o in World.GetNearbyVehicles(pos, 45f))
            {
                if (o == null || !o.Exists() || o == player || o.Speed < 3f) continue;
                Ped d = o.Driver;
                if (d == null || !d.Exists() || d.IsPlayer) continue;
                Vector3 of = o.ForwardVector;
                if (of.X * fwd.X + of.Y * fwd.Y > -0.85f) continue;
                Vector3 rel = o.Position - pos;
                float side = Math.Abs(rel.X * fwd.Y - rel.Y * fwd.X);
                float along = rel.X * fwd.X + rel.Y * fwd.Y;
                if (along > 0 && side < 6f) n++;
            }
            return n;
        }

        // ------------------------------------------------------------ emergency vehicles

        private int yieldSince = -1;

        // An emergency vehicle with its siren on is close behind in the same direction and
        // catching up, but the player keeps driving at speed instead of slowing / pulling over.
        private void CheckYield(Vehicle v, float kmh, int now)
        {
            bool blocking = false;
            if (kmh > 25f)
            {
                Vector3 fwd = v.ForwardVector;
                foreach (Vehicle e in World.GetNearbyVehicles(v.Position, 35f))
                {
                    if (e == null || !e.Exists() || e == v || (int)e.ClassType != 18 || e.Speed < 8f) continue;
                    if (!N.Call<bool>(N.IS_VEHICLE_SIREN_ON, e)) continue;
                    if (main.Dispatch.IsInUse(e) && main.Police != null && main.Police.StopActive) continue; // that is our own stop
                    Vector3 rel = e.Position - v.Position;
                    float along = rel.X * fwd.X + rel.Y * fwd.Y;
                    float side = Math.Abs(rel.X * fwd.Y - rel.Y * fwd.X);
                    Vector3 ef = e.ForwardVector;
                    if (along < -30f || along > -4f || side > 5f || ef.X * fwd.X + ef.Y * fwd.Y < 0.8f) continue;
                    blocking = true;
                    break;
                }
            }
            if (!blocking) { yieldSince = -1; return; }
            if (yieldSince < 0) { yieldSince = now; return; }
            if (unchecked(now - yieldSince) >= 6000)
            {
                yieldSince = -1;
                Report(ViolationType.FailureToYieldEmergency, 0,
                    L.S("An emergency vehicle with its siren on was stuck behind you", "За вами ехал спецтранспорт с сиреной и не мог проехать"), now, 90000);
            }
        }

        // ------------------------------------------------------------ headlights

        private void CheckHeadlights(Vehicle v, float kmh, int now)
        {
            int hour = N.Call<int>(N.GET_CLOCK_HOURS);
            bool night = hour >= 21 || hour < 5;
            if (!night || kmh < 10f) { lightsOffSince = -1; return; }
            OutputArgument on = new OutputArgument(), high = new OutputArgument();
            N.Call(N.GET_VEHICLE_LIGHTS_STATE, v, on, high);
            bool lights = on.GetResult<bool>() || high.GetResult<bool>();
            if (lights) { lightsOffSince = -1; return; }
            if (lightsOffSince < 0) { lightsOffSince = now; return; }
            if (unchecked(now - lightsOffSince) >= 15000)
            {
                lightsOffSince = -1;
                Report(ViolationType.NoHeadlights, 0, "", now, 300000);
            }
        }

        // ------------------------------------------------------------ recording and fines

        private void Report(ViolationType type, int overKmh, string details, int now, int cooldownMs)
        {
            if (main.Police != null && main.Police.StopActive) return; // already being dealt with
            int until;
            if (cooldownUntil.TryGetValue(type, out until) && unchecked(now - until) < 0) return;
            cooldownUntil[type] = now + cooldownMs;

            bool moving = type != ViolationType.NoHeadlights && type != ViolationType.Reckless;
            if (moving) LastMovingViolationTick = now;

            RlsSettings set = main.Config.Settings;
            Ped witness = FindWitness();
            bool witnessed = witness != null;
            bool fined = set.Fines == FineMode.Always || (set.Fines == FineMode.Witnessed && witnessed);

            // A police car saw it: the officer pulls the player over and decides at the roadside.
            if (fined && witnessed && set.PoliceStops && main.Police != null
                && main.Police.TryStartStop(type, overKmh, details ?? "", LocationText(), witness))
            {
                Log.Info("Violation " + type + " witnessed; traffic stop started");
                TrackRepeated(moving, now);
                return;
            }

            double fine = fined ? Math.Round(TrafficRules.BaseFine(type, overKmh) * set.FineMultiplier) : 0;
            int points = fine > 0 ? PoliceRules.PointsFor(type, overKmh) : 0;
            ViolationRecord r = AddViolation(type, details, fine, witnessed, points, false, LocationText());

            string msg = "~o~" + TrafficRules.Name(type) + "~s~";
            if (!string.IsNullOrEmpty(details)) msg += "~n~" + details;
            if (fine > 0) msg += "~n~" + L.F("Fine: {0}, {1} point(s) (pay in the Life Hub)", "Штраф: {0}, баллов: {1} (оплата в центре управления)", L.Money(fine), points);
            else if (set.Fines == FineMode.Witnessed) msg += "~n~" + L.S("No police saw it - recorded only.", "Полиция не видела - только запись.");
            main.Notify(msg);
            Log.Info(string.Format(CultureInfo.InvariantCulture, "Violation {0} ({1}) at {2}, fine {3}, witnessed {4}",
                type, r.Details, r.Location, fine, witnessed));
            TrackRepeated(moving, now);
        }

        private void TrackRepeated(bool moving, int now)
        {
            if (!moving) return;
            recentMoving.Add(now);
            recentMoving.RemoveAll(t => unchecked(now - t) > 90000);
            if (recentMoving.Count >= 3)
            {
                recentMoving.Clear();
                Report(ViolationType.Reckless, 0, L.S("Repeated violations within 90 seconds", "Несколько нарушений за 90 секунд"), now, 120000);
            }
        }

        public int LastMovingViolationTick { get; private set; }

        // Shared by traffic, police stops and accidents: one place that records a violation,
        // updates statistics, licence points and reputation.
        public ViolationRecord AddViolation(ViolationType type, string details, double fine, bool witnessed,
                                            int points, bool warning, string location)
        {
            SaveData data = main.Saves.Data;
            DriverProfile prof = main.CurrentProfile();
            ViolationRecord r = new ViolationRecord();
            r.TimeUtc = DateTime.UtcNow.ToString("o");
            r.Type = type.ToString();
            r.Details = details ?? "";
            r.Location = location ?? "";
            r.Fine = fine;
            r.Witnessed = witnessed;
            r.Points = points;
            r.Warning = warning;
            r.Character = prof.Character;
            data.Violations.Add(r);
            if (data.Violations.Count > SaveData.MaxViolations) data.Violations.RemoveAt(0);
            data.Driving.Violations++;
            if (fine > 0) data.Driving.FinesIssued++;
            if (warning) { data.Driving.Warnings++; prof.Warnings++; }

            double play = data.Stats.TotalPlayTimeSeconds;
            prof.AddPoints(points, TrafficRules.Name(type), play);
            if (fine > 0 || warning) prof.Reputation = PoliceRules.ReputationAfter(prof.Reputation, points, warning);
            prof.CleanKm = 0;
            return r;
        }

        private string LocationText()
        {
            string zone = currentZone;
            try
            {
                string label = GTA.Game.GetLocalizedString(currentZone);
                if (!string.IsNullOrEmpty(label) && label != "NULL") zone = label;
            }
            catch (Exception) { }
            if (string.IsNullOrEmpty(currentStreet)) return zone;
            return currentStreet + ", " + zone;
        }

        // Street + area for any position (used for accident reports).
        public string DescribeLocation(Vector3 pos)
        {
            OutputArgument street = new OutputArgument(), crossing = new OutputArgument();
            N.Call(N.GET_STREET_NAME_AT_COORD, pos.X, pos.Y, pos.Z, street, crossing);
            int sh = street.GetResult<int>();
            string s = sh != 0 ? (N.Call<string>(N.GET_STREET_NAME_FROM_HASH_KEY, sh) ?? "") : "";
            string zoneCode = N.Call<string>(N.GET_NAME_OF_ZONE, pos.X, pos.Y, pos.Z) ?? "";
            string zone = zoneCode;
            try
            {
                string label = GTA.Game.GetLocalizedString(zoneCode);
                if (!string.IsNullOrEmpty(label) && label != "NULL") zone = label;
            }
            catch (Exception) { }
            return string.IsNullOrEmpty(s) ? zone : s + ", " + zone;
        }

        // Speed limit at any position (used to judge NPC drivers).
        public int LimitAt(Vector3 pos)
        {
            OutputArgument street = new OutputArgument(), crossing = new OutputArgument();
            N.Call(N.GET_STREET_NAME_AT_COORD, pos.X, pos.Y, pos.Z, street, crossing);
            int sh = street.GetResult<int>();
            string s = sh != 0 ? (N.Call<string>(N.GET_STREET_NAME_FROM_HASH_KEY, sh) ?? "") : "";
            string zone = N.Call<string>(N.GET_NAME_OF_ZONE, pos.X, pos.Y, pos.Z) ?? "";
            OutputArgument density = new OutputArgument(), flags = new OutputArgument();
            int nf = N.Call<bool>(N.GET_VEHICLE_NODE_PROPERTIES, pos.X, pos.Y, pos.Z, density, flags) ? flags.GetResult<int>() : 0;
            if ((nf & TrafficRules.NodeOffRoad) != 0) return 0;
            RoadCategory cat;
            return limits.Classify(s, zone, nf, out cat);
        }

        // A police officer (on foot or driving) who can see the player.
        private static Ped FindWitness()
        {
            Ped player = GTA.Game.Player.Character;
            Entity target = player.CurrentVehicle != null ? (Entity)player.CurrentVehicle : player;
            Ped onFoot = null;
            foreach (Ped cop in World.GetNearbyPeds(player, 80f))
            {
                if (cop == null || !cop.Exists() || cop.IsDead || !N.IsCop(cop)) continue;
                if (!N.Call<bool>(N.HAS_ENTITY_CLEAR_LOS_TO_ENTITY, cop, target, 17)) continue;
                Vehicle cv = cop.CurrentVehicle;
                if (cv != null && cv.Driver == cop) return cop;   // prefer a driving officer (can pull you over)
                if (onFoot == null) onFoot = cop;
            }
            return onFoot;
        }    }
}
