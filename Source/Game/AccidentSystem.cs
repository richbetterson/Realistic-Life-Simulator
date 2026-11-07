// Realistic Life Simulator - player traffic accidents.
//
// Detection (every 100 ms while driving): a sudden change of velocity or body damage
// together with a vehicle touching/damaged by the player's car.
// The evidence at the moment of impact is stored for the investigation.
//
// 1.1.0: pedestrian accidents are removed. Hitting a pedestrian no longer opens a scene,
// calls 911, writes a report or issues fines. Only vehicle-vs-vehicle collisions start a
// scene. The victim branches below stay inert (Victim is always null) so accident reports
// from older saves still load and display unchanged.
//
// At the scene: the other driver brakes, gets out, inspects, and after a serious crash
// calls 911 (police drive in on the roads; an ambulance if someone is hurt). The player
// opens the accident menu with the Context key near the scene: check on the other driver,
// exchange details, call police / ambulance, wait, or leave.
//
// Police investigation: the officer inspects both vehicles, speaks to the other driver,
// then to the player (statement + documents), determines fault from the evidence, issues
// fines/points where justified and files a report. Leaving the scene of a serious accident
// is an offence.

using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using RealisticLifeSimulator.Core;

namespace RealisticLifeSimulator.Game
{
    public sealed class AccidentSystem : IModule, IFrameModule
    {
        private enum OtherState { Braking, Exiting, Waiting, Calling, Done, Fled, None }
        private enum InvState { None, Arriving, Exiting, InspectOther, InspectPlayer, TalkOther, TalkPlayer, Talking, Writing, Done }

        private sealed class Scene
        {
            public AccidentReport Report;
            public CollisionEvidence Evidence;
            public AccidentSeverity Severity;
            public Vector3 Position;
            public Vehicle PlayerVehicle, OtherVehicle;
            public Ped OtherDriver, Victim;
            public OtherState Other;
            public int OtherSince;
            public int Started;
            public Responder Police, Ambulance;
            public InvState Inv;
            public int InvSince;
            public bool AmbDone;
            public int AmbSince;
            public Statement Statement;
            public bool LicenceShown, Refused, Decided;
            public int LeftSince = -1;
            public Vector3 WalkTarget;
            // 0.6.0 pedestrian scenes
            public int BystanderCallAt;      // a witness calls 911 for a struck pedestrian
            public bool VictimLeft;          // walked away or no longer exists
            public bool VictimSettled;       // sitting on the ground waiting for help
            public int AmbStage;             // 0 driving, 1 walking to patient, 2 treating, 3 boarding, 4 done
            public int AmbStageSince;
            public int AmbNextWalk;
        }

        private readonly RlsMain main;
        private readonly Random rng = new Random();
        private Scene scene;
        private Vehicle lastVeh;
        private Vector3 lastVel;
        private float lastBody, lastEngine;
        private readonly Dictionary<int, int> recentOthers = new Dictionary<int, int>();

        public AccidentSystem(RlsMain main) { this.main = main; }

        public string Name { get { return "Accidents"; } }
        public int IntervalMs { get { return 100; } }
        public bool SceneActive { get { return scene != null; } }

        public void Start() { }

        public void Shutdown()
        {
            if (scene != null) CloseScene(L.S("Session ended", "Сеанс завершён"));
        }

        // ------------------------------------------------------------ detection

        public void Update()
        {
            int now = Environment.TickCount;
            if (scene != null)
            {
                try { UpdateScene(now); }
                catch (Exception ex)
                {
                    Log.Error("Accident scene failed; closing it", ex);
                    CloseScene("error");
                }
            }

            RlsSettings set = main.Config.Settings;
            Ped p = GTA.Game.Player.Character;
            Vehicle v = p != null && p.Exists() ? p.CurrentVehicle : null;
            if (!set.AccidentsEnabled || v == null || !v.Exists() || v.Driver != p || GTA.Game.IsPaused)
            {
                lastVeh = null;
                return;
            }
            int cls = (int)v.ClassType;
            if (cls == 13 || cls == 14 || cls == 15 || cls == 16 || cls == 21) { lastVeh = null; return; }

            Vector3 vel = v.Velocity;
            float body = v.BodyHealth, engine = v.EngineHealth;
            if (v != lastVeh)
            {
                lastVeh = v;
                lastVel = vel;
                lastBody = body;
                lastEngine = engine;
                return;
            }

            float dv = (vel - lastVel).Length();
            float bodyDrop = lastBody - body, engineDrop = lastEngine - engine;
            Vector3 before = lastVel;
            lastVel = vel;
            lastBody = body;
            lastEngine = engine;

            if (scene != null || N.InMission() || N.Wanted() > 0) return;
            if (dv < 3f && bodyDrop < 8f && engineDrop < 15f) return; // no impact (pedestrians are not tracked since 1.1.0)
            TryDetect(p, v, before, bodyDrop + engineDrop, now, set);
        }

        private void TryDetect(Ped p, Vehicle v, Vector3 velBefore, float damage, int now, RlsSettings set)
        {
            Vector3 pos = v.Position;
            Vehicle other = null;
            float best = 64f;
            foreach (Vehicle o in World.GetNearbyVehicles(pos, 8f))
            {
                if (o == null || !o.Exists() || o == v) continue;
                bool hit = N.Call<bool>(N.IS_ENTITY_TOUCHING_ENTITY, v, o)
                           || N.Call<bool>(N.HAS_ENTITY_BEEN_DAMAGED_BY_ENTITY, o, v, true)
                           || N.Call<bool>(N.HAS_ENTITY_BEEN_DAMAGED_BY_ENTITY, v, o, true);
                if (!hit) continue;
                float d = N.DistSq(o.Position, pos);
                if (d < best) { best = d; other = o; }
            }

            // Walls, poles and pedestrians: no accident scene. Only another vehicle counts.
            if (other == null) return;
            Ped victim = null;

            Entity key = other;
            int last;
            if (recentOthers.TryGetValue(key.Handle, out last) && unchecked(now - last) < 60000) return;
            recentOthers[key.Handle] = now;
            if (recentOthers.Count > 40) recentOthers.Clear();

            double playerKmh = velBefore.Length() * 3.6;
            double otherKmh = other != null ? other.Speed * 3.6 : 0;
            if (Math.Max(playerKmh, otherKmh) < set.AccidentMinSpeedKmh) return;

            N.Call(N.CLEAR_ENTITY_LAST_DAMAGE_ENTITY, key);
            N.Call(N.CLEAR_ENTITY_LAST_DAMAGE_ENTITY, v);
            StartScene(p, v, other, victim, velBefore, playerKmh, otherKmh, damage, now);
        }

        // ------------------------------------------------------------ scene start

