// Realistic Life Simulator - accidents between NPC drivers.
//
// Near the player (full simulation): both drivers brake and get out (unless hurt), look at
// their cars, walk over and exchange details. Aggressive drivers sometimes flee. Minor
// crashes end there; serious ones (or a hit-and-run) make a driver call 911: police drive
// in, inspect both cars, talk to both drivers and write the report; an ambulance comes
// for the injured. Traffic around the scene is slowed with a road speed zone.
// Far from the player (simplified): the outcome is resolved without simulating people,
// and all entities are handed back to the game.
// Every scene is logged as a city incident.

using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using RealisticLifeSimulator.Core;

namespace RealisticLifeSimulator.Game
{
    public sealed class NpcAccidentSystem : IModule, IFrameModule
    {
        private enum Step { Braking, Exiting, Inspecting, Exchanging, Waiting, Investigating, Clearing, Done }

        private sealed class Scene
        {
            public Vehicle A, B;
            public Ped DA, DB;
            public bool FledA, FledB;
            public DriverPersonality PA, PB;
            public Vector3 Position;
            public AccidentSeverity Severity;
            public CollisionEvidence Evidence;   // from A's point of view
            public string Location;
            public Step Step;
            public int StepSince;
            public int Started;
            public int SpeedZone = -1;
            public bool Called;
            public Responder Police, Ambulance;
            public int InvPhase;
            public int InvPhaseSince;
            public int AmbSince;
            public bool AmbDone;
            public bool Witness;
        }

        private readonly RlsMain main;
        private readonly Random rng = new Random();
        private readonly List<Scene> scenes = new List<Scene>();

        public NpcAccidentSystem(RlsMain main) { this.main = main; }

        public string Name { get { return "NpcAccidents"; } }
        public int IntervalMs { get { return 500; } }
        public int Active { get { return scenes.Count; } }

        private int MaxScenes
        {
            get
            {
                switch (main.Performance.Profile.Kind)
                {
                    case PerformanceProfileKind.High: return 3;
                    case PerformanceProfileKind.Balanced: return 2;
                }
                return 1;
            }
        }

        public void Start() { }

        // True while one of this system's scenes owns the vehicle (used by passenger rides).
        public bool IsInvolved(Vehicle v)
        {
            if (v == null) return false;
            foreach (Scene s in scenes) if (s.A == v || s.B == v) return true;
            return false;
        }

        public void Shutdown()
        {
            foreach (Scene s in scenes.ToArray()) Close(s, null);
            scenes.Clear();
        }

        // ------------------------------------------------------------ new accident

        public void Report(Vehicle a, Vehicle b, double speedA, double speedB, float damage)
        {
            if (!main.Config.Settings.NpcAccidents || N.InMission()) return;
            foreach (Scene s in scenes)
                if (s.A == a || s.B == a || s.A == b || s.B == b || N.Dist2D(s.Position, a.Position) < 20f) return;

            Scene sc = new Scene();
            sc.A = a;
            sc.B = b;
            sc.DA = a.Driver != null && a.Driver.Exists() && !a.Driver.IsPlayer ? a.Driver : null;
            sc.DB = b.Driver != null && b.Driver.Exists() && !b.Driver.IsPlayer ? b.Driver : null;
            sc.PA = main.NpcTraffic.PersonalityOf(a);
            sc.PB = main.NpcTraffic.PersonalityOf(b);
            sc.Position = (a.Position + b.Position) * 0.5f;
            sc.Started = Environment.TickCount;
            sc.Location = main.Traffic.DescribeLocation(sc.Position);

            CollisionEvidence e = new CollisionEvidence();
            e.PlayerSpeedKmh = speedA;   // "player" = vehicle A in the shared rules
            e.OtherSpeedKmh = speedB;
            e.OtherWasStopped = speedB < 3;
            Vector3 rel = b.Position - a.Position;
            e.OtherAhead = rel.Length() > 0.1f ? Vector3.Dot(rel.Normalized, a.ForwardVector) : 0;
            sc.Evidence = e;
            bool injury = Hurt(sc.DA) || Hurt(sc.DB);
            bool death = (sc.DA != null && sc.DA.IsDead) || (sc.DB != null && sc.DB.IsDead);
            sc.Severity = PoliceRules.Severity(Math.Max(speedA, speedB), damage, injury, death);
            main.Saves.Data.Driving.NpcAccidentsSeen++;

            bool full = scenes.Count < MaxScenes && GTA.Game.Player.Character.Position.DistanceTo(sc.Position) < 250f;
            if (!full)
            {
                main.Saves.Data.AddIncident("Accident", sc.Location, Describe(sc),
                    L.S("Resolved without police (off-screen)", "Разобрались без полиции (вне поля зрения)"));
                Log.Debug("NPC accident logged (simplified): " + sc.Location);
                return;
            }

            Take(sc.DA, a, sc.PA, sc.Severity, out sc.FledA);
            Take(sc.DB, b, sc.PB, sc.Severity, out sc.FledB);
            if (sc.FledA && sc.FledB) { sc.FledB = false; }
            sc.SpeedZone = N.Call<int>(N.ADD_ROAD_NODE_SPEED_ZONE, sc.Position.X, sc.Position.Y, sc.Position.Z, 28f, 3f, false);
            sc.Step = Step.Braking;
            sc.StepSince = sc.Started;
            scenes.Add(sc);
            Log.Info(string.Format("NPC accident ({0}) at {1}: {2} vs {3}{4}", sc.Severity, sc.Location,
                N.VehicleName(a), N.VehicleName(b), sc.FledA || sc.FledB ? ", one driver fled" : ""));
        }

