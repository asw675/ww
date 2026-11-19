using System;
using System.Diagnostics;
using System.Threading;

namespace TideCombat
{
    internal enum CombatOutcome { Finished, NoCombat, Dead, Timeout }

    // Shared combat loop: runs the battle planner until enemies are gone, the
    // character dies, or the watch expires.
    internal static class CombatRunner
    {
        internal static CombatOutcome Engage(WorldSession world, CombatSettings settings, CancellationToken token,
            int timeoutMs = 120000, int waitForCombatMs = 0)
        {
            var planner = new BattlePlanner(settings);
            if (waitForCombatMs > 0)
            {
                var waiting = Stopwatch.StartNew();
                while (waiting.ElapsedMilliseconds < waitForCombatMs && !token.IsCancellationRequested)
                {
                    if (!world.Tick())
                    {
                        world.Sleep(120);
                        continue;
                    }
                    HudSnapshot probe = world.Hud;
                    if (probe != null && probe.Dead)
                        return CombatOutcome.Dead;
                    if (probe != null && (probe.EnemyHealth || probe.Locked))
                        break;
                    token.WaitHandle.WaitOne(80);
                }
                if (world.Hud == null || !(world.Hud.EnemyHealth || world.Hud.Locked))
                    return CombatOutcome.NoCombat;
            }
            var watch = Stopwatch.StartNew();
            long lastSeen = -1;
            while (!token.IsCancellationRequested)
            {
                long now = watch.ElapsedMilliseconds;
                if (!world.Tick())
                {
                    world.Sleep(120);
                    continue;
                }
                HudSnapshot hud = world.Hud;
                if (hud != null && hud.Dead)
                    return CombatOutcome.Dead;
                if (hud != null && (hud.EnemyHealth || hud.Locked))
                {
                    lastSeen = now;
                    BattleAction action = planner.Observe(hud, now);
                    if (planner.TakeReleaseRequest())
                        try { world.BattleInput.ReleaseAll(); } catch (Exception) { }
                    if (action.Kind != ActionKind.Wait && world.Send(action))
                        planner.Sent(action, now);
                }
                else if (lastSeen >= 0 && now - lastSeen > 2500)
                    return CombatOutcome.Finished;
                if (now > timeoutMs)
                    return CombatOutcome.Timeout;
                token.WaitHandle.WaitOne(50);
            }
            return CombatOutcome.Timeout;
        }
    }
}
