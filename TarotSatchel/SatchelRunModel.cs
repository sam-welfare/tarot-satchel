using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace TarotSatchel;

public sealed class SatchelRunModel : RunModel
{
    private const double EliteBonus = 0.125;
    private const double DropStep = 0.20;
    private const double MissStep = 0.10;

    private CombatRoom _lastRewardRoom;
    private RunState _state;
    private bool _loggedAttach;
    private bool _baseStateWarned;
    private bool _idInitialized;

    /// <summary>
    /// Called from the postfix of the official <c>RunManager.ToSave(AbstractRoom)</c>. The returned
    /// <c>SerializableRun.PreFinishedRoom</c> IS the game's checkpoint, so its identity (SerializableRoom
    /// state fields) decides whether the checkpoint advanced. "Save &amp; quit", act transitions before
    /// the Ancient event, and event-option combats all re-save the same checkpoint room, so their
    /// identity is unchanged and nothing is written; in-room actions therefore roll back exactly like
    /// vanilla HP/rewards.
    /// </summary>
    public static void OnOfficialSnapshot(AbstractRoom room, MegaCrit.Sts2.Core.Saves.SerializableRun serializable)
    {
        try
        {
            var checkpointRoom = serializable?.PreFinishedRoom;
            if (checkpointRoom == null)
            {
                // Treasure/shop/rest-site rooms have no pre-finished serialization, and the map-screen
                // save is not a checkpoint either. Their checkpoint is handled at room entry instead
                // (AfterRoomEntered), so nothing is written here.
                ModEntry.Log.Info("[store] ToSave has no pre-finished room; not a checkpoint, skipped", 1);
                return;
            }
            string identity = CheckpointIdentity(checkpointRoom);
            if (_quitting)
            {
                ModEntry.Log.Info($"[store] save-and-quit re-save; not a checkpoint, skipped: {identity}", 1);
                return;
            }
            if (_ignoreNextSnapshot)
            {
                // The first save after a load re-saves the snapshot we just restored from disk.
                _ignoreNextSnapshot = false;
                _lastCheckpointIdentity = identity;
                ModEntry.Log.Info($"[store] load-boundary save adopted without writing: {identity}", 1);
                return;
            }
            if (identity == _lastCheckpointIdentity)
            {
                ModEntry.Log.Info($"[store] checkpoint unchanged; skipped: {identity}", 1);
                return;
            }
            _lastCheckpointIdentity = identity;

            var run = ModEntry.Model?.CurrentRun;
            if (run == null) return;
            PreRollRewardAtSavePoint(run, room);
            foreach (var player in run.Players)
            {
                try { SatchelStore.ForPlayer(player).LastCheckpoint = identity; } catch { }
            }
            SatchelStore.Save();
            ModEntry.Log.Info($"[store] checkpoint advanced -> {identity}: {SatchelStore.SnapshotSummary()}", 1);
        }
        catch (Exception e)
        {
            ModEntry.Log.Warn($"[store] official snapshot handling failed: {e.Message}", 1);
        }
    }

    private static string _lastCheckpointIdentity;
    private static bool _quitting;
    private static bool _ignoreNextSnapshot;

    /// <summary>
    /// Called from the official pause-menu "save &amp; quit" handler. The re-save that follows when
    /// returning to the main menu must not persist in-room actions (vanilla rolls them back).
    /// </summary>
    public static void BeginQuit()
    {
        if (_quitting) return;
        _quitting = true;
        ModEntry.Log.Info("[store] save-and-quit detected; no mod state will be written during the quit", 1);
    }