        private void Take(Ped d, Vehicle v, DriverPersonality p, AccidentSeverity sev, out bool fled)
        {
            fled = false;
            v.IsPersistent = true;
            if (d == null || d.IsDead) return;
            d.IsPersistent = true;
            N.Block(d);
            if (sev != AccidentSeverity.Severe && !Hurt(d) && rng.NextDouble() < NpcRules.Traits(p).FleeAfterCrash)
            {
                N.Call(N.TASK_VEHICLE_DRIVE_WANDER, d, v, 30f, N.StyleUrgent);
                fled = true;
                return;
            }
            N.Call(N.TASK_VEHICLE_TEMP_ACTION, d, v, 6, 60000);
        }

        private static bool Hurt(Ped p)
        {
            return p != null && p.Exists() && (p.IsDead || p.IsInjured || p.Health < 150);
        }

        private static bool Present(Ped p, bool fled)
        {
            return p != null && p.Exists() && !p.IsDead && !fled;
        }

        // ------------------------------------------------------------ update

        public void Update()
        {
            int now = Environment.TickCount;
            Ped player = GTA.Game.Player.Character;
            for (int i = scenes.Count - 1; i >= 0; i--)
            {
                Scene s = scenes[i];
                string outcome;
                try { outcome = Step_(s, player, now); }
                catch (Exception ex) { Log.Error("NPC accident scene failed", ex); outcome = "error"; }
                if (outcome != null)
                {
                    Close(s, outcome);
                    scenes.RemoveAt(i);
                }
            }
        }

