// Realistic Life Simulator - police traffic stops.
//
// Player stop: the officer who WITNESSED a violation follows with lights on. Pull over,
// the officer parks behind, walks to your window and talks to you (mouse menu):
// documents, questions, then a warning or a fine with licence points. Driving off turns
// the stop into "evading the police" and GTA's wanted system takes over.
//
// Assistance call: you call the police (Life Hub or your own key); a unit drives to you.
//
// NPC stops: police cars also pull over NPC drivers who speed past them (budgeted).

using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using RealisticLifeSimulator.Core;

namespace RealisticLifeSimulator.Game
{
    public sealed class PoliceStopSystem : IModule, IFrameModule
    {
        private enum StopMode { TrafficStop, Assistance }
        private enum StopState { Signalling, Responding, Pulling, Approaching, Talking, Leaving }

        private sealed class PlayerStop
        {
            public StopMode Mode;
            public StopState State;
            public int Since;
            public Responder Unit;
            public ViolationType Type;
            public int OverKmh;
            public string Details, Location;
            public Vehicle PlayerVehicle;
            public int StoppedSince = -1, FleeSince = -1;
            public bool WarnedToStop;
            public bool LicenceShown, RegistrationShown, InsuranceShown, Refused, Answered, Suspended, Decided;
            public LicenceStatus Licence;   // read from LicenceRules when the licence is handed over
            public int Attitude;
            public Vector3 WalkTarget;
            public int LastWalk;
        }

        private enum NpcState { Following, Parking, Approaching, Writing, Returning }

        private sealed class NpcStop
        {
            public Responder Cop;
            public Ped Driver;
            public Vehicle Vehicle;
            public NpcState State;
            public int Since;
            public bool Flees;
            public int StoppedSince = -1;
            public int WriteMs;
            public string Reason;
        }

        private readonly RlsMain main;
        private readonly Random rng = new Random();
        private PlayerStop stop;
        private readonly List<NpcStop> npcStops = new List<NpcStop>();
        private readonly List<int> stopTimes = new List<int>();
        private int nextNpcScan;
        private int nextDispatchUpdate;

        public PoliceStopSystem(RlsMain main) { this.main = main; }

        public string Name { get { return "Police"; } }
        public int IntervalMs { get { return 250; } }
        public bool StopActive { get { return stop != null; } }
        public int NpcStopsActive { get { return npcStops.Count; } }

        public void Start() { }

        public void Shutdown()
        {
            if (stop != null && stop.Unit != null) main.Dispatch.SendAway(stop.Unit);
            stop = null;
            foreach (NpcStop s in npcStops) EndNpcStop(s);
            npcStops.Clear();
        }

        // ------------------------------------------------------------ starting stops

        public bool TryStartStop(ViolationType type, int overKmh, string details, string location, Ped witness)
        {
            if (stop != null || !main.Config.Settings.PoliceStops || N.InMission()) return false;
            if (N.Wanted() > 0) return false;
            Ped player = GTA.Game.Player.Character;
            Vehicle pv = player.CurrentVehicle;
            if (pv == null || witness == null || !witness.Exists()) return false;
            Vehicle cv = witness.CurrentVehicle;
            if (cv == null || !cv.Exists() || cv.Driver != witness || (int)cv.ClassType != 18) return false;
            if (main.Dispatch.IsInUse(cv)) return false;
            foreach (NpcStop n in npcStops) if (n.Cop.Vehicle == cv) return false;
            // Mission vehicles are off limits, except this mod's own patrol cars (handed over).
            bool patrol = main.Patrols != null && main.Patrols.Owns(cv);
            if (!patrol && N.IsMissionEntity(cv)) return false;
            if (patrol) main.Patrols.HandOver(cv);

            PlayerStop s = new PlayerStop();
            s.Mode = StopMode.TrafficStop;
            s.Unit = main.Dispatch.Adopt(cv, witness);
            s.Type = type;
            s.OverKmh = overKmh;
            s.Details = details;
            s.Location = location;
            s.PlayerVehicle = pv;
            SetState(s, StopState.Signalling);
            N.Siren(cv, true);
            N.Call(N.TASK_VEHICLE_FOLLOW, witness, cv, pv, 30f, N.StyleUrgent, 8);
            stop = s;
            main.Notify(L.F("~r~Police~s~ are signalling you to pull over ({0}). Stop safely at the side of the road.",
                            "~r~Полиция~s~ требует остановиться ({0}). Безопасно остановитесь у обочины.", TrafficRules.Name(type)));
            return true;
        }

