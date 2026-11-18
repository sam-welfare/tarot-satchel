using System.Collections.Generic;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Runs;

namespace TarotSatchel;

[ModInitializer("Initialize")]
public static class ModEntry
{
    public static readonly Logger Log = new Logger("TarotSatchel", LogType.Generic);

    public static SatchelRunModel Model { get; private set; }

    public static void Initialize()
    {
        Log.Info("TarotSatchel initializing", 1);
        Model = new SatchelRunModel();
        ModHelper.SubscribeForRunStateHooks("TarotSatchel", OnRunHooks);
        ModHelper.SubscribeForCombatStateHooks("TarotSatchel", OnCombatHooks);
        Log.Info("TarotSatchel subscribed to run/combat hooks", 1);
        SatchelPoller.Start();
        TestDriver.Start();
        PersistencePatcher.Install();
    }

    private static IEnumerable<AbstractModel> OnRunHooks(RunState runState)
    {
        if (runState != null) Model.Attach(runState);
        return new AbstractModel[] { Model };
    }

    private static IEnumerable<AbstractModel> OnCombatHooks(CombatState combatState)
    {
        return new AbstractModel[] { Model };
    }
}
