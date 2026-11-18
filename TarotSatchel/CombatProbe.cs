using System.Linq;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace TarotSatchel;

/// <summary>
/// Shared combat-activity probe. The run's CurrentRoom stays a CombatRoom while the rewards
/// screen is up, so room type alone cannot tell "actually fighting" from "combat finished".
/// </summary>
public static class CombatProbe
{
    public static bool IsInLiveCombat(RunState runState)
    {
        if (runState?.CurrentRoom is not CombatRoom room) return false;
        try
        {
            var combatState = room.CombatState;
            if (combatState == null) return false;
            var enemies = combatState.Enemies;
            return enemies != null && enemies.Any(e => e != null && e.IsAlive);
        }
        catch
        {
            return false;
        }
    }
}