        // Player called the police for help (Life Hub or the CallPolice key).
        public bool CallForAssistance()
        {
            if (stop != null) { main.Notify(L.S("Police are already with you.", "Полиция уже рядом с вами.")); return false; }
            if (main.Accidents != null && main.Accidents.CallPoliceIfActive()) return true;
            Ped player = GTA.Game.Player.Character;
            Responder r = main.Dispatch.SendPolice(player.Position, false);
            if (r == null)
            {
                main.Notify(L.S("No police unit could be dispatched right now.", "Сейчас нет свободных патрулей."));
                return false;
            }
            PlayerStop s = new PlayerStop();
            s.Mode = StopMode.Assistance;
            s.Unit = r;
            SetState(s, StopState.Responding);
            stop = s;
            main.Notify(L.F("911: a police unit is on its way. ETA about {0} s.", "911: патруль выехал. Прибудет примерно через {0} с.", main.Dispatch.EtaSeconds(r)));
            return true;
        }

        private static void SetState(PlayerStop s, StopState st)
        {
            s.State = st;
            s.Since = Environment.TickCount;
        }

        // ------------------------------------------------------------ update

        public void Update()
        {
            int now = Environment.TickCount;
            if (unchecked(now - nextDispatchUpdate) >= 0)
            {
                nextDispatchUpdate = now + 1000;
                main.Dispatch.Update();
            }

            if (stop != null)
            {
                try { UpdatePlayerStop(now); }
                catch (Exception ex)
                {
                    Log.Error("Traffic stop failed; ending it", ex);
                    EndStop(false);
                }
            }
            UpdateNpcStops(now);
        }

        private void UpdatePlayerStop(int now)
        {
            PlayerStop s = stop;
            Ped player = GTA.Game.Player.Character;
            if (!s.Unit.Valid || player.IsDead || (N.Wanted() > 0 && s.State != StopState.Leaving))
            {
                EndStop(false);
                return;
            }
            Ped cop = s.Unit.Driver;
            Vehicle cv = s.Unit.Vehicle;
            int elapsed = unchecked(now - s.Since);
            float copDist = player.Position.DistanceTo(cop.Position);
            Vehicle pv = player.CurrentVehicle;
            float speed = pv != null ? pv.Speed : 0f;

            switch (s.State)
            {
                case StopState.Signalling:
                    if (copDist > 180f) { if (Fleeing(s, now, 4000)) return; } else s.FleeSince = -1;
                    if (speed < 1f && copDist < 45f)
                    {
                        if (s.StoppedSince < 0) s.StoppedSince = now;
                        if (unchecked(now - s.StoppedSince) > 1500)
                        {
                            Vehicle target = pv ?? s.PlayerVehicle;
                            Vector3 behind = target != null && target.Exists() ? target.GetOffsetPosition(new Vector3(0f, -9f, 0f)) : player.Position;
                            N.Call(N.TASK_VEHICLE_DRIVE_TO_COORD, cop, cv, behind.X, behind.Y, behind.Z, 6f, 0, cv.Model.Hash, N.StyleNormal, 2f, 0f);
                            N.Call(N.SET_VEHICLE_HAS_MUTED_SIRENS, cv, true);
                            SetState(s, StopState.Pulling);
                        }
                    }
                    else s.StoppedSince = -1;
                    if (elapsed > 20000 && !s.WarnedToStop)
                    {
                        s.WarnedToStop = true;
                        Say(L.S("Pull over now!", "Немедленно остановитесь!"));
                    }
                    if (elapsed > 50000) { Fled(s); return; }
                    break;

                case StopState.Responding:
                    if (main.Dispatch.Arrived(s.Unit) || (copDist < 35f && cv.Speed < 1f))
                    {
                        N.Call(N.SET_VEHICLE_HAS_MUTED_SIRENS, cv, true);
                        N.Call(N.TASK_LEAVE_VEHICLE, cop, cv, 0);
                        SetState(s, StopState.Approaching);
                    }
                    else if (elapsed > 240000) { EndStop(false); return; }
                    break;

                case StopState.Pulling:
                    if (DrivingAway(s, speed, now)) return;
                    if ((cv.Speed < 0.5f && elapsed > 2500) || elapsed > 12000)
                    {
                        N.Call(N.TASK_LEAVE_VEHICLE, cop, cv, 0);
                        SetState(s, StopState.Approaching);
                    }
                    break;

                case StopState.Approaching:
                    if (s.Mode == StopMode.TrafficStop && DrivingAway(s, speed, now)) return;
                    if (cop.IsInVehicle()) { if (elapsed > 8000) N.Call(N.TASK_LEAVE_VEHICLE, cop, cv, 0); break; }
                    Vector3 window = pv != null ? pv.GetOffsetPosition(new Vector3(-1.4f, 0.4f, 0f)) : player.Position;
                    if (unchecked(now - s.LastWalk) > 3000 || N.Dist2D(window, s.WalkTarget) > 2f)
                    {
                        s.LastWalk = now;
                        s.WalkTarget = window;
                        N.WalkTo(cop, window);
                    }
                    if (N.Dist2D(cop.Position, window) < 1.6f || copDist < 2.2f || elapsed > 30000)
                    {
                        N.Face(cop, player);
                        SetState(s, StopState.Talking);
                        OpenStopMenu();
                    }
                    break;

                case StopState.Talking:
                    if (s.Mode == StopMode.TrafficStop && !s.Decided && copDist > 35f) { Fled(s); return; }
                    if (s.Mode == StopMode.Assistance && copDist > 60f) { EndStop(false); return; }
                    break;

                case StopState.Leaving:
                    if (elapsed > 1500) EndStop(true);
                    break;
            }
        }

