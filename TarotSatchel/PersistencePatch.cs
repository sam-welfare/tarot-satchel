using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace TarotSatchel;

/// <summary>
/// Tarot rewards are official <see cref="SpecialCardReward"/>s, so the game restores them from the
/// room's <c>SerializableRoom.ExtraRewards</c> by itself. This postfix only maps the restored
/// vanilla reward back to our subclass so selecting it still moves the card into the belt.
/// </summary>
internal static class RewardFromSerializablePatch
{
    private static HashSet<ModelId> _tarotIds;

    private static HashSet<ModelId> TarotIds()
    {
        if (_tarotIds != null) return _tarotIds;
        _tarotIds = new HashSet<ModelId>();
        try
        {
            var all = MegaCrit.Sts2.Core.Entities.Cards.TarotCards.All;
            if (all != null)
                foreach (var card in all)
                    if (card != null) _tarotIds.Add(card.Id);
        }
        catch { }
        return _tarotIds;
    }

    [HarmonyPostfix]
    private static void Postfix(SerializableReward save, Player player, ref Reward __result)
    {
        try
        {
            if (player == null || __result == null) return;
            if (__result is TarotCardReward) return;
            if (__result is not SpecialCardReward special) return;

            var card = ReadCard(special);
            if (card == null || !TarotIds().Contains(card.Id)) return;

            var inst = card;
            try
            {
                var runState = player.RunState as RunState;
                inst = runState?.CreateCard(card, player) ?? card;
            }
            catch { }

            __result = new TarotCardReward(inst, player);
            ModEntry.Log.Info($"[persist] restored tarot reward {card.Id}", 1);
        }
        catch (Exception e)
        {
            ModEntry.Log.Warn($"[persist] reward restore failed: {e.Message}", 1);
        }
    }

    private static CardModel ReadCard(SpecialCardReward reward)
    {
        try
        {
            var field = typeof(SpecialCardReward).GetField("_card", BindingFlags.NonPublic | BindingFlags.Instance);
            return field?.GetValue(reward) as CardModel;
        }
        catch
        {
            return null;
        }
    }
}

public static class PersistencePatcher
{
    private static HarmonyLib.Harmony _harmony;

    public static void Install()
    {
        if (_harmony != null) return;
        try
        {
            _harmony = new HarmonyLib.Harmony("TarotSatchel.Persistence");

            var fromSerializable = typeof(Reward).GetMethod("FromSerializable",
                BindingFlags.Public | BindingFlags.Static);
            var postfix = typeof(RewardFromSerializablePatch).GetMethod("Postfix",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (fromSerializable == null || postfix == null)
            {
                ModEntry.Log.Warn("[persist] reward patch target not found", 1);
            }
            else
            {
                _harmony.Patch(fromSerializable, postfix: new HarmonyMethod(postfix));
            }

            // Official checkpoint: RunManager.ToSave(AbstractRoom) builds the SerializableRun whose
            // PreFinishedRoom is the game's actual checkpoint. Its postfix is the ONLY place where the
            // mod state is written, so cross-act transitions before the Ancient event, event-option
            // combats and "save & quit" (all of which re-save the same checkpoint) never write.
            var toSave = typeof(MegaCrit.Sts2.Core.Runs.RunManager).GetMethod("ToSave",
                BindingFlags.Public | BindingFlags.Instance, null,
                new[] { typeof(MegaCrit.Sts2.Core.Rooms.AbstractRoom) }, null);
            var onToSave = typeof(PersistencePatcher).GetMethod("OnToSave",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (toSave == null || onToSave == null)
            {
                ModEntry.Log.Warn("[persist] ToSave patch target not found", 1);
            }
            else
            {
                _harmony.Patch(toSave, postfix: new HarmonyMethod(onToSave));
            }

            ModEntry.Log.Info("[persist] harmony patches installed", 1);
        }
        catch (Exception e)
        {
            ModEntry.Log.Warn($"[persist] harmony patch failed: {e}", 1);
        }
    }

    // Parameter names must match the official method exactly (Harmony matches by name):
    // RunManager.ToSave(AbstractRoom preFinishedRoom).
    private static void OnToSave(MegaCrit.Sts2.Core.Rooms.AbstractRoom preFinishedRoom,
        MegaCrit.Sts2.Core.Saves.SerializableRun __result)
    {
        SatchelRunModel.OnOfficialSnapshot(preFinishedRoom, __result);
    }
}