        private void StartScene(Ped p, Vehicle v, Vehicle other, Ped victim, Vector3 velBefore, double playerKmh, double otherKmh, float damage, int now)
        {
            Scene s = new Scene();
            s.Started = now;
            s.PlayerVehicle = v;
            s.OtherVehicle = other;
            s.Victim = victim;
            s.Position = v.Position;
            s.OtherDriver = other != null ? other.Driver : null;
            if (s.OtherDriver != null && (!s.OtherDriver.Exists() || s.OtherDriver.IsPlayer)) s.OtherDriver = null;

            CollisionEvidence e = new CollisionEvidence();
            e.PlayerSpeedKmh = playerKmh;
            e.OtherSpeedKmh = otherKmh;
            e.PedestrianStruck = victim != null;
            e.OtherWasStopped = other != null && otherKmh < 3;
            e.PlayerViolationJustBefore = unchecked(now - main.Traffic.LastMovingViolationTick) < 10000 && main.Traffic.LastMovingViolationTick != 0;
            Vector3 dir = velBefore.Length() > 1f ? velBefore.Normalized : v.ForwardVector;
            Entity otherEnt = other != null ? (Entity)other : victim;
            Vector3 rel = otherEnt.Position - v.Position;
            e.OtherAhead = rel.Length() > 0.1f ? Vector3.Dot(rel.Normalized, dir) : 0;
            s.Evidence = e;

            bool death = (victim != null && victim.IsDead) || (s.OtherDriver != null && s.OtherDriver.IsDead);
            bool injury = victim != null || (s.OtherDriver != null && (s.OtherDriver.IsInjured || s.OtherDriver.Health < 150));
            s.Severity = PoliceRules.Severity(Math.Max(playerKmh, otherKmh), damage, injury, death);

            AccidentReport r = new AccidentReport();
            r.Id = PoliceRules.ReportId(rng);
            r.TimeUtc = DateTime.UtcNow.ToString("o");
            r.Character = main.CurrentProfile().Character;
            r.Location = main.Traffic.DescribeLocation(s.Position);
            r.Severity = s.Severity.ToString();
            r.PedestrianStruck = victim != null;
            r.OtherVehicle = other != null ? N.VehicleName(other) : L.S("Pedestrian", "Пешеход");
            r.OtherPlate = other != null ? N.Plate(other) : "";
            r.PlayerSpeedKmh = Math.Round(playerKmh);
            r.OtherSpeedKmh = Math.Round(otherKmh);
            r.Statement = Statement.None.ToString();
            r.Fault = Fault.Undetermined.ToString();
            r.OtherDriverState = OtherStateText(s);
            main.Saves.Data.AddAccident(r);
            main.Saves.Data.Driving.Accidents++;
            s.Report = r;
            scene = s;

            // The other party reacts.
            if (s.OtherDriver != null && !s.OtherDriver.IsDead)
            {
                s.OtherDriver.IsPersistent = true;
                other.IsPersistent = true;
                N.Block(s.OtherDriver);
                if (rng.NextDouble() < 0.1 && s.Severity != AccidentSeverity.Severe)
                {
                    N.Call(N.TASK_VEHICLE_DRIVE_WANDER, s.OtherDriver, other, 30f, N.StyleUrgent);
                    s.Other = OtherState.Fled;
                }
                else
                {
                    N.Call(N.TASK_VEHICLE_TEMP_ACTION, s.OtherDriver, other, 6, 60000);
                    s.Other = OtherState.Braking;
                }
            }
            else s.Other = OtherState.None;
            if (victim != null && victim.Exists())
            {
                victim.IsPersistent = true;
                if (!victim.IsDead) N.Block(victim); // stays at the scene instead of fleeing
                // Nobody else is involved, so a witness on the street calls 911.
                if (main.Config.Settings.NpcCallEmergency) s.BystanderCallAt = now + 7000 + rng.Next(4000);
            }
            s.OtherSince = now;
            r.OtherDriverState = OtherStateText(s);

            main.Notify(L.F("~o~Traffic accident~s~ ({0}) with {1}.~n~Stop and deal with it: press ~INPUT_CONTEXT~ near the scene.",
                            "~o~ДТП~s~ ({0}): {1}.~n~Остановитесь и разберитесь: нажмите ~INPUT_CONTEXT~ у места ДТП.",
                            PoliceRules.SeverityName(s.Severity), r.OtherVehicle));
            Log.Info(string.Format("Accident {0}: {1}, {2}, player {3:0} km/h, other {4:0} km/h, ahead {5:0.00}",
                r.Id, s.Severity, r.OtherVehicle, playerKmh, otherKmh, e.OtherAhead));
        }

        private string OtherStateText(Scene s)
        {
            if (s.Victim != null)
            {
                if (s.Victim.Exists() && s.Victim.IsDead) return "Dead";
                if (s.VictimLeft) return "Left";
                return s.Report != null && s.Report.AmbulanceAttended ? "Treated" : "Injured";
            }
            if (s.Other == OtherState.Fled) return "Fled";
            if (s.OtherDriver == null) return "None";
            if (!s.OtherDriver.Exists()) return "Unknown";
            if (s.OtherDriver.IsDead) return "Dead";
            if (s.OtherDriver.IsInjured || s.OtherDriver.Health < 150) return "Injured";
            return "Unhurt";
        }

        private static string StateName(string state)
        {
            switch (state)
            {
                case "Dead": return L.S("Dead", "Погиб");
                case "Injured": return L.S("Injured", "Ранен");
                case "Fled": return L.S("Fled the scene", "Скрылся");
                case "None": return L.S("No driver (parked)", "Без водителя (припаркован)");
                case "Unhurt": return L.S("Unhurt", "Не пострадал");
                case "Left": return L.S("Walked away from the scene", "Ушёл с места ДТП");
                case "Treated": return L.S("Treated by paramedics", "Помощь медиков оказана");
            }
            return L.S("Unknown", "Неизвестно");
        }

        private bool Injured(Scene s)
        {
            if (s.Victim != null && s.Victim.Exists() && !s.VictimLeft) return true;
            return s.OtherDriver != null && s.OtherDriver.Exists() && (s.OtherDriver.IsDead || s.OtherDriver.IsInjured || s.OtherDriver.Health < 150);
        }

        private bool OtherPartyPresent(Scene s)
        {
            if (s.Victim != null && s.Victim.Exists() && !s.VictimLeft) return true;
            return s.OtherDriver != null && s.OtherDriver.Exists() && s.Other != OtherState.Fled;
        }

        // ------------------------------------------------------------ scene update

        private void UpdateScene(int now)
        {
            Scene s = scene;
            Ped player = GTA.Game.Player.Character;
            if (player.IsDead) { CloseScene("player died"); return; }
            float dist = N.Dist2D(player.Position, s.Position);
            int age = unchecked(now - s.Started);

            UpdateOtherDriver(s, now);
            UpdateVictim(s, now);
            UpdateAmbulance(s, now);
            UpdateInvestigation(s, player, now);

            // The pedestrian walked off and nobody was called: nothing left to handle here.
            if (s.Victim != null && s.VictimLeft && s.Police == null && s.Ambulance == null && age > 15000)
            {
                CloseScene("pedestrian left");
                return;
            }

            // Leaving the scene
            if (dist > 150f && s.Inv != InvState.Done)
            {
                if (s.LeftSince < 0) s.LeftSince = now;
                if (unchecked(now - s.LeftSince) > 3000) { LeftScene(s); return; }
            }
            else s.LeftSince = -1;

            if (s.Inv == InvState.Done && (s.Ambulance == null || s.AmbDone)) { CloseScene("resolved"); return; }
            if (age > 600000) { CloseScene("timeout"); return; }
            if (s.Police == null && s.Ambulance == null && s.Report.InfoExchanged && dist > 60f) { CloseScene("details exchanged"); return; }
        }