        private bool DrivingAway(PlayerStop s, float speed, int now)
        {
            if (speed > 8f)
            {
                if (s.FleeSince < 0) s.FleeSince = now;
                if (unchecked(now - s.FleeSince) > 3000) { Fled(s); return true; }
            }
            else s.FleeSince = -1;
            return false;
        }

        private bool Fleeing(PlayerStop s, int now, int ms)
        {
            if (s.FleeSince < 0) s.FleeSince = now;
            if (unchecked(now - s.FleeSince) > ms) { Fled(s); return true; }
            return false;
        }

        // Evading a stop is a crime, not a traffic matter: GTA's wanted system takes over.
        private void Fled(PlayerStop s)
        {
            RlsSettings set = main.Config.Settings;
            double fine = Math.Round(TrafficRules.BaseFine(ViolationType.EvadingPolice, 0) * set.FineMultiplier);
            main.Traffic.AddViolation(ViolationType.EvadingPolice, TrafficRules.Name(s.Type), fine, true,
                PoliceRules.PointsFor(ViolationType.EvadingPolice, 0), false, s.Location);
            if (main.Menu.IsOpen) main.Menu.Close();
            N.RaiseWanted(1);
            main.Notify(L.S("~r~You evaded a traffic stop.~s~ The police are now after you.",
                            "~r~Вы уклонились от остановки.~s~ Теперь вас преследует полиция."));
            Log.Info("Player evaded a traffic stop");
            Responder u = s.Unit;
            stop = null;
            if (u != null)
            {
                if (u.Blip != null && u.Blip.Exists()) u.Blip.Delete();
                main.Dispatch.SendAway(u);
            }
        }

        private void EndStop(bool completed)
        {
            if (stop == null) return;
            if (main.Menu.IsOpen) main.Menu.Close();
            Responder u = stop.Unit;
            stop = null;
            if (u != null) main.Dispatch.SendAway(u);
            if (completed) main.Saves.Save();
        }

        // ------------------------------------------------------------ frame: prompts

        public void OnFrame()
        {
            if (stop == null) return;
            Ped player = GTA.Game.Player.Character;
            if (stop.State == StopState.Signalling)
            {
                GTA.UI.Screen.ShowHelpTextThisFrame(L.S("Police: pull over and stop.", "Полиция: прижмитесь к обочине и остановитесь."));
                return;
            }
            if (stop.State == StopState.Talking && !main.Menu.IsOpen && stop.Unit.Valid
                && player.Position.DistanceTo(stop.Unit.Driver.Position) < 6f)
            {
                GTA.UI.Screen.ShowHelpTextThisFrame(L.S("Press ~INPUT_CONTEXT~ to talk to the officer.", "Нажмите ~INPUT_CONTEXT~, чтобы поговорить с полицейским."));
                if (NativeInput.JustPressed(GTA.Control.Context)) OpenStopMenu();
            }
        }

        public static void Say(string line)
        {
            GTA.UI.Screen.ShowSubtitle(line, 6000);
        }

        // ------------------------------------------------------------ stop menus

        private void OpenStopMenu()
        {
            if (stop == null) return;
            Menu m = stop.Mode == StopMode.Assistance ? BuildAssistanceMenu() : BuildStopMenu();
            if (main.Menu.IsOpen) main.Menu.Replace(m); else main.Menu.Open(m);
            if (stop.Mode == StopMode.TrafficStop && !stop.LicenceShown && !stop.Refused)
                Say(L.S("Good day. Licence, registration and proof of insurance, please.",
                        "Здравствуйте. Ваши права, регистрацию и страховку, пожалуйста."));
            else if (stop.Mode == StopMode.Assistance)
                Say(L.S("You called the police. How can I help?", "Вы вызывали полицию. Чем могу помочь?"));
        }

        private void Refresh()
        {
            if (stop != null && main.Menu.IsOpen) main.Menu.Replace(stop.Decided ? main.Menu.Current : BuildStopMenu());
        }

