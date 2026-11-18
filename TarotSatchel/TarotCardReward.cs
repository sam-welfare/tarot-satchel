using System;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace TarotSatchel;

/// <summary>
/// Tarot reward built on the official <see cref="SpecialCardReward"/> ("a reward that adds a
/// specific card to the player's deck"). Using the official type gives us the official reward row,
/// hover tips and serialization (<see cref="SerializableReward.SpecialCard"/>); the room keeps it in
/// <c>SerializableRoom.ExtraRewards</c>, so it survives save &amp; quit without any custom state.
/// Selecting it moves the card from the deck into the tarot belt instead of keeping it in the deck.
/// </summary>
public sealed class TarotCardReward : SpecialCardReward
{
    private readonly ModelId _tarotId;

    public TarotCardReward(CardModel tarot, Player player) : base(tarot, player)
    {
        _tarotId = tarot.Id;
        Tarot = tarot;
    }

    public ModelId TarotId => _tarotId;

    public CardModel Tarot { get; }

    protected override async Task<bool> OnSelect()
    {
        // The official flow tries to add the card to the deck; our model vetoes that through
        // ShouldAddToDeck (so "on card added to deck" relics never trigger), which means the base
        // result may be false. The player action itself is what matters here.
        try { await base.OnSelect(); }
        catch (Exception e) { ModEntry.Log.Warn($"[reward] official select flow: {e.Message}", 1); }

        try
        {
            var player = Player;
            var st = SatchelStore.ForPlayer(player);
            var runState = player.RunState as RunState;

            // Defensive cleanup in case the card slipped into the deck anyway.
            var deckCard = player.Deck?.Cards?.FirstOrDefault(c => c.Id == _tarotId);
            if (deckCard != null && runState != null) runState.RemoveCard(deckCard);

            if (st.Slots.Count >= SatchelStore.MaxSlots)
            {
                SatchelBelt.PlayFullFeedback();
                ModEntry.Log.Info($"[reward] belt full; tarot {_tarotId} declined", 1);
                return false;
            }

            st.Slots.Add(_tarotId.ToString());
            // Keep PendingRewardId/Key: the room's outcome stays fixed, so a later save point in the
            // same room must not roll a second time. PreRollRewardAtSavePoint clears it when the next
            // room is rolled.
            SatchelStore.MarkDirty();
            SatchelBelt.RefreshAll();
            ModEntry.Log.Info($"[reward] claimed tarot {_tarotId}; slots=[{string.Join("|", st.Slots)}]", 1);
            return true;
        }
        catch (Exception e)
        {
            ModEntry.Log.Warn($"[reward] tarot claim failed: {e}", 1);
            return false;
        }
    }
}