        // Returns an outcome text when the scene is over, otherwise null.
        private string Step_(Scene s, Ped player, int now)
        {
            int t = unchecked(now - s.StepSince);
            int age = unchecked(now - s.Started);
            if (player.Position.DistanceTo(s.Position) > 300f)
                return s.Police != null
                    ? L.F("Investigated off-screen. Fault: {0}", "Разобрано вне поля зрения. Виновник: {0}", FaultText(s))
                    : L.S("Resolved off-screen", "Разобрались вне поля зрения");
            if (age > 420000) return L.S("Scene cleared after a long wait", "Место ДТП освобождено после долгого ожидания");

            bool a = Present(s.DA, s.FledA), b = Present(s.DB, s.FledB);
            UpdateAmbulance(s, now);

            switch (s.Step)
            {
                case Step.Braking:
                    if (t > 3000)
                    {
                        if (a && !Hurt(s.DA)) N.Call(N.TASK_LEAVE_VEHICLE, s.DA, s.A, 0);
                        if (b && !Hurt(s.DB)) N.Call(N.TASK_LEAVE_VEHICLE, s.DB, s.B, 0);
                        Next(s, Step.Exiting, now);
                    }
                    break;

                case Step.Exiting:
                    if (t > 4000)
                    {
                        if (a && !s.DA.IsInVehicle()) N.WalkTo(s.DA, s.A.GetOffsetPosition(new Vector3(1.5f, 2.6f, 0f)));
                        if (b && !s.DB.IsInVehicle()) N.WalkTo(s.DB, s.B.GetOffsetPosition(new Vector3(1.5f, 2.6f, 0f)));
                        Next(s, Step.Inspecting, now);
                    }
                    break;

                case Step.Inspecting:
                    if (t > 7000)
                    {
                        if (a && b && !s.DA.IsInVehicle() && !s.DB.IsInVehicle())
                        {
                            N.Call(N.TASK_GO_TO_ENTITY, s.DA, s.DB, -1, 1.6f, 1.0f, 0f, 0);
                            N.Face(s.DB, s.DA);
                        }
                        Next(s, Step.Exchanging, now);
                    }
                    break;

                case Step.Exchanging:
                    if (t > 4000 && t < 4600 && a && b) { N.Face(s.DA, s.DB); }
                    if (t > 12000)
                    {
                        bool serious = s.Severity != AccidentSeverity.Minor || Hurt(s.DA) || Hurt(s.DB) || s.FledA || s.FledB;
                        if (!serious)
                            return L.S("Drivers exchanged details and drove on", "Водители обменялись данными и уехали");
                        Ped caller = b && !Hurt(s.DB) ? s.DB : (a && !Hurt(s.DA) ? s.DA : null);
                        if (caller != null && !caller.IsInVehicle()) N.Scenario(caller, "WORLD_HUMAN_STAND_MOBILE");
                        Call911(s, false);
                        Next(s, Step.Waiting, now);
                    }
                    break;

                case Step.Waiting:
                    if (s.Police == null) return L.S("No police unit available; drivers left", "Свободного патруля не было; водители уехали");
                    if (!s.Police.Valid) return L.S("Police could not attend", "Полиция не смогла приехать");
                    if (main.Dispatch.Arrived(s.Police) || (N.Dist2D(s.Police.Vehicle.Position, s.Position) < 30f && s.Police.Vehicle.Speed < 1f))
                    {
                        N.Call(N.SET_VEHICLE_HAS_MUTED_SIRENS, s.Police.Vehicle, true);
                        N.Siren(s.Police.Vehicle, true);
                        N.Call(N.TASK_LEAVE_VEHICLE, s.Police.Driver, s.Police.Vehicle, 0);
                        s.InvPhase = 0;
                        s.InvPhaseSince = now;
                        Next(s, Step.Investigating, now);
                    }
                    else if (t > 240000) return L.S("Police did not arrive in time", "Полиция не успела приехать");
                    break;

                case Step.Investigating:
                    if (!s.Police.Valid) return L.S("Investigation interrupted", "Разбирательство прервано");
                    if (Investigate(s, now)) { Next(s, Step.Clearing, now); }
                    break;

                case Step.Clearing:
                    if (t > 2000) return L.F("Police report filed. Fault: {0}", "Полиция составила протокол. Виновник: {0}", FaultText(s));
                    break;
            }
            return null;
        }

        private static void Next(Scene s, Step st, int now)
        {
            s.Step = st;
            s.StepSince = now;
        }