        private Menu BuildStopMenu()
        {
            PlayerStop s = stop;
            DriverProfile prof = main.CurrentProfile();
            double play = main.Saves.Data.Stats.TotalPlayTimeSeconds;
            Menu m = new Menu(L.S("Traffic stop", "Остановка полицией"), s.Unit.Name);
            m.Add(new MenuItem(L.S("Reason", "Причина"), null).WithValue(() => TrafficRules.Name(s.Type))
                .WithDescription(s.Details + (string.IsNullOrEmpty(s.Location) ? "" : "  -  " + s.Location)));

            m.Add(new MenuItem(L.S("Ask why I was stopped", "Спросить причину остановки"), () =>
                Say(L.F("I stopped you for: {0}. {1}", "Я остановил вас за нарушение: {0}. {1}", TrafficRules.Name(s.Type), s.Details))));

            m.Add(new MenuItem(L.S("Hand over driving licence", "Передать водительское удостоверение"), () =>
            {
                s.LicenceShown = true;
                double now = main.Saves.Data.Stats.TotalPlayTimeSeconds;
                s.Licence = LicenceRules.Status(prof, now);      // the one source of truth
                s.Suspended = s.Licence != LicenceStatus.Valid;
                Say(LicenceRules.OfficerLine(prof, now));
                Refresh();
            }).WithEnabled(() => !s.LicenceShown && !s.Refused)
              .WithValue(() => s.LicenceShown ? LicenceRules.StatusName(s.Licence) : ""));

            m.Add(new MenuItem(L.S("Hand over registration", "Передать регистрацию"), () =>
            {
                s.RegistrationShown = true;
                Vehicle v = GTA.Game.Player.Character.CurrentVehicle ?? s.PlayerVehicle;
                if (v != null && v.Exists() && N.Call<bool>(N.IS_VEHICLE_STOLEN, v))
                {
                    Say(L.S("This vehicle is reported stolen! Step out of the car!", "Эта машина числится в угоне! Выйдите из машины!"));
                    Stolen();
                    return;
                }
                Say(L.F("Registration for plate {0} is in order.", "Регистрация номера {0} в порядке.", v != null && v.Exists() ? N.Plate(v) : "-"));
                Refresh();
            }).WithEnabled(() => !s.RegistrationShown && !s.Refused));

            m.Add(new MenuItem(L.S("Hand over insurance", "Передать страховку"), () =>
            {
                s.InsuranceShown = true;
                Say(L.F("{0}, policy {1}. Valid.", "{0}, полис {1}. Действителен.", prof.Insurer, prof.PolicyNumber));
                Refresh();
            }).WithEnabled(() => !s.InsuranceShown && !s.Refused));

            m.Add(new MenuItem(L.S("Refuse to show documents", "Отказаться предъявить документы"), () =>
            {
                s.Refused = true;
                s.Attitude -= 1;
                Say(L.S("Then I have to cite you for that as well.", "Тогда мне придётся оштрафовать вас и за это."));
                Refresh();
            }).WithEnabled(() => !s.LicenceShown && !s.Refused)
              .WithDescription(L.S("Adds a fine for failing to present documents.", "Добавит штраф за отказ предъявить документы.")));

            bool canAnswer = (s.LicenceShown || s.Refused) && !s.Answered;
            if (canAnswer)
            {
                Say(L.S("Do you know why I stopped you?", "Вы знаете, почему я вас остановил?"));
                AddAnswer(m, L.S("\"Yes. I'm sorry, officer.\"", "«Да. Простите, офицер.»"), 2,
                    L.S("I appreciate your honesty.", "Ценю вашу честность."));
                AddAnswer(m, L.S("\"No, officer.\"", "«Нет, офицер.»"), 0,
                    L.S("I see.", "Понятно."));
                AddAnswer(m, L.S("\"I was in a hurry.\"", "«Я торопился.»"), 0,
                    L.S("That's no excuse on public roads.", "На дороге это не оправдание."));
                AddAnswer(m, L.S("\"You're wasting my time.\"", "«Вы тратите моё время.»"), -2,
                    L.S("Watch your tone.", "Следите за тоном."));
            }

            m.Add(new MenuItem(L.S("Ask for directions to the nearest gas station", "Спросить дорогу до ближайшей АЗС"), DirectionsToFuel));

            m.Add(new MenuItem(L.S("Wait for the officer's decision", "Ждать решения офицера"), Decide)
                .WithEnabled(() => (s.LicenceShown || s.Refused) && s.Answered)
                .WithDescription(L.S("Show your licence and answer the question first.", "Сначала покажите права и ответьте на вопрос.")));
            return m;
        }

        private void AddAnswer(Menu m, string label, int attitude, string reply)
        {
            m.Add(new MenuItem(label, () =>
            {
                stop.Answered = true;
                stop.Attitude += attitude;
                Say(reply);
                Refresh();
            }).WithDescription(L.S("Your answer affects whether you get a warning.", "Ответ влияет на то, отделаетесь ли вы предупреждением.")));
        }

        private void DirectionsToFuel()
        {
            Ped p = GTA.Game.Player.Character;
            GasStation best = null;
            float bestD = float.MaxValue;
            foreach (GasStation g in main.Fuel.Stations)
            {
                float d = N.DistSq(p.Position, g.Position);
                if (d < bestD) { bestD = d; best = g; }
            }
            if (best == null) return;
            N.Call(N.SET_NEW_WAYPOINT, best.Position.X, best.Position.Y);
            Say(L.F("Nearest gas station: {0}. I've marked it on your map.", "Ближайшая АЗС: {0}. Отметил её на вашей карте.", best.Name));
        }

