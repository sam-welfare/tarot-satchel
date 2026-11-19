using Godot;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.addons.mega_text;

namespace TarotSatchel;

/// <summary>
/// Sets the tarot reward row text the moment the official reward button receives its reward
/// (postfix of <c>NRewardButton.SetReward</c>), so the generic label never shows up even for a frame.
/// Replaces the old poller that renamed the label after the fact (visible English flash).
/// </summary>
public static class RewardRowLabel
{
    public static void Apply(NRewardButton button)
    {
        try
        {
            if (button?.Reward is not TarotCardReward tarotReward) return;
            var label = FindLabel(button);
            if (label == null) return;
            string desired = LocText.TarotRowPrefix + TarotNames.GetDisplayName(tarotReward.Tarot);
            if (label.Text != desired) label.Text = desired;
        }
        catch (System.Exception e)
        {
            ModEntry.Log.Warn($"[ui] reward row label failed: {e.Message}", 1);
        }
    }

    private static MegaRichTextLabel FindLabel(Node node)
    {
        if (node == null) return null;
        if (node is MegaRichTextLabel label) return label;
        foreach (var child in node.GetChildren())
        {
            var found = FindLabel(child);
            if (found != null) return found;
        }
        return null;
    }
}