        private void UpdateOtherDriver(Scene s, int now)
        {
            Ped d = s.OtherDriver;
            if (d == null || !d.Exists() || d.IsDead || s.Other == OtherState.Fled || s.Other == OtherState.None) return;
            int t = unchecked(now - s.OtherSince);
            switch (s.Other)
            {
                case OtherState.Braking:
                    if (t > 2500)
                    {
                        if (Injured(s)) { s.Other = OtherState.Waiting; s.OtherSince = now; break; } // injured: stays in the car
                        N.Call(N.TASK_LEAVE_VEHICLE, d, s.OtherVehicle, 0);
                        s.Other = OtherState.Exiting;
                        s.OtherSince = now;
                    }
                    break;
                case OtherState.Exiting:
                    if (!d.IsInVehicle() || t > 6000)
                    {
                        N.Scenario(d, "WORLD_HUMAN_STAND_IMPATIENT");
                        s.Other = OtherState.Waiting;
                        s.OtherSince = now;
                    }
                    break;
                case OtherState.Waiting:
                    if (t > 10000 && s.Police == null && main.Config.Settings.NpcCallEmergency
                        && (s.Severity != AccidentSeverity.Minor || Injured(s)) && !d.IsInVehicle())
                    {
                        N.Scenario(d, "WORLD_HUMAN_STAND_MOBILE");
                        s.Other = OtherState.Calling;
                        s.OtherSince = now;
                        s.Report.EmergencyCalledByOther = true;
                        CallPolice(s, s.Severity == AccidentSeverity.Severe, false);
                        if (Injured(s) && s.Ambulance == null) CallAmbulance(s, false);
                        main.Notify(L.S("The other driver called 911.", "Другой водитель позвонил в 911."));
                    }
                    break;
                case OtherState.Calling:
                    if (t > 15000)
                    {
                        N.Scenario(d, "WORLD_HUMAN_STAND_IMPATIENT");
                        s.Other = OtherState.Done;
                    }
                    break;
            }
        }

        // A struck pedestrian: waits for help sitting on the ground, may walk away if only
        // lightly hurt, and a witness calls 911 if the player does not.
        private void UpdateVictim(Scene s, int now)
        {
            Ped v = s.Victim;
            if (v == null || s.VictimLeft) return;
            if (!v.Exists())
            {
                VictimGone(s, L.S("The pedestrian is no longer at the scene.", "Пешехода больше нет на месте ДТП."));
                return;
            }
            int age = unchecked(now - s.Started);
            if (!v.IsDead)
            {
                if (N.Dist2D(v.Position, s.Position) > 40f)
                {
                    VictimGone(s, L.S("The pedestrian walked away from the scene.", "Пешеход ушёл с места ДТП."));
                    return;
                }
                bool ragdoll = N.Call<bool>(N.IS_PED_RAGDOLL, v);
                if (!s.VictimSettled && !ragdoll && age > 3000 && s.AmbStage < 2)
                {
                    s.VictimSettled = true;
                    N.Call(N.CLEAR_PED_TASKS, v);
                    N.Scenario(v, "WORLD_HUMAN_PICNIC"); // sits on the ground, waiting for help
                }
            }
            if (s.BystanderCallAt != 0 && unchecked(now - s.BystanderCallAt) >= 0)
            {
                s.BystanderCallAt = 0;
                bool calledSomething = false;
                if (s.Ambulance == null) { CallAmbulance(s, false); calledSomething |= s.Ambulance != null; }
                if (s.Police == null && (s.Severity != AccidentSeverity.Minor || v.IsDead))
                {
                    CallPolice(s, true, false);
                    calledSomething |= s.Police != null;
                }
                if (calledSomething)
                {
                    s.Report.EmergencyCalledByOther = true;
                    main.Notify(L.S("A witness called 911 for the injured pedestrian.", "Свидетель вызвал 911 к пострадавшему пешеходу."));
                }
            }
        }

        // Pedestrian gone: cancel an ambulance that is still on its way; police (if called) still come.
        private void VictimGone(Scene s, string message)
        {
            s.VictimLeft = true;
            s.BystanderCallAt = 0;
            s.Report.OtherDriverState = "Left";
            if (s.Ambulance != null && !s.AmbDone && s.AmbStage == 0)
            {
                main.Dispatch.SendAway(s.Ambulance);
                s.AmbDone = true;
                message += " " + L.S("The ambulance was cancelled.", "Скорая отменена.");
            }
            N.Release(s.Victim);
            main.Notify(message);
            Log.Info("Accident " + s.Report.Id + ": pedestrian left the scene");
        }

        // ------------------------------------------------------------ emergency services

        public bool CallPoliceIfActive()
        {
            if (scene == null) return false;
            if (scene.Police != null) { main.Notify(L.S("Police are already on the way.", "Полиция уже едет.")); return true; }
            CallPolice(scene, scene.Severity == AccidentSeverity.Severe || Injured(scene), true);
            return true;
        }

        private void CallPolice(Scene s, bool urgent, bool byPlayer)
        {
            if (s.Police != null) return;
            s.Police = main.Dispatch.SendPolice(s.Position, urgent);
            if (s.Police == null)
            {
                if (byPlayer) main.Notify(L.S("No police unit available right now.", "Сейчас нет свободных патрулей."));
                return;
            }
            s.Inv = InvState.Arriving;
            s.InvSince = Environment.TickCount;
            if (byPlayer)
                main.Notify(L.F("911: police dispatched ({0}). ETA about {1} s. Wait at the scene.",
                                "911: полиция выехала ({0}). Прибудет примерно через {1} с. Ждите на месте.",
                    urgent ? L.S("priority", "срочно") : L.S("routine", "в обычном порядке"), main.Dispatch.EtaSeconds(s.Police)));
        }

        private void CallAmbulance(Scene s, bool byPlayer)
        {
            if (s.Ambulance != null) return;
            s.Ambulance = main.Dispatch.SendAmbulance(s.Position, true);
            if (s.Ambulance == null)
            {
                if (byPlayer) main.Notify(L.S("No ambulance available right now.", "Сейчас нет свободной скорой."));
                return;
            }
            if (byPlayer)
                main.Notify(L.F("911: ambulance dispatched. ETA about {0} s.", "911: скорая выехала. Прибудет примерно через {0} с.", main.Dispatch.EtaSeconds(s.Ambulance)));
        }