        private void Stolen()
        {
            if (main.Menu.IsOpen) main.Menu.Close();
            Responder u = stop.Unit;
            stop = null;
            N.Release(u.Driver);
            if (u.Blip != null && u.Blip.Exists()) u.Blip.Delete();
            N.RaiseWanted(2);
            main.Dispatch.SendAway(u);
        }

        private void Decide()
        {
            PlayerStop s = stop;
            RlsSettings set = main.Config.Settings;
            SaveData data = main.Saves.Data;
            DriverProfile prof = main.CurrentProfile();
            int now = Environment.TickCount;
            double play = data.Stats.TotalPlayTimeSeconds;
            stopTimes.RemoveAll(t => unchecked(now - t) > 3600000);

            // The officer checks the licence in the database even if it was not handed over.
            LicenceStatus before = LicenceRules.Status(prof, play);
            s.Licence = before;
            s.Suspended = before != LicenceStatus.Valid;
            bool licenceStop = s.Type == ViolationType.SuspendedLicence || s.Type == ViolationType.DrivingWhileRevoked;

            double chance = PoliceRules.WarningChance(prof.Reputation, s.Attitude, s.Type, s.OverKmh,
                data.OutstandingFines() > 0, stopTimes.Count);
            bool warning = !s.Refused && !s.Suspended && !licenceStop && rng.NextDouble() < chance;
            stopTimes.Add(now);
            prof.Stops++;
            data.Driving.TrafficStops++;
            s.Decided = true;

            List<ViolationRecord> issued = new List<ViolationRecord>();
            if (licenceStop && before == LicenceStatus.Valid)
            {
                // The licence was restored since the check started: nothing to cite.
                Say(L.S("Your licence checks out. Sorry for the delay, drive safely.", "С правами всё в порядке. Извините за задержку, счастливого пути."));
            }
            else if (warning)
            {
                main.Traffic.AddViolation(s.Type, s.Details, 0, true, 0, true, s.Location);
                Say(L.S("I'm letting you off with a warning this time. Drive safely.", "На этот раз отпускаю с предупреждением. Будьте внимательны."));
            }
            else
            {
                double fine = Math.Round(TrafficRules.BaseFine(s.Type, s.OverKmh) * set.FineMultiplier);
                issued.Add(main.Traffic.AddViolation(s.Type, s.Details, fine, true, PoliceRules.PointsFor(s.Type, s.OverKmh), false, s.Location));
                Say(L.F("I'm issuing you a citation: {0}.", "Выписываю вам штраф: {0}.", L.Money(fine)));
            }
            if (s.Refused)
                issued.Add(main.Traffic.AddViolation(ViolationType.NoDocuments, "", Math.Round(TrafficRules.BaseFine(ViolationType.NoDocuments, 0) * set.FineMultiplier), true, 0, false, s.Location));
            if (s.Suspended && !licenceStop)
            {
                ViolationType lt = before == LicenceStatus.Revoked ? ViolationType.DrivingWhileRevoked : ViolationType.SuspendedLicence;
                issued.Add(main.Traffic.AddViolation(lt, "", Math.Round(TrafficRules.BaseFine(lt, 0) * set.FineMultiplier), true, 0, false, s.Location));
            }
            if (s.Suspended)
            {
                LicenceRules.CaughtDriving(prof, play);
                main.AnnounceLicenceChange(before, prof);
                Say(before == LicenceStatus.Revoked
                    ? L.S("You are not allowed to drive. The revocation has been extended.", "Вам запрещено управлять машиной. Срок лишения продлён.")
                    : L.S("You must not drive while suspended. This goes on your record.", "С приостановленными правами ездить нельзя. Это занесено в ваше дело."));
            }

            Log.Info("Traffic stop decided: " + (warning ? "warning" : "citation") + " (chance " + chance.ToString("0.00") + ")");
            main.Menu.Replace(BuildOutcomeMenu(warning, issued));
        }

