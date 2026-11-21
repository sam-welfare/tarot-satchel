using System;
using System.IO;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;

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
