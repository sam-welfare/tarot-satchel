using System;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;

namespace TarotSatchel;

/// <summary>Resolves human-readable names/descriptions for the base game's tarot cards.</summary>
public static class TarotNames
{
    private static readonly string[] LocTables = { "cards", "card", "card_keywords", "keywords", "eng" };
    private static bool _loggedLoc;
    private static bool _loggedMissing;

    public static string EntryOf(ModelId id)
    {
        try
        {
            var s = id.ToString();
            int dot = s.IndexOf('.');
            return dot >= 0 ? s.Substring(dot + 1) : s;
        }
        catch
        {
            return null;
        }
    }

    public static LocString ResolveLoc(CardModel card, string suffix)
    {
        if (card == null) return null;
        try
        {
            var loc = suffix == ".title" ? card.TitleLocString : card.Description;
            if (loc != null && !loc.IsEmpty) return loc;
        }
        catch { }

        var entry = EntryOf(card.Id);
        if (entry == null) return null;
        foreach (var table in LocTables)
        {
            try
            {
                var loc = LocString.GetIfExists(table, entry + suffix);
                if (loc != null && !loc.IsEmpty)
                {
                    if (!_loggedLoc)
                    {
                        _loggedLoc = true;
                        ModEntry.Log.Info($"[names] loc table '{table}' works ('{entry}{suffix}')", 1);
                    }
                    return loc;
                }
            }
            catch { }
        }
        return null;
    }

    public static string GetDisplayName(CardModel card)
    {
        if (card == null) return "Tarot";
        var loc = ResolveLoc(card, ".title");
        if (loc != null) return loc.GetFormattedText();
        if (!_loggedMissing)
        {
            _loggedMissing = true;
            ModEntry.Log.Warn($"[names] could not resolve a display name for {EntryOf(card.Id)}", 1);
        }
        return EntryOf(card.Id) ?? "Tarot";
    }

    public static string GetDescriptionText(CardModel card)
    {
        var loc = ResolveLoc(card, ".description");
        if (loc == null) return string.Empty;
        try
        {
            // Raw text avoids the game's format-string parser (its dynamic selectors such as
            // {Energy:...} are only resolvable in a live combat context and spam errors otherwise).
            var raw = loc.GetRawText();
            if (!string.IsNullOrWhiteSpace(raw)) return raw;
        }
        catch { }
        try { return loc.GetFormattedText(); }
        catch { return string.Empty; }
    }
}