        private Menu BuildOutcomeMenu(bool warning, List<ViolationRecord> issued)
        {
            double total = 0;
            int points = 0;
            foreach (ViolationRecord r in issued) { total += r.Fine; points += r.Points; }
            double older = main.Saves.Data.OutstandingFines() - total;

            Menu m = new Menu(L.S("Officer's decision", "Решение офицера"), stop.Unit.Name);
            m.Add(new MenuItem(L.S("Result", "Итог"), null).WithValue(() => issued.Count > 0 ? L.S("Citation", "Штраф")
                : warning ? L.S("Warning", "Предупреждение") : L.S("No action", "Без последствий")));
            if (stop.LicenceShown || stop.Decided)
                m.Add(new MenuItem(L.S("Licence status", "Статус прав"), null).WithValue(() =>
                    LicenceRules.StatusName(LicenceRules.Status(main.CurrentProfile(), main.Saves.Data.Stats.TotalPlayTimeSeconds))));
            foreach (ViolationRecord r in issued)
            {
                ViolationRecord rec = r;
                m.Add(new MenuItem(TrafficRules.NameFromString(rec.Type), null)
                    .WithValue(() => L.Money(rec.Fine) + (rec.Points > 0 ? L.F(", {0} pt", ", {0} б.", rec.Points) : "") + (rec.Paid ? L.S(" (paid)", " (оплачен)") : "")));
            }
            if (points > 0)
                m.Add(new MenuItem(L.S("Licence points now", "Баллов на правах"), null).WithValue(() =>
                    main.CurrentProfile().ActivePoints(main.Saves.Data.Stats.TotalPlayTimeSeconds) + " / " + PoliceRules.SuspensionPoints));

            if (total > 0)
            {
                m.Add(new MenuItem(L.F("Pay now ({0})", "Оплатить сейчас ({0})", L.Money(total)), () =>
                {
                    if (PayRecords(issued)) { Say(L.S("Thank you. Have a safe day.", "Спасибо. Счастливого пути.")); Finish(); }
                }).WithEnabled(() => !AllPaid(issued) && GTA.Game.Player.Money >= total));
            }
            if (older > 0.5)
            {
                m.Add(new MenuItem(L.F("Also pay older unpaid fines ({0})", "Оплатить и старые штрафы ({0})", L.Money(older)), () =>
                {
                    List<ViolationRecord> old = new List<ViolationRecord>();
                    foreach (ViolationRecord r in main.Saves.Data.Violations) if (!r.Paid && r.Fine > 0) old.Add(r);
                    PayRecords(old);
                    main.Menu.Replace(BuildOutcomeMenu(warning, issued));
                }).WithEnabled(() => GTA.Game.Player.Money >= main.Saves.Data.OutstandingFines() && main.Saves.Data.OutstandingFines() > 0)
                  .WithDescription(L.S("The officer reminded you of fines you haven't paid yet.", "Офицер напомнил о неоплаченных штрафах.")));
            }
            m.Add(new MenuItem(total > 0 ? L.S("Pay later and drive on", "Оплатить позже и ехать") : L.S("Thank the officer and drive on", "Поблагодарить и уехать"),
                () => { Say(L.S("Drive safely.", "Счастливого пути.")); Finish(); })
                .WithDescription(L.S("Unpaid fines can be paid later in the Life Hub.", "Неоплаченные штрафы можно оплатить позже в центре управления.")));
            return m;
        }

        private static bool AllPaid(List<ViolationRecord> list)
        {
            foreach (ViolationRecord r in list) if (!r.Paid && r.Fine > 0) return false;
            return true;
        }

        private bool PayRecords(List<ViolationRecord> list)
        {
            double total = 0;
            foreach (ViolationRecord r in list) if (!r.Paid && r.Fine > 0) total += Math.Round(r.Fine);
            if (total <= 0) return true;
            if (GTA.Game.Player.Money < total)
            {
                main.Notify(L.S("Not enough cash.", "Недостаточно наличных."));
                return false;
            }
            GTA.Game.Player.Money -= (int)total;
            foreach (ViolationRecord r in list)
            {
                if (r.Paid || r.Fine <= 0) continue;
                r.Paid = true;
                main.Saves.Data.AddTransaction("Fine", TrafficRules.NameFromString(r.Type) + " (" + r.Location + ")", -Math.Round(r.Fine));
            }
            main.Saves.Data.Driving.FinesPaid += total;
            return true;
        }

        private void Finish()
        {
            if (stop == null) return;
            if (main.Menu.IsOpen) main.Menu.Close();
            SetState(stop, StopState.Leaving);
        }

        private Menu BuildAssistanceMenu()
        {
            Menu m = new Menu(L.S("Police", "Полиция"), stop.Unit.Name);
            m.Add(new MenuItem(L.S("Ask for directions to the nearest gas station", "Спросить дорогу до ближайшей АЗС"), DirectionsToFuel));
            m.Add(new MenuItem(L.S("Ask about my driving record", "Спросить о моих нарушениях"), () =>
            {
                DriverProfile p = main.CurrentProfile();
                Say(L.F("You have {0} licence point(s) and {1} in unpaid fines.", "У вас {0} балл(ов) на правах и неоплаченных штрафов на {1}.",
                    p.ActivePoints(main.Saves.Data.Stats.TotalPlayTimeSeconds), L.Money(main.Saves.Data.OutstandingFines())));
            }));
            m.Add(new MenuItem(L.S("Pay unpaid fines to the officer", "Оплатить штрафы офицеру"), () =>
            {
                List<ViolationRecord> old = new List<ViolationRecord>();
                foreach (ViolationRecord r in main.Saves.Data.Violations) if (!r.Paid && r.Fine > 0) old.Add(r);
                if (PayRecords(old)) Say(L.S("All settled. Thank you.", "Всё оплачено. Спасибо."));
            }).WithEnabled(() => main.Saves.Data.OutstandingFines() > 0));
            m.Add(new MenuItem(L.S("That's all, thank you", "Это всё, спасибо"), () =>
            {
                Say(L.S("Stay safe.", "Берегите себя."));
                Finish();
            }));
            return m;
        }