        private void UpdateAmbulance(Scene s, int now)
        {
            Responder a = s.Ambulance;
            if (a == null || s.AmbDone) return;
            if (!a.Valid) { s.AmbDone = true; return; }
            if (s.AmbSince == 0)
            {
                if (unchecked(now - a.DispatchedAt) > 180000)
                {
                    // Stuck far away (blocked road, water, unreachable scene): give up cleanly.
                    s.AmbDone = true;
                    main.Dispatch.SendAway(a);
                    main.Notify(L.S("The ambulance could not reach the scene.", "Скорая не смогла добраться до места ДТП."));
                    return;
                }
                if (!main.Dispatch.Arrived(a)) return;
                N.Call(N.SET_VEHICLE_HAS_MUTED_SIRENS, a.Vehicle, true);
                N.Call(N.TASK_LEAVE_VEHICLE, a.Driver, a.Vehicle, 0);
                if (a.Partner != null && a.Partner.Exists()) N.Call(N.TASK_LEAVE_VEHICLE, a.Partner, a.Vehicle, 0);
                s.AmbSince = now;
                return;
            }
            int t = unchecked(now - s.AmbSince);
            Ped patient = s.Victim != null && s.Victim.Exists() && !s.VictimLeft ? s.Victim : s.OtherDriver;
            Ped player = GTA.Game.Player.Character;
            bool playerHurt = player.Health < player.MaxHealth - 40;
            Entity treat = patient != null && patient.Exists() && Injured(s) ? (Entity)patient : (playerHurt ? (Entity)player : null);

            // Stage 1: walk to the patient, following them if they moved.
            if (s.AmbStage <= 1 && t > 3000)
            {
                if (s.AmbStage == 0) { s.AmbStage = 1; s.AmbStageSince = now; }
                if (treat == null) { s.AmbStage = 2; s.AmbStageSince = now - 20000; }
                else
                {
                    Vector3 at = treat.Position + new Vector3(0.8f, 0.8f, 0f);
                    if (unchecked(now - s.AmbNextWalk) >= 0)
                    {
                        s.AmbNextWalk = now + 3000;
                        N.WalkTo(a.Driver, at);
                        if (a.Partner != null && a.Partner.Exists()) N.WalkTo(a.Partner, treat.Position + new Vector3(-0.8f, 0.8f, 0f));
                    }
                    if (N.Dist2D(a.Driver.Position, at) < 1.8f || unchecked(now - s.AmbStageSince) > 25000)
                    {
                        // Stage 2: treatment (kneeling medics, no injuries shown).
                        N.Face(a.Driver, treat);
                        N.Scenario(a.Driver, "CODE_HUMAN_MEDIC_TEND_TO_DEAD");
                        if (a.Partner != null && a.Partner.Exists()) N.Scenario(a.Partner, "CODE_HUMAN_MEDIC_KNEEL");
                        s.AmbStage = 2;
                        s.AmbStageSince = now;
                    }
                }
                return;
            }
            if (s.AmbStage == 2 && unchecked(now - s.AmbStageSince) < 16000) return;
            if (s.AmbStage == 2)
            {
                if (patient != null && patient.Exists() && !patient.IsDead && Injured(s)) patient.Health = Math.Max(patient.Health, 180);
                N.Call(N.CLEAR_PED_TASKS, a.Driver);
                if (a.Partner != null && a.Partner.Exists()) N.Call(N.CLEAR_PED_TASKS, a.Partner);
                // A treated pedestrian is taken to hospital in the ambulance.
                if (s.Victim != null && s.Victim.Exists() && !s.Victim.IsDead && !s.VictimLeft)
                {
                    N.Call(N.CLEAR_PED_TASKS, s.Victim);
                    N.Call(N.TASK_ENTER_VEHICLE, s.Victim, a.Vehicle, 15000, 2, 1.0f, 1, 0);
                    s.AmbStage = 3;
                    s.AmbStageSince = now;
                    return;
                }
                s.AmbStage = 4;
            }
            if (s.AmbStage == 3 && unchecked(now - s.AmbStageSince) < 15000 && s.Victim != null && s.Victim.Exists() && !s.Victim.IsInVehicle(a.Vehicle))
                return; // boarding

            // Done: treat the player too if hurt and close by, then the crew leaves.
            if (playerHurt && player.Position.DistanceTo(a.Driver.Position) < 8f)
            {
                player.Health = player.MaxHealth;
                int bill = Math.Min(250, GTA.Game.Player.Money);
                if (bill > 0)
                {
                    GTA.Game.Player.Money -= bill;
                    main.Saves.Data.AddTransaction("Medical", L.S("Paramedic treatment", "Помощь медиков"), -bill);
                    main.Notify(L.F("Paramedics treated you. Bill: {0}.", "Медики оказали вам помощь. Счёт: {0}.", L.Money(bill)));
                }
            }
            s.AmbStage = 4;
            s.Report.AmbulanceAttended = true;
            s.Report.OtherDriverState = OtherStateText(s);
            if (s.Victim != null && s.Victim.Exists() && s.Victim.IsInVehicle(a.Vehicle))
                main.Notify(L.S("Paramedics are taking the pedestrian to hospital.", "Медики увозят пешехода в больницу."));
            s.AmbDone = true;
            main.Dispatch.SendAway(a);
        }

        // ------------------------------------------------------------ investigation