        // Officer: car A, car B, driver A, driver B, write. Returns true when finished.
        private bool Investigate(Scene s, int now)
        {
            Ped cop = s.Police.Driver;
            int t = unchecked(now - s.InvPhaseSince);
            switch (s.InvPhase)
            {
                case 0: // walk to car A
                    if (t > 2500) { N.WalkTo(cop, s.A.GetOffsetPosition(new Vector3(2.2f, 0f, 0f))); Phase(s, now); }
                    break;
                case 1:
                    if (t > 9000) { N.Scenario(cop, "WORLD_HUMAN_CLIPBOARD"); Phase(s, now); }
                    break;
                case 2: // car B
                    if (t > 5000) { N.Call(N.CLEAR_PED_TASKS, cop); N.WalkTo(cop, s.B.GetOffsetPosition(new Vector3(2.2f, 0f, 0f))); Phase(s, now); }
                    break;
                case 3:
                    if (t > 9000) { N.Scenario(cop, "WORLD_HUMAN_CLIPBOARD"); Phase(s, now); }
                    break;
                case 4: // talk to the drivers
                    if (t > 5000)
                    {
                        N.Call(N.CLEAR_PED_TASKS, cop);
                        Ped who = Present(s.DA, s.FledA) ? s.DA : (Present(s.DB, s.FledB) ? s.DB : null);
                        if (who != null) { N.Call(N.TASK_GO_TO_ENTITY, cop, who, -1, 1.6f, 1.0f, 0f, 0); }
                        Phase(s, now);
                    }
                    break;
                case 5:
                    if (t > 10000)
                    {
                        Ped other = Present(s.DB, s.FledB) && Present(s.DA, s.FledA) ? s.DB : null;
                        if (other != null) N.Call(N.TASK_GO_TO_ENTITY, cop, other, -1, 1.6f, 1.0f, 0f, 0);
                        Phase(s, now);
                    }
                    break;
                case 6:
                    if (t > 9000) { N.Scenario(cop, "WORLD_HUMAN_CLIPBOARD"); Phase(s, now); }
                    break;
                case 7:
                    if (t > 6000) return true;
                    break;
            }
            return false;
        }

        private static void Phase(Scene s, int now)
        {
            s.InvPhase++;
            s.InvPhaseSince = now;
        }

        private void Call911(Scene s, bool byPlayer)
        {
            if (s.Called) return;
            s.Called = true;
            s.Police = main.Dispatch.SendPolice(s.Position, s.Severity == AccidentSeverity.Severe);
            if (s.Police != null && s.Police.Blip != null && s.Police.Blip.Exists() && !byPlayer) s.Police.Blip.Delete(); // no map clutter
            if (Hurt(s.DA) || Hurt(s.DB)) s.Ambulance = main.Dispatch.SendAmbulance(s.Position, true);
            if (s.Ambulance != null && s.Ambulance.Blip != null && s.Ambulance.Blip.Exists() && !byPlayer) s.Ambulance.Blip.Delete();
        }

        private void UpdateAmbulance(Scene s, int now)
        {
            Responder a = s.Ambulance;
            if (a == null || s.AmbDone) return;
            if (!a.Valid) { s.AmbDone = true; return; }
            if (s.AmbSince == 0)
            {
                if (!main.Dispatch.Arrived(a)) return;
                N.Call(N.SET_VEHICLE_HAS_MUTED_SIRENS, a.Vehicle, true);
                N.Call(N.TASK_LEAVE_VEHICLE, a.Driver, a.Vehicle, 0);
                if (a.Partner != null && a.Partner.Exists()) N.Call(N.TASK_LEAVE_VEHICLE, a.Partner, a.Vehicle, 0);
                s.AmbSince = now;
                return;
            }
            int t = unchecked(now - s.AmbSince);
            Ped patient = Hurt(s.DA) ? s.DA : (Hurt(s.DB) ? s.DB : null);
            if (t > 4000 && t < 4600 && patient != null)
            {
                N.WalkTo(a.Driver, patient.Position + new Vector3(0.8f, 0.8f, 0f));
                if (a.Partner != null && a.Partner.Exists()) N.WalkTo(a.Partner, patient.Position + new Vector3(-0.8f, 0.8f, 0f));
            }
            if (t > 14000 && t < 14600 && patient != null)
            {
                N.Scenario(a.Driver, "CODE_HUMAN_MEDIC_TEND_TO_DEAD");
                if (a.Partner != null && a.Partner.Exists()) N.Scenario(a.Partner, "CODE_HUMAN_MEDIC_KNEEL");
            }
            if (t > 30000)
            {
                if (patient != null && patient.Exists() && !patient.IsDead) patient.Health = Math.Max(patient.Health, 180);
                s.AmbDone = true;
                main.Dispatch.SendAway(a);
            }
        }

        private string FaultText(Scene s)
        {
            Fault f = PoliceRules.DetermineFault(s.Evidence);
            if (s.FledA) f = Fault.Player;   // fleeing driver is held responsible
            else if (s.FledB) f = Fault.Other;
            switch (f)
            {
                case Fault.Player: return L.F("driver of the {0}", "водитель {0}", N.SafeName(s.A));
                case Fault.Other: return L.F("driver of the {0}", "водитель {0}", N.SafeName(s.B));
                case Fault.Shared: return L.S("both drivers", "оба водителя");
            }
            return L.S("not determined", "не установлен");
        }

