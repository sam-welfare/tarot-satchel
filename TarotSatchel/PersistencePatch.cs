using System;
using System.Collections.Generic;
using System.Linq;
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

            // "Save & quit" re-saves the same checkpoint when leaving to the main menu. Mark the quit
            // flow from the official pause-menu entry so that re-save never writes mod state; genuine
            // checkpoints (room entry, relic taken in the Ancient room, ...) happen outside it.
            // Resolve by name only: the parameter type/visibility is an implementation detail.
            var saveAndQuit = typeof(MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu.NPauseMenu)
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "OnSaveAndQuitButtonPressed");
            var onSaveAndQuit = typeof(PersistencePatcher).GetMethod("OnSaveAndQuit",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (saveAndQuit == null || onSaveAndQuit == null)
            {
                ModEntry.Log.Warn("[persist] save-and-quit patch target not found", 1);
            }
            else
            {
                _harmony.Patch(saveAndQuit, postfix: new HarmonyMethod(onSaveAndQuit));
            }

            // Reward row text: the official button may rewrite its label after SetReward (e.g. Reload
            // on locale refresh), so apply the tarot label after every one of these entry points.
            var onSetReward = typeof(PersistencePatcher).GetMethod("OnSetReward",
                BindingFlags.NonPublic | BindingFlags.Static);
            int rewardPatched = 0;
            if (onSetReward != null)
            {
                var methods = typeof(MegaCrit.Sts2.Core.Nodes.Rewards.NRewardButton)
                    .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (var name in new[] { "SetReward", "Reload", "_Ready" })
                {
                    var target = methods.FirstOrDefault(m => m.Name == name);
                    if (target == null) continue;
                    try
                    {
                        _harmony.Patch(target, postfix: new HarmonyMethod(onSetReward));
                        rewardPatched++;
                    }
                    catch (Exception e)
                    {
                        ModEntry.Log.Warn($"[persist] reward row patch failed for {name}: {e.Message}", 1);
                    }
                }
            }
            if (rewardPatched == 0)
                ModEntry.Log.Warn("[persist] reward row patch target not found", 1);

            // Potion bar layout: only slot-count/size changes move the belt (empty holders stay visible,
            // so using/discarding a potion does not change geometry). GrowPotionHolders covers the
            // relic-driven slot growth; NPotionContainer.Resized is handled inside SatchelBelt.
            var onPotionChanged = typeof(PersistencePatcher).GetMethod("OnPotionContainerChanged",
                BindingFlags.NonPublic | BindingFlags.Static);
            int potionPatched = 0;
            if (onPotionChanged != null)
            {
                var potionMethods = typeof(MegaCrit.Sts2.Core.Nodes.Potions.NPotionContainer)
                    .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (var name in new[] { "GrowPotionHolders" })
                {
                    var target = potionMethods.FirstOrDefault(m => m.Name == name);
                    if (target == null) continue;
                    try
                    {
                        _harmony.Patch(target, postfix: new HarmonyMethod(onPotionChanged));
                        potionPatched++;
                    }
                    catch (Exception e)
                    {
                        ModEntry.Log.Warn($"[persist] potion-bar patch failed for {name}: {e.Message}", 1);
                    }
                }
            }
            if (potionPatched == 0)
                ModEntry.Log.Warn("[persist] potion-bar patch target not found", 1);

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

    private static void OnSaveAndQuit()
    {
        SatchelRunModel.BeginQuit();
    }

    private static void OnSetReward(MegaCrit.Sts2.Core.Nodes.Rewards.NRewardButton __instance)
    {
        RewardRowLabel.Apply(__instance);
    }

    private static void OnPotionContainerChanged()
    {
        SatchelBelt.RefreshSoon();
    }
}