        private void UpdateInvestigation(Scene s, Ped player, int now)
        {
            Responder u = s.Police;
            if (u == null || s.Inv == InvState.None || s.Inv == InvState.Done) return;
            if (!u.Valid) { s.Inv = InvState.Done; return; }
            Ped cop = u.Driver;
            int t = unchecked(now - s.InvSince);

            switch (s.Inv)
            {
                case InvState.Arriving:
                    if (main.Dispatch.Arrived(u) || (N.Dist2D(u.Vehicle.Position, s.Position) < 30f && u.Vehicle.Speed < 1f))
                    {
                        N.Call(N.SET_VEHICLE_HAS_MUTED_SIRENS, u.Vehicle, true);
                        N.Siren(u.Vehicle, true); // lights on at the scene
                        N.Call(N.TASK_LEAVE_VEHICLE, cop, u.Vehicle, 0);
                        Inv(s, InvState.Exiting, now);
                    }
                    break;
                case InvState.Exiting:
                    if (!cop.IsInVehicle() || t > 8000)
                    {
                        if (s.OtherVehicle != null && s.OtherVehicle.Exists()) WalkCop(s, cop, s.OtherVehicle.GetOffsetPosition(new Vector3(2.2f, 1.5f, 0f)));
                        Inv(s, s.OtherVehicle != null && s.OtherVehicle.Exists() ? InvState.InspectOther : InvState.InspectPlayer, now);
                        if (s.Inv == InvState.InspectPlayer) WalkToPlayerVehicle(s, cop);
                    }
                    break;
                case InvState.InspectOther:
                    if (Near(cop, s.WalkTarget) && t < 20000 && !scenarioStarted) { N.Scenario(cop, "WORLD_HUMAN_CLIPBOARD"); scenarioStarted = true; scenarioAt = now; }
                    if ((scenarioStarted && unchecked(now - scenarioAt) > 6000) || t > 26000)
                    {
                        N.Call(N.CLEAR_PED_TASKS, cop);
                        WalkToPlayerVehicle(s, cop);
                        Inv(s, InvState.InspectPlayer, now);
                    }
                    break;
                case InvState.InspectPlayer:
                    if (Near(cop, s.WalkTarget) && !scenarioStarted) { N.Scenario(cop, "WORLD_HUMAN_CLIPBOARD"); scenarioStarted = true; scenarioAt = now; }
                    if ((scenarioStarted && unchecked(now - scenarioAt) > 5000) || t > 25000)
                    {
                        N.Call(N.CLEAR_PED_TASKS, cop);
                        if (s.OtherDriver != null && s.OtherDriver.Exists() && !s.OtherDriver.IsDead && s.Other != OtherState.Fled)
                        {
                            WalkCop(s, cop, s.OtherDriver.Position + new Vector3(1.2f, 0f, 0f));
                            Inv(s, InvState.TalkOther, now);
                        }
                        else Inv(s, InvState.TalkPlayer, now);
                    }
                    break;
                case InvState.TalkOther:
                    if (Near(cop, s.WalkTarget) && !scenarioStarted) { N.Face(cop, s.OtherDriver); scenarioStarted = true; scenarioAt = now; }
                    if ((scenarioStarted && unchecked(now - scenarioAt) > 5000) || t > 20000) Inv(s, InvState.TalkPlayer, now);
                    break;
                case InvState.TalkPlayer:
                    Vector3 at = player.Position + new Vector3(1.0f, 0.6f, 0f);
                    if (N.Dist2D(at, s.WalkTarget) > 2f || t < 300) WalkCop(s, cop, at);
                    if (cop.Position.DistanceTo(player.Position) < 2.4f || t > 30000)
                    {
                        N.Face(cop, player);
                        Inv(s, InvState.Talking, now);
                        OpenInvestigationMenu();
                    }
                    break;
                case InvState.Talking:
                    break;
                case InvState.Writing:
                    if (t > 6000)
                    {
                        N.Call(N.CLEAR_PED_TASKS, cop);
                        main.Dispatch.SendAway(u);
                        s.Inv = InvState.Done;
                    }
                    break;
            }
        }

        private bool scenarioStarted;
        private int scenarioAt;

        private void Inv(Scene s, InvState st, int now)
        {
            s.Inv = st;
            s.InvSince = now;
            scenarioStarted = false;
        }

        private static void WalkCop(Scene s, Ped cop, Vector3 at)
        {
            s.WalkTarget = at;
            N.WalkTo(cop, at);
        }

        private static void WalkToPlayerVehicle(Scene s, Ped cop)
        {
            if (s.PlayerVehicle != null && s.PlayerVehicle.Exists())
                WalkCop(s, cop, s.PlayerVehicle.GetOffsetPosition(new Vector3(2.2f, 1.5f, 0f)));
            else
                WalkCop(s, cop, s.Position);
        }

        private static bool Near(Ped p, Vector3 at)
        {
            return N.Dist2D(p.Position, at) < 2.0f;
        }

        // ------------------------------------------------------------ leaving / closing

        private void LeftScene(Scene s)
        {
            s.Report.LeftScene = true;
            bool serious = s.Severity != AccidentSeverity.Minor || s.Victim != null;
            if (serious && OtherPartyPresent(s))
            {
                RlsSettings set = main.Config.Settings;
                bool policeHere = s.Police != null && (s.Inv != InvState.Arriving);
                bool identified = policeHere || rng.NextDouble() < 0.6; // the other driver noted the plate
                // Automatic / Witnessed: a reported plate is enough evidence. Always: always fined.
                bool fined = set.Fines == FineMode.Always
                          || ((set.Fines == FineMode.Witnessed || set.Fines == FineMode.Automatic) && identified);
                double fine = fined ? Math.Round(TrafficRules.BaseFine(ViolationType.LeavingScene, 0) * set.FineMultiplier) : 0;
                main.Traffic.AddViolation(ViolationType.LeavingScene, s.Report.Id, fine, identified,
                    fine > 0 ? PoliceRules.PointsFor(ViolationType.LeavingScene, 0) : 0, false, s.Report.Location);
                s.Report.Fine += fine;
                main.Notify(fine > 0
                    ? L.F("~r~You left the scene of an accident.~s~ Your plate was reported. Fine: {0}.", "~r~Вы скрылись с места ДТП.~s~ Ваш номер передан полиции. Штраф: {0}.", L.Money(fine))
                    : L.S("You left the scene of an accident. It has been recorded.", "Вы уехали с места ДТП. Это записано."));

                // Hit-and-run involving a pedestrian: licence revoked; a fatal one is a crime.
                if (s.Victim != null && identified && fine > 0)
                {
                    DriverProfile prof = main.CurrentProfile();
                    double play = main.Saves.Data.Stats.TotalPlayTimeSeconds;
                    LicenceStatus before = LicenceRules.Status(prof, play);
                    LicenceRules.Revoke(prof, play, L.S("Hit-and-run with a pedestrian", "Скрылся после наезда на пешехода"));
                    main.AnnounceLicenceChange(before, prof);
                    if (s.Victim.Exists() && s.Victim.IsDead) N.RaiseWanted(1);
                }
            }
            CloseScene("left scene");
        }

        private void CloseScene(string reason)
        {
            Scene s = scene;
            scene = null;
            if (s == null) return;
            if (main.Menu.IsOpen && (s.Inv == InvState.Talking || menuIsAccident)) main.Menu.Close();
            menuIsAccident = false;
            s.Report.Closed = true;
            s.Report.OtherDriverState = OtherStateText(s);
            if (s.Police != null && s.Inv != InvState.Done) main.Dispatch.SendAway(s.Police);
            if (s.Ambulance != null && !s.AmbDone) main.Dispatch.SendAway(s.Ambulance);
            if (s.OtherDriver != null && s.OtherDriver.Exists() && !s.OtherDriver.IsDead && s.OtherVehicle != null && s.OtherVehicle.Exists())
            {
                N.Call(N.CLEAR_PED_TASKS, s.OtherDriver);
                if (s.OtherDriver.IsInVehicle(s.OtherVehicle)) N.Call(N.TASK_VEHICLE_DRIVE_WANDER, s.OtherDriver, s.OtherVehicle, 12f, N.StyleNormal);
                else N.Call(N.TASK_ENTER_VEHICLE, s.OtherDriver, s.OtherVehicle, 20000, -1, 1.0f, 1, 0);
            }
            N.Release(s.OtherDriver);
            N.Release(s.OtherVehicle);
            N.Release(s.Victim);
            main.Saves.Save();
            Log.Info("Accident " + s.Report.Id + " closed: " + reason);
        }

        // ------------------------------------------------------------ frame: prompt

        private bool menuIsAccident;