        private string Describe(Scene s)
        {
            string text = L.F("{0} and {1} collided ({2}).", "Столкнулись {0} и {1} ({2}).",
                N.SafeName(s.A), N.SafeName(s.B), PoliceRules.SeverityName(s.Severity));
            if (s.FledA || s.FledB) text += " " + L.S("One driver fled.", "Один водитель скрылся.");
            if (Hurt(s.DA) || Hurt(s.DB)) text += " " + L.S("Injuries reported.", "Есть пострадавшие.");
            if (s.Witness) text += " " + L.S("You gave a witness statement.", "Вы дали свидетельские показания.");
            return text;
        }

        private void Close(Scene s, string outcome)
        {
            if (s.SpeedZone >= 0) N.Call<bool>(N.REMOVE_ROAD_NODE_SPEED_ZONE, s.SpeedZone);
            s.SpeedZone = -1;
            if (s.Police != null) main.Dispatch.SendAway(s.Police);
            if (s.Ambulance != null && !s.AmbDone) main.Dispatch.SendAway(s.Ambulance);
            Resume(s.DA, s.A);
            Resume(s.DB, s.B);
            N.Release(s.DA);
            N.Release(s.DB);
            N.Release(s.A);
            N.Release(s.B);
            if (outcome != null)
            {
                main.Saves.Data.AddIncident("Accident", s.Location, Describe(s), outcome);
                Log.Info("NPC accident at " + s.Location + " closed: " + outcome);
            }
        }

        private static void Resume(Ped d, Vehicle v)
        {
            if (d == null || !d.Exists() || d.IsDead || v == null || !v.Exists() || v.IsDead) return;
            N.Call(N.CLEAR_PED_TASKS, d);
            if (d.IsInVehicle(v)) N.Call(N.TASK_VEHICLE_DRIVE_WANDER, d, v, 12f, N.StyleNormal);
            else N.Call(N.TASK_ENTER_VEHICLE, d, v, 20000, -1, 1.0f, 1, 0);
        }

        // ------------------------------------------------------------ player interaction

        public void OnFrame()
        {
            if (scenes.Count == 0 || main.Menu.IsOpen) return;
            Ped p = GTA.Game.Player.Character;
            if (p == null || !p.Exists() || p.IsDead || p.IsInVehicle()) return;
            foreach (Scene s in scenes)
            {
                if (p.Position.DistanceTo(s.Position) > 20f) continue;
                if (!s.Called && s.Step >= Step.Exiting)
                {
                    GTA.UI.Screen.ShowHelpTextThisFrame(L.S("Press ~INPUT_CONTEXT~ to call 911 for this accident.", "Нажмите ~INPUT_CONTEXT~, чтобы сообщить о ДТП в 911."));
                    if (NativeInput.JustPressed(GTA.Control.Context))
                    {
                        Call911(s, true);
                        if (s.Step < Step.Waiting) Next(s, Step.Waiting, Environment.TickCount);
                        main.Notify(s.Police != null
                            ? L.F("911: police dispatched to the accident. ETA about {0} s.", "911: полиция выехала на ДТП. Прибудет примерно через {0} с.", main.Dispatch.EtaSeconds(s.Police))
                            : L.S("No police unit available right now.", "Сейчас нет свободных патрулей."));
                    }
                    return;
                }
                if (s.Step == Step.Investigating && !s.Witness && s.Police != null && s.Police.Valid && p.Position.DistanceTo(s.Police.Driver.Position) < 6f)
                {
                    GTA.UI.Screen.ShowHelpTextThisFrame(L.S("Press ~INPUT_CONTEXT~ to give a witness statement.", "Нажмите ~INPUT_CONTEXT~, чтобы дать свидетельские показания."));
                    if (NativeInput.JustPressed(GTA.Control.Context))
                    {
                        s.Witness = true;
                        PoliceStopSystem.Say(L.S("Thank you, that helps the report.", "Спасибо, это поможет составить протокол."));
                        DriverProfile prof = main.CurrentProfile();
                        prof.Reputation = Math.Min(100, prof.Reputation + 2);
                    }
                    return;
                }
            }
        }
    }
}
