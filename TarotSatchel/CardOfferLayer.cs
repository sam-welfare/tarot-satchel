using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.HoverTips;

namespace TarotSatchel;

/// <summary>
/// Semi-transparent full-screen card offer layer (official card visuals + hover tips + skip).
/// Works for 1..N cards so future 2/3-choice drops can reuse it unchanged.
/// Returns the picked index, or -1 when dismissed / skipped.
/// </summary>
public static class CardOfferLayer
{
    public static Task<int> ShowAsync(IReadOnlyList<CardModel> cards, Func<int, bool> onPick)
    {
        var tcs = new TaskCompletionSource<int>();
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree?.Root == null || cards == null || cards.Count == 0)
        {
            tcs.TrySetResult(-1);
            return tcs.Task;
        }

        var layer = new CanvasLayer { Name = "TarotOfferLayer", Layer = 100 };
        var root = new Control { Name = "TarotOfferRoot", MouseFilter = Control.MouseFilterEnum.Stop };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(root);

        var dim = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.55f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(dim);
        dim.GuiInput += evt =>
        {
            if (evt is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) Finish(-1);
        };

        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(center);

        var row = new HBoxContainer { Name = "Cards" };
        row.AddThemeConstantOverride("separation", 40);
        center.AddChild(row);

        for (int i = 0; i < cards.Count; i++)
        {
            int index = i;
            var card = cards[i];
            var holder = new Button
            {
                Name = $"TarotOfferCard{index}",
                FocusMode = Control.FocusModeEnum.None,
                Flat = true,
                MouseFilter = Control.MouseFilterEnum.Stop,
            };
            var cardNode = NCard.Create(card, MegaCrit.Sts2.Core.Entities.UI.ModelVisibility.Visible);
            if (cardNode != null)
            {
                cardNode.Scale = new Vector2(0.85f, 0.85f);
                cardNode.MouseFilter = Control.MouseFilterEnum.Ignore;
                holder.CustomMinimumSize = new Vector2(280, 380);
                holder.AddChild(cardNode);
            }
            holder.Pressed += () =>
            {
                bool accepted = false;
                try { accepted = onPick == null || onPick(index); }
                catch (Exception e) { ModEntry.Log.Warn($"[offer] pick failed: {e.Message}", 1); }
                if (accepted) Finish(index);
            };
            holder.MouseEntered += () => ShowTips(holder, card);
            holder.MouseExited += () => HideTips(holder);
            row.AddChild(holder);
        }

        var skip = new Button { Name = "Skip", Text = "跳过" };
        skip.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        skip.Position = new Vector2(-60, -140);
        skip.CustomMinimumSize = new Vector2(140, 40);
        skip.Pressed += () => Finish(-1);
        root.AddChild(skip);

        tree.Root.AddChild(layer);

        void Finish(int result)
        {
            if (!tcs.TrySetResult(result)) return;
            try { layer.QueueFree(); } catch { }
        }

        layer.TreeExiting += () =>
        {
            if (!tcs.Task.IsCompleted) tcs.TrySetResult(-1);
        };

        return tcs.Task;
    }

    private static void ShowTips(Control owner, CardModel card)
    {
        try
        {
            var tips = HoverTipFactory.FromCardWithCardHoverTips(card, true);
            if (tips != null)
            {
                var set = NHoverTipSet.CreateAndShow(owner, tips, HoverTipAlignment.Right);
                set?.SetExtraFollowOffset(new Vector2(16, 0));
            }
        }
        catch (Exception e)
        {
            ModEntry.Log.Warn($"[offer] hover tips failed: {e.Message}", 1);
        }
    }

    private static void HideTips(Control owner)
    {
        try { NHoverTipSet.Remove(owner); } catch { }
    }
}