        public void OnFrame()
        {
            Scene s = scene;
            if (s == null || main.Menu.IsOpen) return;
            Ped p = GTA.Game.Player.Character;
            if (p == null || !p.Exists() || p.IsDead) return;

            bool inTalk = s.Inv == InvState.Talking && s.Police != null && s.Police.Valid && p.Position.DistanceTo(s.Police.Driver.Position) < 6f;
            bool near;
            Vehicle v = p.CurrentVehicle;
            if (v != null) near = v.Speed < 1f && N.Dist2D(p.Position, s.Position) < 35f;
            else
            {
                near = N.Dist2D(p.Position, s.Position) < 25f
                    || (s.OtherVehicle != null && s.OtherVehicle.Exists() && p.Position.DistanceTo(s.OtherVehicle.Position) < 10f);
            }
            if (!near && !inTalk) return;

            GTA.UI.Screen.ShowHelpTextThisFrame(inTalk
                ? L.S("Press ~INPUT_CONTEXT~ to talk to the officer.", "Нажмите ~INPUT_CONTEXT~, чтобы поговорить с полицейским.")
                : L.S("Press ~INPUT_CONTEXT~ to deal with the accident.", "Нажмите ~INPUT_CONTEXT~, чтобы разобраться с ДТП."));
            if (NativeInput.JustPressed(GTA.Control.Context))
            {
                if (inTalk) OpenInvestigationMenu(); else OpenSceneMenu();
            }
        }

        // ------------------------------------------------------------ scene menu

        private void OpenSceneMenu()
        {
            if (scene == null) return;
            menuIsAccident = true;
            main.Menu.Open(BuildSceneMenu());
        }

        private Menu BuildSceneMenu()
        {
            Scene s = scene;
            AccidentReport r = s.Report;
            Menu m = new Menu(L.S("Traffic accident", "ДТП"), PoliceRules.SeverityName(s.Severity) + "  -  " + r.Location);
            m.Add(new MenuItem(L.S("Other party", "Другой участник"), null).WithValue(() => r.OtherVehicle + (r.OtherPlate.Length > 0 ? "  " + r.OtherPlate : "")));
            m.Add(new MenuItem(L.S("Their condition", "Его состояние"), null).WithValue(() => StateName(OtherStateText(s))));
            m.Add(new MenuItem(L.S("Your vehicle damage", "Повреждения вашей машины"), null).WithValue(() =>
                s.PlayerVehicle != null && s.PlayerVehicle.Exists()
                    ? L.Money(PoliceRules.DamageEstimate(s.PlayerVehicle.BodyHealth, s.PlayerVehicle.EngineHealth, 1.0))
                    : "-").WithDescription(L.S("Rough repair estimate. Owned cars can be repaired at a repair shop.",
                                               "Примерная стоимость ремонта. Свою машину можно отремонтировать в автосервисе.")));
            m.Add(new MenuItem(L.S("Emergency services", "Экстренные службы"), null).WithValue(() => ServicesText(s)));

            m.Add(new MenuItem(s.Victim != null ? L.S("Check on the pedestrian", "Проверить пешехода") : L.S("Check on the other driver", "Проверить другого водителя"), () =>
            {
                string st = OtherStateText(s);
                PoliceStopSystem.Say(st == "Left" ? L.S("The pedestrian has left.", "Пешеход ушёл.")
                    : st == "Treated" ? L.S("\"The paramedics have looked after me.\"", "«Медики мне помогли.»")
                    : st == "Injured" ? L.S("\"I'm hurt... please call an ambulance.\"", "«Мне плохо... вызовите скорую, пожалуйста.»")
                    : st == "Dead" ? L.S("The other person is not responding.", "Другой человек не реагирует.")
                    : st == "Unhurt" ? L.S("\"I'm okay. Look at my car though!\"", "«Я в порядке. Но посмотрите на мою машину!»")
                    : L.S("There is nobody to talk to.", "Здесь не с кем говорить."));
            }));

            m.Add(new MenuItem(L.S("Exchange information", "Обменяться данными"), () =>
            {
                Random rr = new Random(r.Id.GetHashCode());
                r.OtherDriver = PoliceRules.RandomName(rr);
                r.OtherInsurer = PoliceRules.RandomInsurer(rr);
                r.OtherPolicy = PoliceRules.RandomPolicy(rr);
                r.InfoExchanged = true;
                DriverProfile prof = main.CurrentProfile();
                if (s.OtherDriver != null && s.OtherDriver.Exists() && !s.OtherDriver.IsInVehicle())
                    N.Call(N.TASK_GO_TO_ENTITY, s.OtherDriver, GTA.Game.Player.Character, -1, 1.8f, 1.0f, 0f, 0);
                PoliceStopSystem.Say(L.F("\"I'm {0}. Insured with {1}, policy {2}.\" You give your details: {3}, {4}.",
                                         "«Я {0}. Страховка {1}, полис {2}.» Вы передаёте свои данные: {3}, {4}.",
                    r.OtherDriver, r.OtherInsurer, r.OtherPolicy, prof.Insurer, prof.PolicyNumber));
                main.Menu.Replace(BuildSceneMenu());
            }).WithEnabled(() => !r.InfoExchanged && s.OtherDriver != null && s.OtherDriver.Exists() && !s.OtherDriver.IsDead && s.Other != OtherState.Fled)
              .WithValue(() => r.InfoExchanged ? r.OtherDriver : ""));

            m.Add(new MenuItem(L.S("Call police (911)", "Вызвать полицию (911)"), () =>
            {
                CallPolice(s, s.Severity == AccidentSeverity.Severe || Injured(s), true);
                main.Menu.Replace(BuildSceneMenu());
            }).WithEnabled(() => s.Police == null)
              .WithDescription(L.S("Police drive to the scene, investigate and file a report.", "Полиция приедет, проведёт разбирательство и составит протокол.")));

            m.Add(new MenuItem(L.S("Request an ambulance", "Вызвать скорую помощь"), () =>
            {
                CallAmbulance(s, true);
                main.Menu.Replace(BuildSceneMenu());
            }).WithEnabled(() => s.Ambulance == null && (Injured(s) || GTA.Game.Player.Character.Health < GTA.Game.Player.Character.MaxHealth - 40))
              .WithDescription(L.S("Available when someone is hurt.", "Доступно, если есть пострадавшие.")));

            m.Add(new MenuItem(L.S("Wait for emergency services", "Ждать экстренные службы"), () =>
            {
                main.Menu.Close();
                PoliceStopSystem.Say(L.S("Wait near the scene. The officer will come to you.", "Ждите у места ДТП. Полицейский подойдёт к вам."));
            }).WithEnabled(() => s.Police != null || s.Ambulance != null));

            m.Add(new MenuItem(L.S("Leave the scene", "Покинуть место ДТП"), () => main.Menu.Close())
                .WithDescription(s.Severity != AccidentSeverity.Minor || s.Victim != null
                    ? L.S("Driving away from a serious accident is an offence (fine and licence points).", "Уезжать с места серьёзного ДТП - нарушение (штраф и баллы).")
                    : L.S("Minor accident: you may drive on, but it is recorded.", "Лёгкое ДТП: можно уехать, но это будет записано.")));
            return m;
        }

