using System;

namespace TideCombat
{
    // Shared boundary for normal-but-unverified child results. This is only
    // a read-only observation of the existing game route, not a login action.
    internal static class TaskContinuation
    {
        internal static bool IsSafeWorld(WorldSession world)
        {
            return world.Captured && world.VerifiedWorld && !world.InRealm && world.Hud != null &&
                !world.Hud.Locked && !world.Hud.EnemyHealth && !world.Hud.Dead;
        }

        internal static bool SameWindow(IntPtr expectedHandle, uint expectedProcess, IntPtr observedHandle, uint observedProcess)
        {
            return expectedHandle != IntPtr.Zero && expectedProcess != 0 &&
                expectedHandle == observedHandle && expectedProcess == observedProcess;
        }

        internal static bool TryGetCurrentWindow(WorldSession world, out IntPtr handle, out uint processId)
        {
            handle = IntPtr.Zero;
            processId = 0;
            int route = world.InputRouteVersion;
            if (!world.Captured || !world.Tick() || route != world.InputRouteVersion || !IsSafeWorld(world)) return false;
            handle = world.Window.Handle;
            processId = world.Window.ProcessId;
            return handle != IntPtr.Zero && processId != 0;
        }
    }
}