        // ------------------------------------------------------------ NPC stops

        private int MaxNpcStops
        {
            get { return main.Performance.Profile.Kind == PerformanceProfileKind.High ? 2 : 1; }
        }

        private void UpdateNpcStops(int now)
        {
            Ped player = GTA.Game.Player.Character;
            for (int i = npcStops.Count - 1; i >= 0; i--)
            {
                NpcStop s = npcStops[i];
                bool keep;
                try { keep = StepNpcStop(s, player, now); }
                catch (Exception ex) { Log.Error("NPC stop failed", ex); keep = false; }
                if (!keep)
                {
                    EndNpcStop(s);
                    npcStops.RemoveAt(i);
                }
            }

            if (!main.Config.Settings.NpcPoliceStops || stop != null || npcStops.Count >= MaxNpcStops || N.InMission()) return;
            if (unchecked(now - nextNpcScan) < 0) return;
            nextNpcScan = now + 10000;
            TryStartNpcStop(player, now);
        }

        private void TryStartNpcStop(Ped player, int now)
        {
            Vehicle[] list = World.GetNearbyVehicles(player.Position, main.Performance.Profile.NpcFullSimRadius);
            int limitChecks = 3;
            foreach (Vehicle cv in list)
            {
                if (cv == null || !cv.Exists() || (int)cv.ClassType != 18 || cv.Speed < 3f) continue;
                Ped cop = cv.Driver;
                bool patrol = main.Patrols != null && main.Patrols.Owns(cv);
                if (cop == null || !cop.Exists() || cop.IsDead || !N.IsCop(cop) || main.Dispatch.IsInUse(cv) || (!patrol && N.IsMissionEntity(cv))) continue;
                Vector3 fwd = cv.ForwardVector;
                foreach (Vehicle v in list)
                {
                    if (v == null || !v.Exists() || v == cv || v.IsDead || (int)v.ClassType == 18) continue;
                    Ped d = v.Driver;
                    if (d == null || !d.Exists() || d.IsDead || d.IsPlayer || N.IsCop(d) || N.IsMissionEntity(v) || N.IsMissionEntity(d)) continue;
                    Vector3 rel = v.Position - cv.Position;
                    float dist = rel.Length();
                    if (dist > 70f || dist < 5f) continue;
                    if ((rel.X * fwd.X + rel.Y * fwd.Y) / dist < 0.3f) continue; // must be ahead of the police car
                    string reason;
                    if (main.NpcTraffic != null && main.NpcTraffic.RecentViolation(v, out reason))
                    {
                        StartNpcStop(cv, cop, v, d, now, reason);
                        return;
                    }
                    float kmh = v.Speed * 3.6f;
                    if (kmh < 55f) continue;
                    if (limitChecks-- <= 0) return;
                    int limit = main.Traffic.LimitAt(v.Position);
                    if (limit <= 0 || kmh < limit + 15) continue;
                    StartNpcStop(cv, cop, v, d, now, L.F("{0}: {1:0} km/h in a {2} zone", "{0}: {1:0} км/ч при ограничении {2}", TrafficRules.Name(ViolationType.Speeding), kmh, limit));
                    return;
                }
            }
        }

        private void StartNpcStop(Vehicle cv, Ped cop, Vehicle v, Ped d, int now, string reason)
        {
            NpcStop s = new NpcStop();
            s.Reason = reason;
            if (main.Patrols != null) main.Patrols.HandOver(cv);
            s.Cop = main.Dispatch.Adopt(cv, cop);
            if (s.Cop.Blip != null && s.Cop.Blip.Exists()) s.Cop.Blip.Delete(); // no map clutter for NPC stops
            s.Driver = d;
            s.Vehicle = v;
            s.Flees = rng.NextDouble() < 0.08;
            s.WriteMs = 15000 + rng.Next(12000);
            s.State = NpcState.Following;
            s.Since = now;
            d.IsPersistent = true;
            v.IsPersistent = true;
            N.Block(d);
            N.Siren(cv, true);
            N.Call(N.TASK_VEHICLE_FOLLOW, cop, cv, v, 25f, N.StyleUrgent, 10);
            if (s.Flees)
                N.Call(N.TASK_VEHICLE_DRIVE_WANDER, d, v, 35f, N.StyleUrgent);
            else
            {
                Vector3 side = v.GetOffsetPosition(new Vector3(3.5f, 35f, 0f));
                N.Call(N.TASK_VEHICLE_DRIVE_TO_COORD, d, v, side.X, side.Y, side.Z, 8f, 0, v.Model.Hash, N.StyleNormal, 3f, 0f);
            }
            npcStops.Add(s);
            Log.Debug("Police stopping a speeding NPC" + (s.Flees ? " (it flees)" : ""));
        }