        private string ServicesText(Scene s)
        {
            List<string> parts = new List<string>();
            if (s.Police != null)
            {
                if (s.Inv == InvState.Arriving && s.Police.Valid) parts.Add(L.F("police ~{0} s", "полиция ~{0} с", main.Dispatch.EtaSeconds(s.Police)));
                else parts.Add(L.S("police on scene", "полиция на месте"));
            }
            if (s.Ambulance != null)
            {
                if (s.AmbDone) parts.Add(L.S("ambulance done", "скорая уехала"));
                else if (s.AmbSince == 0 && s.Ambulance.Valid) parts.Add(L.F("ambulance ~{0} s", "скорая ~{0} с", main.Dispatch.EtaSeconds(s.Ambulance)));
                else parts.Add(L.S("ambulance on scene", "скорая на месте"));
            }
            return parts.Count == 0 ? L.S("not called", "не вызваны") : string.Join(", ", parts.ToArray());
        }

        // ------------------------------------------------------------ investigation menu

        private void OpenInvestigationMenu()
        {
            if (scene == null || scene.Police == null) return;
            menuIsAccident = true;
            Menu m = scene.Decided ? null : BuildInvestigationMenu();
            if (m == null) return;
            if (main.Menu.IsOpen) main.Menu.Replace(m); else main.Menu.Open(m);
            PoliceStopSystem.Say(L.S("I'm investigating this collision. Tell me what happened.", "Я разбираюсь в этом ДТП. Расскажите, что произошло."));
        }

        private Menu BuildInvestigationMenu()
        {
            Scene s = scene;
            DriverProfile prof = main.CurrentProfile();
            double play = main.Saves.Data.Stats.TotalPlayTimeSeconds;
            Menu m = new Menu(L.S("Accident investigation", "Разбор ДТП"), s.Police.Name);
            m.Add(new MenuItem(L.S("Report", "Протокол"), null).WithValue(() => s.Report.Id));

            AddStatement(m, s, Statement.OtherAtFault, L.S("\"The other driver caused it.\"", "«Виноват другой водитель.»"));
            AddStatement(m, s, Statement.MyFault, L.S("\"It was my fault.\"", "«Это моя вина.»"));
            AddStatement(m, s, Statement.DontKnow, L.S("\"I don't know what happened.\"", "«Не знаю, как это случилось.»"));

            m.Add(new MenuItem(L.S("Hand over driving licence", "Передать водительское удостоверение"), () =>
            {
                s.LicenceShown = true;
                // 0.6.0 fix: the officer used to accept any licence. Status comes from LicenceRules.
                PoliceStopSystem.Say(LicenceRules.OfficerLine(prof, main.Saves.Data.Stats.TotalPlayTimeSeconds));
                main.Menu.Replace(BuildInvestigationMenu());
            }).WithEnabled(() => !s.LicenceShown && !s.Refused)
              .WithValue(() => s.LicenceShown ? LicenceRules.StatusName(LicenceRules.Status(prof, main.Saves.Data.Stats.TotalPlayTimeSeconds)) : ""));
            m.Add(new MenuItem(L.S("Hand over registration and insurance", "Передать регистрацию и страховку"), () =>
            {
                Vehicle v = s.PlayerVehicle;
                if (v != null && v.Exists() && N.Call<bool>(N.IS_VEHICLE_STOLEN, v))
                {
                    PoliceStopSystem.Say(L.S("This vehicle is reported stolen! Hands where I can see them!", "Эта машина в угоне! Руки на виду!"));
                    main.Menu.Close();
                    N.RaiseWanted(2);
                    s.Inv = InvState.Done;
                    Responder u = s.Police;
                    if (u.Blip != null && u.Blip.Exists()) u.Blip.Delete();
                    main.Dispatch.SendAway(u);
                    return;
                }
                PoliceStopSystem.Say(L.F("{0}, policy {1}. In order.", "{0}, полис {1}. В порядке.", prof.Insurer, prof.PolicyNumber));
            }));
            m.Add(new MenuItem(L.S("Refuse to show documents", "Отказаться предъявить документы"), () =>
            {
                s.Refused = true;
                main.Menu.Replace(BuildInvestigationMenu());
            }).WithEnabled(() => !s.LicenceShown && !s.Refused));

            m.Add(new MenuItem(L.S("Let the officer write the report", "Дать офицеру составить протокол"), Decide)
                .WithEnabled(() => s.Statement != Statement.None && (s.LicenceShown || s.Refused))
                .WithDescription(L.S("Give a statement and show your licence first.", "Сначала дайте показания и покажите права.")));
            return m;
        }

        private void AddStatement(Menu m, Scene s, Statement st, string label)
        {
            m.Add(new MenuItem(label, () =>
            {
                s.Statement = st;
                s.Report.Statement = st.ToString();
                PoliceStopSystem.Say(L.S("Noted.", "Записал."));
                main.Menu.Replace(BuildInvestigationMenu());
            }).WithValue(() => s.Statement == st ? L.S("given", "дано") : "")
              .WithEnabled(() => s.Statement == Statement.None));
        }

