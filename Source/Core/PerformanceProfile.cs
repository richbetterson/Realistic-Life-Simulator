// Realistic Life Simulator - performance budgets shared by every system.
// Later systems (NPC simulation, traffic, fuel) read their limits from here so that a
// single setting controls the whole mod's CPU cost.

namespace RealisticLifeSimulator.Core
{
    public sealed class PerformanceProfile
    {
        public PerformanceProfileKind Kind;
        public float NpcFullSimRadius;     // metres around the player with detailed NPC logic
        public int MaxNpcChecksPerUpdate;  // NPCs examined per update step (spread over time)
        public int NpcUpdateIntervalMs;    // how often nearby NPC logic runs
        public int MaxTrackedNpcs;         // upper bound on NPC records kept in memory
        public bool DistantNpcSimulation;  // simplified off-screen simulation
        public int DistantSimIntervalMs;

        public string Summary
        {
            get
            {
                return string.Format("NPC radius {0:0} m, {1} checks / {2} ms, {3} tracked, distant sim {4}",
                    NpcFullSimRadius, MaxNpcChecksPerUpdate, NpcUpdateIntervalMs, MaxTrackedNpcs,
                    DistantNpcSimulation ? "every " + (DistantSimIntervalMs / 1000) + " s" : "off");
            }
        }

        public static PerformanceProfile For(PerformanceProfileKind kind)
        {
            PerformanceProfile p = new PerformanceProfile();
            p.Kind = kind;
            switch (kind)
            {
                case PerformanceProfileKind.High:
                    p.NpcFullSimRadius = 250f; p.MaxNpcChecksPerUpdate = 16; p.NpcUpdateIntervalMs = 300;
                    p.MaxTrackedNpcs = 96; p.DistantNpcSimulation = true; p.DistantSimIntervalMs = 1500;
                    break;
                case PerformanceProfileKind.Balanced:
                    p.NpcFullSimRadius = 180f; p.MaxNpcChecksPerUpdate = 8; p.NpcUpdateIntervalMs = 600;
                    p.MaxTrackedNpcs = 48; p.DistantNpcSimulation = true; p.DistantSimIntervalMs = 3000;
                    break;
                default:
                    p.NpcFullSimRadius = 120f; p.MaxNpcChecksPerUpdate = 4; p.NpcUpdateIntervalMs = 1000;
                    p.MaxTrackedNpcs = 24; p.DistantNpcSimulation = false; p.DistantSimIntervalMs = 6000;
                    break;
            }
            return p;
        }
    }
}