        private bool StepNpcStop(NpcStop s, Ped player, int now)
        {
            if (!s.Cop.Valid || s.Driver == null || !s.Driver.Exists() || s.Driver.IsDead || s.Vehicle == null || !s.Vehicle.Exists()) return false;
            if (player.Position.DistanceTo(s.Vehicle.Position) > 350f || player.CurrentVehicle == s.Vehicle) return false;
            Ped cop = s.Cop.Driver;
            Vehicle cv = s.Cop.Vehicle;
            int elapsed = unchecked(now - s.Since);

            switch (s.State)
            {
                case NpcState.Following:
                    if (s.Flees)
                    {
                        if (elapsed < 30000) return true;           // the chase is given up after 30 s
                        main.Saves.Data.AddIncident("Stop", main.Traffic.DescribeLocation(s.Vehicle.Position),
                            L.F("A {0} fled from a police stop. {1}", "{0} скрылся от полиции. {1}", N.SafeName(s.Vehicle), s.Reason),
                            L.S("Driver escaped", "Водитель скрылся"));
                        return false;
                    }
                    if (s.Vehicle.Speed < 0.5f && cv.Position.DistanceTo(s.Vehicle.Position) < 45f)
                    {
                        if (s.StoppedSince < 0) s.StoppedSince = now;
                        if (unchecked(now - s.StoppedSince) > 2000)
                        {
                            N.Call(N.TASK_VEHICLE_TEMP_ACTION, s.Driver, s.Vehicle, 6, 120000);
                            Vector3 behind = s.Vehicle.GetOffsetPosition(new Vector3(0f, -9f, 0f));
                            N.Call(N.TASK_VEHICLE_DRIVE_TO_COORD, cop, cv, behind.X, behind.Y, behind.Z, 6f, 0, cv.Model.Hash, N.StyleNormal, 2f, 0f);
                            N.Call(N.SET_VEHICLE_HAS_MUTED_SIRENS, cv, true);
                            Next(s, NpcState.Parking, now);
                        }
                    }
                    else s.StoppedSince = -1;
                    return elapsed < 60000;

                case NpcState.Parking:
                    if ((cv.Speed < 0.5f && elapsed > 2500) || elapsed > 12000)
                    {
                        N.Call(N.TASK_LEAVE_VEHICLE, cop, cv, 0);
                        Next(s, NpcState.Approaching, now);
                    }
                    return true;

                case NpcState.Approaching:
                    if (cop.IsInVehicle()) return elapsed < 10000;
                    Vector3 window = s.Vehicle.GetOffsetPosition(new Vector3(-1.4f, 0.4f, 0f));
                    if (elapsed < 400 || (elapsed / 3000) != ((elapsed - 250) / 3000)) N.WalkTo(cop, window);
                    if (N.Dist2D(cop.Position, window) < 1.6f || elapsed > 25000)
                    {
                        N.Scenario(cop, "WORLD_HUMAN_CLIPBOARD");
                        Next(s, NpcState.Writing, now);
                    }
                    return true;

                case NpcState.Writing:
                    if (elapsed > s.WriteMs)
                    {
                        N.Call(N.CLEAR_PED_TASKS, cop);
                        N.Call(N.TASK_ENTER_VEHICLE, cop, cv, 20000, -1, 1.0f, 1, 0);
                        N.Call(N.TASK_VEHICLE_DRIVE_WANDER, s.Driver, s.Vehicle, 14f, N.StyleNormal);
                        main.Saves.Data.Driving.NpcStopsSeen++;
                        main.Saves.Data.AddIncident("Stop", main.Traffic.DescribeLocation(s.Vehicle.Position),
                            L.F("Police stopped a {0}. {1}", "Полиция остановила {0}. {1}", N.SafeName(s.Vehicle), s.Reason),
                            L.S("Ticket issued", "Выписан штраф"));
                        Next(s, NpcState.Returning, now);
                    }
                    return true;

                case NpcState.Returning:
                    return !cop.IsInVehicle(cv) && elapsed < 25000;
            }
            return false;
        }

        private static void Next(NpcStop s, NpcState st, int now)
        {
            s.State = st;
            s.Since = now;
        }

        private void EndNpcStop(NpcStop s)
        {
            if (s.Driver != null && s.Driver.Exists() && s.Vehicle != null && s.Vehicle.Exists() && !s.Driver.IsDead && s.Driver.IsInVehicle(s.Vehicle))
                N.Call(N.TASK_VEHICLE_DRIVE_WANDER, s.Driver, s.Vehicle, 14f, N.StyleNormal);
            N.Release(s.Driver);
            N.Release(s.Vehicle);
            main.Dispatch.SendAway(s.Cop);
        }
    }
}