        private void Decide()
        {
            Scene s = scene;
            RlsSettings set = main.Config.Settings;
            DriverProfile prof = main.CurrentProfile();
            double play = main.Saves.Data.Stats.TotalPlayTimeSeconds;
            AccidentReport r = s.Report;
            s.Decided = true;

            Fault f = PoliceRules.DetermineFault(s.Evidence);
            bool lie = PoliceRules.IsFalseStatement(s.Statement, f, s.Evidence);
            List<ViolationRecord> issued = new List<ViolationRecord>();

            LicenceStatus licence = LicenceRules.Status(prof, play); // checked even when not handed over
            bool pedestrian = s.Victim != null;
            bool victimDead = pedestrian && s.Victim.Exists() && s.Victim.IsDead;
            if (pedestrian)
            {
                double pf = PedestrianFine(s.Severity, f, victimDead) * set.FineMultiplier;
                if (pf > 0)
                {
                    int pts = PoliceRules.PointsFor(ViolationType.HitPedestrian, 0);
                    if (f == Fault.Shared) pts = Math.Max(1, pts / 2);
                    issued.Add(main.Traffic.AddViolation(ViolationType.HitPedestrian, r.Id, Math.Round(pf), true, pts, false, r.Location));
                }
            }
            else
            {
                double fine = Math.Round(PoliceRules.CollisionFine(s.Severity, f) * set.FineMultiplier);
                if (fine > 0)
                {
                    int pts = s.Severity == AccidentSeverity.Severe ? 6 : PoliceRules.PointsFor(ViolationType.CausingCollision, 0);
                    if (f == Fault.Shared) pts = Math.Max(1, pts / 2);
                    issued.Add(main.Traffic.AddViolation(ViolationType.CausingCollision, r.Id, fine, true, pts, false, r.Location));
                }
            }
            if (lie) issued.Add(main.Traffic.AddViolation(ViolationType.FalseStatement, r.Id,
                Math.Round(TrafficRules.BaseFine(ViolationType.FalseStatement, 0) * set.FineMultiplier), true, PoliceRules.PointsFor(ViolationType.FalseStatement, 0), false, r.Location));
            if (s.Refused) issued.Add(main.Traffic.AddViolation(ViolationType.NoDocuments, r.Id,
                Math.Round(TrafficRules.BaseFine(ViolationType.NoDocuments, 0) * set.FineMultiplier), true, 0, false, r.Location));
            if (licence != LicenceStatus.Valid)
            {
                ViolationType lt = licence == LicenceStatus.Revoked ? ViolationType.DrivingWhileRevoked : ViolationType.SuspendedLicence;
                issued.Add(main.Traffic.AddViolation(lt, r.Id, Math.Round(TrafficRules.BaseFine(lt, 0) * set.FineMultiplier), true, 0, false, r.Location));
                LicenceRules.CaughtDriving(prof, play);
            }
            // Pedestrian at fault: a fatal collision revokes the licence, a severe one suspends it.
            if (pedestrian && f == Fault.Player)
            {
                if (victimDead) LicenceRules.Revoke(prof, play, L.S("Fatal collision with a pedestrian", "ДТП со смертью пешехода"));
                else if (s.Severity == AccidentSeverity.Severe) LicenceRules.Suspend(prof, play, LicenceRules.PoliceSuspensionSeconds, L.S("Severe collision with a pedestrian", "Тяжёлый наезд на пешехода"));
            }
            main.AnnounceLicenceChange(licence, prof);

            prof.Reputation = PoliceRules.ReputationForAccident(prof.Reputation, s.Severity, f);

            double total = 0;
            foreach (ViolationRecord v in issued) total += v.Fine;
            r.Fault = f.ToString();
            r.Officer = s.Police.Name;
            r.PoliceAttended = true;
            r.Fine += total;
            if (s.PlayerVehicle != null && s.PlayerVehicle.Exists())
                r.PlayerDamageEstimate = PoliceRules.DamageEstimate(s.PlayerVehicle.BodyHealth, s.PlayerVehicle.EngineHealth, 1.0);
            r.Findings = Findings(s, f, lie);
            r.OtherDriverState = OtherStateText(s);

            PoliceStopSystem.Say(f == Fault.Player ? L.S("The evidence shows you caused this collision.", "Доказательства показывают, что ДТП вызвали вы.")
                : f == Fault.Other ? L.S("The other driver is at fault. You're free to go.", "Виноват другой водитель. Вы можете ехать.")
                : f == Fault.Shared ? L.S("You share responsibility for this collision.", "Ответственность за ДТП обоюдная.")
                : L.S("I couldn't determine fault.", "Установить виновника не удалось."));
            Log.Info("Accident " + r.Id + " investigated: fault " + f + ", fines " + total + (lie ? ", false statement" : ""));
            main.Menu.Replace(BuildInvestigationOutcome(issued, total));
        }

        private static double PedestrianFine(AccidentSeverity sev, Fault f, bool dead)
        {
            return PoliceRules.PedestrianFine(sev, f, dead);
        }

        private string Findings(Scene s, Fault f, bool lie)
        {
            CollisionEvidence e = s.Evidence;
            string text = L.F("Your speed {0:0} km/h, other {1:0} km/h.", "Ваша скорость {0:0} км/ч, другого участника {1:0} км/ч.", e.PlayerSpeedKmh, e.OtherSpeedKmh);
            if (e.PedestrianStruck) text += " " + L.S("A pedestrian was struck.", "Сбит пешеход.");
            else if (e.OtherWasStopped) text += " " + L.S("The other vehicle was stationary.", "Другая машина стояла.");
            else if (e.OtherAhead > 0.6) text += " " + L.S("Rear-end impact: the other vehicle was ahead of you.", "Удар сзади: другая машина была впереди вас.");
            else if (e.OtherAhead < -0.6) text += " " + L.S("You were hit from behind.", "Удар пришёлся вам сзади.");
            if (e.PlayerViolationJustBefore) text += " " + L.S("You committed a traffic violation just before the crash.", "Перед ДТП вы нарушили ПДД.");
            if (lie) text += " " + L.S("Your statement contradicted the evidence.", "Ваши показания противоречат доказательствам.");
            return text;
        }

        private Menu BuildInvestigationOutcome(List<ViolationRecord> issued, double total)
        {
            Scene s = scene;
            AccidentReport r = s.Report;
            Menu m = new Menu(L.S("Accident report", "Протокол ДТП"), r.Id);
            m.Add(new MenuItem(L.S("Fault", "Виновник"), null).WithValue(() => PoliceRules.FaultName((Fault)Enum.Parse(typeof(Fault), r.Fault))));
            m.Add(new MenuItem(L.S("Findings (details below)", "Выводы (подробности ниже)"), null).WithDescription(r.Findings));
            foreach (ViolationRecord v in issued)
            {
                ViolationRecord rec = v;
                m.Add(new MenuItem(TrafficRules.NameFromString(rec.Type), null).WithValue(() =>
                    L.Money(rec.Fine) + (rec.Points > 0 ? L.F(", {0} pt", ", {0} б.", rec.Points) : "") + (rec.Paid ? L.S(" (paid)", " (оплачен)") : "")));
            }
            if (total > 0)
            {
                m.Add(new MenuItem(L.F("Pay now ({0})", "Оплатить сейчас ({0})", L.Money(total)), () =>
                {
                    if (GTA.Game.Player.Money < total) { main.Notify(L.S("Not enough cash.", "Недостаточно наличных.")); return; }
                    GTA.Game.Player.Money -= (int)Math.Round(total);
                    foreach (ViolationRecord v in issued)
                    {
                        if (v.Paid || v.Fine <= 0) continue;
                        v.Paid = true;
                        main.Saves.Data.AddTransaction("Fine", TrafficRules.NameFromString(v.Type) + " (" + r.Id + ")", -Math.Round(v.Fine));
                    }
                    main.Saves.Data.Driving.FinesPaid += Math.Round(total);
                    r.FinePaid = true;
                    FinishInvestigation();
                }).WithEnabled(() => !r.FinePaid && GTA.Game.Player.Money >= total));
            }
            m.Add(new MenuItem(total > 0 ? L.S("Pay later", "Оплатить позже") : L.S("Thank the officer", "Поблагодарить офицера"), FinishInvestigation)
                .WithDescription(L.S("The report is saved in the Life Hub under Accident reports.", "Протокол сохранён в центре управления в разделе «Протоколы ДТП».")));
            return m;
        }

        private void FinishInvestigation()
        {
            Scene s = scene;
            if (s == null) return;
            main.Menu.Close();
            menuIsAccident = false;
            PoliceStopSystem.Say(L.S("The report is filed. Drive safely.", "Протокол составлен. Будьте осторожны на дороге."));
            if (s.Police != null && s.Police.Valid) N.Scenario(s.Police.Driver, "WORLD_HUMAN_CLIPBOARD");
            Inv(s, InvState.Writing, Environment.TickCount);
            main.Saves.Save();
        }
    }
}
