using System;
using System.IO;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace TarotSatchel;

/// <summary>
/// Debug helper: dump every tarot card's official portrait textures so a workshop cover can be cut
/// from the real art. Enabled once per session by <c>TAROT_SATCHEL_DUMP_ART=&lt;absolute dir&gt;</c>.
/// </summary>
public static class ArtDump
{
    private static bool _done;

    public static void TryRun()
    {
        TryDumpPortraits();
        TryRenderCard();
    }

    private static void TryDumpPortraits()
    {
        if (_done) return;
        var dir = System.Environment.GetEnvironmentVariable("TAROT_SATCHEL_DUMP_ART");
        if (string.IsNullOrWhiteSpace(dir)) return;
        _done = true;
        try
        {
            Directory.CreateDirectory(dir);
            int saved = 0;
            foreach (var card in TarotCards.All)
            {
                if (card == null) continue;
                string id;
                try { id = card.Id.ToString(); } catch { continue; }
                saved += Save(card.Portrait, Path.Combine(dir, id + "_portrait.png"));
                saved += Save(card.BigPortrait, Path.Combine(dir, id + "_big.png"));
            }
            ModEntry.Log.Info($"[art] dumped {saved} tarot portrait(s) to {dir}", 1);
        }
        catch (Exception e)
        {
            ModEntry.Log.Warn($"[art] dump failed: {e.Message}", 1);
        }
    }

    private static bool _renderStarted;

    /// <summary>
    /// Render one official card face (frame + art + title) off-screen through the game's own
    /// <c>card.tscn</c> and save it as PNG: TAROT_SATCHEL_RENDER_CARD=card id, TAROT_SATCHEL_RENDER_OUT=png path.
    /// </summary>
    private static void TryRenderCard()
    {
        if (_renderStarted) return;
        var cardId = System.Environment.GetEnvironmentVariable("TAROT_SATCHEL_RENDER_CARD");
        var outPath = System.Environment.GetEnvironmentVariable("TAROT_SATCHEL_RENDER_OUT");
        if (string.IsNullOrWhiteSpace(cardId) || string.IsNullOrWhiteSpace(outPath)) return;
        _renderStarted = true;
        RenderCardAsync(cardId, outPath);
    }

    private static async void RenderCardAsync(string cardId, string outPath)
    {
        try
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree == null) return;
            var viewport = new SubViewport
            {
                Size = new Vector2I(760, 1100),
                TransparentBg = true,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            };
            tree.Root.AddChild(viewport);

            var packed = GD.Load<PackedScene>("res://scenes/cards/card.tscn");
            if (packed == null)
            {
                ModEntry.Log.Warn("[art] card.tscn not found", 1);
                return;
            }
            var model = MegaCrit.Sts2.Core.Models.ModelDb.GetByIdOrNull<CardModel>(
                MegaCrit.Sts2.Core.Models.ModelId.Deserialize(cardId));
            if (model == null)
            {
                ModEntry.Log.Warn($"[art] card model not found: {cardId}", 1);
                return;
            }
            var cardNode = MegaCrit.Sts2.Core.Nodes.Cards.NCard.Create(
                model, MegaCrit.Sts2.Core.Entities.UI.ModelVisibility.Visible);
            if (cardNode == null)
            {
                ModEntry.Log.Warn("[art] NCard.Create returned null", 1);
                return;
            }
            viewport.AddChild(cardNode);
            for (int i = 0; i < 20; i++)
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

            ModEntry.Log.Info($"[art] card node size={cardNode.Size} pos={cardNode.Position}", 1);
            var image = viewport.GetTexture().GetImage();
            if (image == null)
            {
                ModEntry.Log.Warn("[art] viewport image null", 1);
                return;
            }
            var error = image.SavePng(outPath);
            ModEntry.Log.Info($"[art] rendered {cardId} -> {outPath} ({error})", 1);
            viewport.QueueFree();
        }
        catch (Exception e)
        {
            ModEntry.Log.Warn($"[art] render failed: {e.Message}", 1);
        }
    }

    private static int Save(Texture2D texture, string path)
    {
        if (texture == null) return 0;
        try
        {
            var image = texture.GetImage();
            if (image == null) return 0;
            var error = image.SavePng(path);
            if (error != Error.Ok)
            {
                ModEntry.Log.Warn($"[art] save {path} failed: {error}", 1);
                return 0;
            }
            return 1;
        }
        catch (Exception e)
        {
            ModEntry.Log.Warn($"[art] save {path} failed: {e.Message}", 1);
            return 0;
        }
    }
}