    /// <summary>Identity of the official checkpoint room (all SerializableRoom state fields).</summary>
    private static string CheckpointIdentity(MegaCrit.Sts2.Core.Saves.Runs.SerializableRoom room)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(room.RoomType).Append('|')
          .Append(room.EncounterId).Append('|')
          .Append(room.EventId).Append('|')
          .Append(room.IsPreFinished).Append('|')
          .Append(room.ParentEventId).Append('|')
          .Append(room.ShouldResumeParentEvent);
        if (room.EncounterState != null)
        {
            foreach (var kv in room.EncounterState.OrderBy(k => k.Key, StringComparer.Ordinal))
                sb.Append('|').Append(kv.Key).Append('=').Append(kv.Value);
        }
        return sb.ToString();
    }

    private readonly HashSet<CombatRoom> _injectedRooms = new();
    private static readonly Dictionary<string, int> _injectedThisPass = new();

    public override bool ShouldReceiveCombatHooks => true;

    /// <summary>The run this model is attached to. Kept locally because the base RunModel
    /// state property is not writable from a mod-subscription model.</summary>
    public RunState CurrentRun => _state;

    public void Attach(RunState runState)
    {
        if (runState == null) return;
        _state = runState;
        if (!_idInitialized)
        {
            _idInitialized = true;
            try
            {
                InitId(new ModelId("run_model", "tarot_satchel"));
                ModEntry.Log.Info($"[run] model id initialized late: {Id}", 1);
            }
            catch (Exception e)
            {
                ModEntry.Log.Warn($"[run] late model id init failed: {e.Message}", 1);
            }
        }
        try { RunState = runState; }
        catch (Exception e)
        {
            if (!_baseStateWarned)
            {
                _baseStateWarned = true;
                ModEntry.Log.Warn($"[run] base RunState assignment failed (keeping local state): {e.Message}", 1);
            }
        }
        if (!_loggedAttach)
        {
            _loggedAttach = true;
            ModEntry.Log.Info($"[run] attached seed={runState.Rng?.StringSeed} players={runState.Players?.Count}", 1);
        }
        // A new RunState instance means the run was (re)loaded (including "save & quit" → main menu →
        // continue in the same process): re-read the file so the mod state rolls back with vanilla.
        if (!ReferenceEquals(_lastRunInstance, runState))
        {
            _lastRunInstance = runState;
            SatchelStore.ReloadFromDisk(runState);
            // Adopt the checkpoint identity that was persisted with the loaded state, so the load-time
            // ToSave (which re-saves the same checkpoint) is skipped and no reward is pre-rolled twice.
            _lastCheckpointIdentity = null;
            _quitting = false;
            _ignoreNextSnapshot = true;
            _injectedRooms.Clear();
            try
            {
                foreach (var player in runState.Players)
                {
                    var st = SatchelStore.ForPlayer(player);
                    if (!string.IsNullOrEmpty(st.LastCheckpoint)) { _lastCheckpointIdentity = st.LastCheckpoint; break; }
                }
            }
            catch { }
            _pendingPreviewCards.Clear();
        }
        SatchelStore.InitForRun(runState);
        SatchelBelt.RefreshAll();
    }

    private static RunState _lastRunInstance;

    public override Task BeforeCombatStart()
    {
        _lastRewardRoom = null;
        // Official hook that runs before combat starts, i.e. before the deck is shuffled into the
        // draw pile: adding the tarot here lets the vanilla shuffle place it, so the resulting order
        // is deterministic from the saved RNG instead of being perturbed afterwards.
        try { PreShuffleInject(); }
        catch (Exception e) { ModEntry.Log.Warn($"[combat] pre-shuffle inject failed: {e.Message}", 1); }
        return Task.CompletedTask;
    }

    private void PreShuffleInject()
    {
        var runState = CurrentRun;
        if (runState == null) return;
        var room = runState.CurrentRoom as CombatRoom;
        var combatState = room?.CombatState;
        if (combatState == null)
        {
            ModEntry.Log.Info("[combat] pre-shuffle inject: combat state not ready yet; will retry", 1);
            return;
        }
        // BeforeCombatStart and AfterRoomEntered can both fire for one combat. The official
        // AddGeneratedCardToCombat is asynchronous, so a second call would race the first one and fail
        // (observed: "Index was out of range" plus a duplicate attempt). Inject exactly once per combat.
        if (!_injectedRooms.Add(room))
        {
            ModEntry.Log.Info("[combat] pre-shuffle inject already performed for this combat; skipped", 1);
            return;
        }

        string key = RoomKey(runState);
        foreach (var player in runState.Players)
        {
            var st = SatchelStore.ForPlayer(player);
            if (st.Queued.Count == 0) continue;
            NormalizeQueueKeys(st);

            var pile = CardPile.Get(PileType.Draw, player);
            if (pile == null) continue;

            for (int i = st.Queued.Count - 1; i >= 0; i--)
            {
                string queuedId = st.Queued[i];
                string entryKey = st.QueuedCombatKeys[i];

                // Per-entry expiry: a tarot that was injected in an earlier combat is done. A tarot
                // used after that combat (entryKey empty) is untouched, and the same combat key
                // (reload) keeps its entry so it can be re-injected before the shuffle.
                if (!string.IsNullOrEmpty(entryKey) && !string.IsNullOrEmpty(key) && entryKey != key)
                {
                    ModEntry.Log.Info($"[combat] queued tarot {queuedId} expired after combat {entryKey}", 1);
                    st.Queued.RemoveAt(i);
                    st.QueuedCombatKeys.RemoveAt(i);
                    SatchelStore.MarkDirty();
                    continue;
                }

                var modelId = ModelId.Deserialize(queuedId);
                // Duplicate tarots are legitimate (you can use two copies of the same card), so the
                // injection decision is count-based: queued copies of this id vs copies already in
                // the combat piles plus copies injected in this pass.
                int queuedSameId = st.Queued.Count(x => x == queuedId);
                int inPiles = CountInCombatPiles(player, modelId);
                int injectedSameId = _injectedThisPass.TryGetValue(queuedId, out var n) ? n : 0;
                if (inPiles + injectedSameId >= queuedSameId)
                {
                    st.QueuedCombatKeys[i] = key;
                    SatchelStore.MarkDirty();
                    continue;
                }
                var canonical = SatchelStore.ResolveCard(queuedId);
                if (canonical == null) continue;

                CardModel proto = canonical;
                try { if (canonical.CanonicalInstance != null) proto = canonical.CanonicalInstance; } catch { }

                CardModel instance = null;
                try { instance = combatState.CreateCard(proto, player); } catch { }
                if (instance == null) continue;

                try
                {
                    // The asynchronous CardPileCmd.AddGeneratedCardToCombat throws "Index was out of
                    // range" when called this early (before the shuffle/UI is ready), which silently
                    // dropped the tarot. Add synchronously through the same official pile API at a
                    // random index instead; the flying-card animation still comes from CardCmd.Preview
                    // in BeforeHandDraw.
                    int count = pile.Cards?.Count ?? 0;
                    int index = count > 0
                        ? runState.Rng.UpFront.NextInt(count + 1)
                        : 0;
                    pile.AddInternal(instance, index, true);
                    st.QueuedCombatKeys[i] = key;
                    SatchelStore.MarkDirty();
                    _injectedThisPass[queuedId] = injectedSameId + 1;
                    _pendingPreviewCards.Add(instance);
                    ModEntry.Log.Info($"[combat] pre-shuffle injected {queuedId} into draw pile at {index}/{count} (pile={pile.Cards?.Count})", 1);
                }
                catch (Exception e)
                {
                    ModEntry.Log.Warn($"[combat] pre-shuffle insert failed for {queuedId}: {e.Message}", 1);
                }
            }
            _injectedThisPass.Clear();
        }
        SatchelBelt.RefreshAll();
    }

    public override Task AfterRoomEntered(AbstractRoom room)
    {
        // Another official pre-shuffle opportunity: retry the pre-shuffle injection as soon as the
        // combat room is entered, so the fallback (post-shuffle) path is practically unnecessary.
        try
        {
            if (room is CombatRoom) PreShuffleInject();
        }
        catch (Exception e)
        {
            ModEntry.Log.Warn($"[combat] pre-shuffle retry failed: {e.Message}", 1);
        }
        SatchelBelt.RefreshAll();
        // Treasure/shop/rest-site rooms have no pre-finished serialization, and the Ancient/event
        // rooms only serialize after the event progresses. Their real checkpoint is the room entry, so
        // snapshot it here; in-room actions still roll back until the next checkpoint (e.g. taking the
        // Ancient relic) writes again.
        if (room is TreasureRoom || room is MerchantRoom || room is RestSiteRoom || room is EventRoom)
        {
            try
            {
                string entryIdentity = $"entry:{RoomKey(CurrentRun)}";
                var run = CurrentRun;
                if (run != null)
                {
                    foreach (var player in run.Players)
                    {
                        try { SatchelStore.ForPlayer(player).LastCheckpoint = entryIdentity; } catch { }
                    }
                }
                SatchelStore.Save();
                ModEntry.Log.Info($"[store] room-entry checkpoint ({room.RoomType}) {entryIdentity}: {SatchelStore.SnapshotSummary()}", 1);
            }
            catch (Exception e)
            {
                ModEntry.Log.Warn($"[store] room-entry save failed: {e.Message}", 1);
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>Sanity: keep <see cref="PlayerSatchel.QueuedCombatKeys"/> aligned with the queue.</summary>
    private static void NormalizeQueueKeys(PlayerSatchel st)
    {
        try
        {
            while (st.QueuedCombatKeys.Count < st.Queued.Count) st.QueuedCombatKeys.Add(null);
            while (st.QueuedCombatKeys.Count > st.Queued.Count) st.QueuedCombatKeys.RemoveAt(st.QueuedCombatKeys.Count - 1);
        }
        catch { }
    }

    /// <summary>Stable per-room key from the official run state (act index + total floor).</summary>
    private static string RoomKey(RunState runState)
    {
        try
        {
            if (runState == null) return null;
            return $"{runState.CurrentActIndex}:{runState.TotalFloor}";
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Tarot card ids (the game's own tarot pool).</summary>
    public static bool IsTarotCard(CardModel card)
    {
        if (card == null) return false;
        try
        {
            var all = TarotCards.All;
            if (all == null) return false;
            foreach (var c in all)
                if (c != null && c.Id.Equals(card.Id)) return true;
        }
        catch { }
        return false;
    }

    // Official hooks: a tarot card may never enter the persistent deck, so "when a card is added to
    // your deck" relics never see it (ShouldAddToDeck vetoes the add before modification hooks run).
    public override bool ShouldAddToDeck(CardModel card) => !IsTarotCard(card);

    public override Task AfterAddToDeckPrevented(CardModel card)
    {
        if (IsTarotCard(card))
            ModEntry.Log.Info($"[relic] vetoed deck add of tarot {card?.Id}; relics cannot affect tarots", 1);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called when the run left combat. The queued tarot belongs to the next combat only, but the
    /// queue is cleared lazily when a *new* combat starts (see <see cref="InjectQueuedIntoCombat"/>):
    /// clearing on combat exit would drop it when the player saves &amp; quits mid-combat.
    /// </summary>
    public void RemoveQueuedAfterCombat()
    {
    }

    /// <summary>
    /// Queue bookkeeping only. Injection itself happens strictly before the shuffle
    /// (<see cref="PreShuffleInject"/> via BeforeCombatStart / AfterRoomEntered) so the tarot takes
    /// part in the official shuffle; there is deliberately no post-shuffle fallback.
    /// </summary>
    public void InjectQueuedIntoCombat(CombatRoom room)
    {
        var runState = CurrentRun;
        if (runState == null || room == null) return;

        var combatState = room.CombatState;
        if (combatState == null) return;

        string combatKey = RoomKey(runState);
        foreach (var player in runState.Players)
        {
            var st = SatchelStore.ForPlayer(player);
            if (st.Queued.Count == 0) continue;
            NormalizeQueueKeys(st);

            for (int i = st.Queued.Count - 1; i >= 0; i--)
            {
                string queuedId = st.Queued[i];
                string entryKey = st.QueuedCombatKeys[i];
                if (!string.IsNullOrEmpty(entryKey) && !string.IsNullOrEmpty(combatKey) && entryKey != combatKey)
                {
                    ModEntry.Log.Info($"[combat] queued tarot {queuedId} expired after combat {entryKey}", 1);
                    st.Queued.RemoveAt(i);
                    st.QueuedCombatKeys.RemoveAt(i);
                    SatchelStore.MarkDirty();
                    continue;
                }

                bool alreadyInPile = false;
                try { alreadyInPile = IsCardInCombatPiles(player, ModelId.Deserialize(queuedId)); } catch { }
                if (alreadyInPile)
                {
                    st.QueuedCombatKeys[i] = combatKey;
                    st.CombatSeenWhileQueued = true;
                    ModEntry.Log.Info($"[combat] {queuedId} already in a combat pile; marking injected for {combatKey}", 1);
                    continue;
                }

                // Pre-shuffle injection did not run for this combat. Keep the official order intact
                // and do not inject after the shuffle; the entry expires when the next combat starts.
                st.QueuedCombatKeys[i] = combatKey;
                ModEntry.Log.Warn($"[combat] pre-shuffle injection missed for {queuedId} in {combatKey}; not injecting after the shuffle", 1);
            }
            SatchelStore.MarkDirty();
        }
        SatchelBelt.RefreshAll();
    }

    /// <summary>
    /// Official animation + hand-draw ordering, copied from <c>BlessedAntler</c>: the engine awaits
    /// <c>BeforeHandDraw</c> before it draws the opening hand, and <c>CardCmd.Preview</c> returns a
    /// TaskCompletionSource that completes when the NCard finishes flying into its pile. Awaiting it
    /// here therefore makes the tarot preview finish *before* cards are drawn into the hand.
    /// Multiple tarots used in one run are all previewed in this single hook.
    /// </summary>
    public override async Task BeforeHandDraw(Player player, PlayerChoiceContext context, ICombatState combatState)
    {
        if (_pendingPreviewCards.Count == 0) return;
        var cards = new List<CardModel>();
        foreach (var card in _pendingPreviewCards)
        {
            if (card == null) continue;
            if (player != null && card.Owner != null && card.Owner.NetId != player.NetId) continue;
            cards.Add(card);
        }
        if (cards.Count == 0) return;

        try
        {
            var burst = System.Environment.GetEnvironmentVariable("TAROT_SATCHEL_BURST");
            if (!string.IsNullOrEmpty(burst)) SatchelPoller.BurstShots(burst, 16, 0.35f);
            foreach (var card in cards)
            {
                var tcs = MegaCrit.Sts2.Core.Commands.CardCmd.Preview(
                    card, 1.0f, MegaCrit.Sts2.Core.Nodes.CommonUi.CardPreviewStyle.HorizontalLayout);
                if (tcs != null) await tcs.Task;
                ModEntry.Log.Info($"[combat] preview finished before hand draw for {card.Id}", 1);
            }
        }
        catch (Exception e)
        {
            ModEntry.Log.Warn($"[combat] hand-draw preview failed: {e.Message}", 1);
        }
        finally
        {
            _pendingPreviewCards.RemoveAll(c => cards.Contains(c));
        }
    }

    private static readonly List<CardModel> _pendingPreviewCards = new();

    private static bool IsCardInCombatPiles(Player player, ModelId id)
    {
        return CountInCombatPiles(player, id) > 0;
    }

    /// <summary>Count copies of a card id across all combat piles (handles duplicate tarots).</summary>
    private static int CountInCombatPiles(Player player, ModelId id)
    {
        int count = 0;
        try
        {
            foreach (var pileType in new[] { PileType.Draw, PileType.Hand, PileType.Discard, PileType.Exhaust })
            {
                var pile = CardPile.Get(pileType, player);
                if (pile?.Cards == null) continue;
                foreach (var c in pile.Cards)
                    if (c != null && c.Id.Equals(id)) count++;
            }
        }
        catch { }
        return count;
    }

    public void MarkCombatSeen()
    {
        var runState = CurrentRun;
        if (runState == null) return;
        foreach (var player in runState.Players)
        {
            var st = SatchelStore.ForPlayer(player);
            if (st.Queued.Count > 0 && !st.CombatSeenWhileQueued)
            {
                st.CombatSeenWhileQueued = true;
                SatchelStore.MarkDirty();
            }
        }
    }

    /// <summary>Using a stored tarot only queues it: the card is injected into the next combat's draw pile.</summary>
    public bool UseSlot(Player player, RunState runState, int index)
    {
        var st = SatchelStore.ForPlayer(player);
        if (index < 0 || index >= st.Slots.Count) return false;
        string id = st.Slots[index];
        if (SatchelStore.ResolveCard(id) == null)
        {
            ModEntry.Log.Warn($"[use] could not resolve tarot {id}; dropping slot", 1);
            st.Slots.RemoveAt(index);
            SatchelStore.MarkDirty();
            return false;
        }
        st.Slots.RemoveAt(index);
        st.Queued.Add(id);
        while (st.QueuedCombatKeys.Count < st.Queued.Count) st.QueuedCombatKeys.Add(null);
        st.QueuedCombatKeys[st.Queued.Count - 1] = null;
        st.CombatSeenWhileQueued = false;
        SatchelStore.MarkDirty();
        SatchelBelt.RefreshAll();
        ModEntry.Log.Info($"[use] tarot {id} queued for next combat; slots=[{string.Join("|", st.Slots)}] queued=[{string.Join("|", st.Queued)}] deck={player.Deck?.Cards?.Count}", 1);
        return true;
    }

    public bool DiscardSlot(Player player, int index)
    {
        var st = SatchelStore.ForPlayer(player);
        if (index < 0 || index >= st.Slots.Count) return false;
        string id = st.Slots[index];
        st.Slots.RemoveAt(index);
        SatchelStore.MarkDirty();
        SatchelBelt.RefreshAll();
        ModEntry.Log.Info($"[discard] tarot {id} discarded; slots=[{string.Join("|", st.Slots)}]", 1);
        return true;
    }

    /// <summary>
    /// Register the reward in the room's official extra-reward container, which the game serializes
    /// in <c>SerializableRoom.ExtraRewards</c> and restores on load.
    /// </summary>
    private static void AddRoomExtraReward(CombatRoom room, Player player, Reward reward)
    {
        try
        {
            var field = typeof(CombatRoom).GetField("_extraRewards",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field?.GetValue(room) is not Dictionary<Player, List<Reward>> dict)
            {
                ModEntry.Log.Warn("[persist] room extra rewards container unavailable", 1);
                return;
            }
            if (!dict.TryGetValue(player, out var list))
            {
                list = new List<Reward>();
                dict[player] = list;
            }
            if (!list.Contains(reward)) list.Add(reward);
            ModEntry.Log.Info($"[persist] tarot reward registered in room {room.Id} extra rewards", 1);
        }
        catch (Exception e)
        {
            ModEntry.Log.Warn($"[persist] extra reward registration failed: {e.Message}", 1);
        }
    }

    private static void PreRollRewardAtSavePoint(RunState run, AbstractRoom room)
    {
        try
        {
            if (run == null || room is not CombatRoom combatRoom) return;
            bool preFinished = false;
            try { preFinished = combatRoom.IsPreFinished; } catch { }
            if (!preFinished) return;
            string key = RoomKey(run);
            if (string.IsNullOrEmpty(key)) return;
            bool elite = combatRoom.RoomType == RoomType.Elite;

            foreach (var player in run.Players)
            {
                var st = SatchelStore.ForPlayer(player);
                if (st.PendingRewardKey == key) continue; // this room's reward is already fixed

                // A previous room's pending reward is irrelevant from here on.
                st.PendingRewardId = null;
                st.PendingRewardKey = null;

                double chance;
                double roll;
                var upFront = run.Rng?.UpFront;
                if (upFront == null)
                {
                    // Deterministic rollback requires the official RNG; without it, do not roll at all
                    // (a local Random() would make the saved reward different on every reload).
                    ModEntry.Log.Warn("[reward] official RNG unavailable; skipping pre-roll", 1);
                    continue;
                }
                try { chance = Math.Clamp(st.Pity + (elite ? EliteBonus : 0.0), 0.0, 1.0); }
                catch { chance = 0.0; }
                roll = upFront.NextDouble();

                bool dropped = roll < chance;
                st.Pity = Math.Clamp(st.Pity + (dropped ? -DropStep : MissStep), 0.0, 1.0);
                st.PendingRewardKey = key;

                if (dropped)
                {
                    List<CardModel> pool = null;
                    try { pool = TarotCards.All; } catch (Exception e) { ModEntry.Log.Warn($"[reward] tarot pool unavailable: {e.Message}", 1); }
                    var canonical = PickTarot(pool, run);
                    if (canonical != null) st.PendingRewardId = canonical.Id.ToString();
                    ModEntry.Log.Info($"[reward] pre-rolled {combatRoom.RoomType} drop for {key}: {st.PendingRewardId} roll={roll:F3} chance={chance:F3} pityAfter={st.Pity:F2}", 1);
                }
                else
                {
                    ModEntry.Log.Info($"[reward] pre-rolled {combatRoom.RoomType} no drop for {key}: roll={roll:F3} chance={chance:F3} pityAfter={st.Pity:F2}", 1);
                }
                SatchelStore.MarkDirty();
            }
        }
        catch (Exception e)
        {
            ModEntry.Log.Warn($"[reward] pre-roll failed: {e.Message}", 1);
        }
    }

    private static string SafeOwner(CardModel card)
    {
        try { return card?.Owner == null ? "null" : card.Owner.NetId.ToString(); }
        catch { return "err"; }
    }

    private static CardModel PickTarot(List<CardModel> pool, RunState runState)
    {
        var upFront = runState?.Rng?.UpFront;
        if (pool == null || pool.Count == 0 || upFront == null) return null;
        int idx = upFront.NextInt(pool.Count);
        return idx >= 0 && idx < pool.Count ? pool[idx] : null;
    }

    public override bool TryModifyRewards(Player player, List<Reward> rewards, AbstractRoom room)
    {
        try
        {
            if (player == null || rewards == null || room is not CombatRoom combatRoom) return false;

            var runState = CurrentRun;
            var st = SatchelStore.ForPlayer(player);
            bool elite = combatRoom.RoomType == RoomType.Elite;
            string roomKey = RoomKey(runState);

            if (ReferenceEquals(combatRoom, _lastRewardRoom)) return false;
            _lastRewardRoom = combatRoom;

            // The room may already carry a tarot reward restored from SerializableRoom.ExtraRewards
            // (PersistencePatch maps it back to our reward subclass); never add a second one.
            bool alreadyHasTarot = rewards.Any(r => r is TarotCardReward);
            if (alreadyHasTarot)
            {
                ModEntry.Log.Info($"[reward] room {roomKey} already carries a restored tarot reward; skipping roll", 1);
                return false;
            }

            // Reward outcome was fixed at the official save point (see PreRollRewardAtSavePoint):
            // restore it instead of rolling again, so reloading cannot change the reward or the pity.
            if (st.PendingRewardKey == roomKey)
            {
                if (string.IsNullOrEmpty(st.PendingRewardId))
                {
                    ModEntry.Log.Info($"[reward] room {roomKey} pre-rolled no drop; nothing to add", 1);
                    return false;
                }
                var canonicalPending = SatchelStore.ResolveCard(st.PendingRewardId);
                if (canonicalPending == null)
                {
                    ModEntry.Log.Warn($"[reward] pre-rolled tarot {st.PendingRewardId} no longer resolvable", 1);
                    return false;
                }
                var pendingInst = canonicalPending;
                try { pendingInst = runState?.CreateCard(canonicalPending, player) ?? canonicalPending; }
                catch (Exception e) { ModEntry.Log.Warn($"[reward] card instance create failed: {e.Message}", 1); }
                Reward pendingReward;
                try { pendingReward = new TarotCardReward(pendingInst, player); }
                catch (Exception e)
                {
                    // No fallback reward type: if the official reward cannot be built, add nothing
                    // rather than shipping a second, divergent implementation.
                    ModEntry.Log.Warn($"[reward] official SpecialCardReward failed ({e.Message}); no tarot reward added", 1);
                    return false;
                }
                rewards.Add(pendingReward);
                AddRoomExtraReward(combatRoom, player, pendingReward);
                ModEntry.Log.Info($"[reward] restored pre-rolled tarot {st.PendingRewardId} in room {roomKey}", 1);
                return true;
            }

            // No pre-rolled outcome for this room: add nothing. The drop is decided exclusively at the
            // official save point (PreRollRewardAtSavePoint); there is deliberately no fallback roll,
            // because a second, unsaved roll is exactly what made the reward state differ after a reload.
            ModEntry.Log.Info($"[reward] room {roomKey} has no pre-rolled outcome; no tarot reward added", 1);
            return false;
        }
        catch (Exception e)
        {
            ModEntry.Log.Warn($"[reward] TryModifyRewards failed: {e}", 1);
            return false;
        }
    }
}
