using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace TarotSatchel;

/// <summary>Per-player tarot satchel state (persisted to a mod-owned JSON file keyed by run seed).</summary>
public sealed class PlayerSatchel
{
    public double Pity { get; set; } = 0.15;
    public List<string> Slots { get; set; } = new();
    public List<string> Queued { get; set; } = new();

    /// <summary>
    /// Per-entry combat key (act:floor) the queued tarot was injected into; aligned with
    /// <see cref="Queued"/>. Empty/null = not injected yet. This lets a tarot expire exactly after
    /// its own combat without dropping a tarot used later.
    /// </summary>
    public List<string> QueuedCombatKeys { get; set; } = new();
    public bool CombatSeenWhileQueued { get; set; }

    /// <summary>Legacy field kept for JSON compatibility (unused).</summary>
    public string LastRewardKey { get; set; }

    /// <summary>Legacy field kept for JSON compatibility (unused).</summary>
    public string PendingRewardId { get; set; }

    /// <summary>Legacy field kept for JSON compatibility (unused).</summary>
    public string PendingRewardKey { get; set; }

    /// <summary>Legacy field kept for JSON compatibility (unused).</summary>
    public string InjectedCombatKey { get; set; }

    /// <summary>Identity of the official checkpoint this state was written for (SerializableRoom).</summary>
    public string LastCheckpoint { get; set; }
}

public static class SatchelStore
{
    public const int MaxSlots = 2;

    private static readonly Dictionary<ulong, PlayerSatchel> _satchels = new();
    private static string _filePath;
    private static bool _dirty;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static string Seed { get; private set; }

    public static void InitForRun(IRunState runState)
    {
        string seed = runState?.Rng?.StringSeed ?? "unknown";
        if (seed == Seed && _filePath != null) return;
        Seed = seed;
        _satchels.Clear();
        string dir = Path.Combine(OS.GetUserDataDir(), "TarotSatchel");
        Directory.CreateDirectory(dir);
        string safe = string.Join("_", seed.Split(Path.GetInvalidFileNameChars()));
        _filePath = Path.Combine(dir, safe + ".json");
        if (File.Exists(_filePath))
        {
            try
            {
                var data = JsonSerializer.Deserialize<Dictionary<string, PlayerSatchel>>(File.ReadAllText(_filePath));
                if (data != null)
                    foreach (var kv in data)
                        if (ulong.TryParse(kv.Key, out var netId)) _satchels[netId] = kv.Value;
                var summary = string.Join("; ", _satchels.Select(kv =>
                    $"{kv.Key}: slots=[{string.Join("|", kv.Value.Slots)}] queued=[{string.Join("|", kv.Value.Queued)}] pity={kv.Value.Pity:F2}"));
                ModEntry.Log.Info($"[store] loaded {_satchels.Count} satchel(s) for seed {seed} from {_filePath}: {summary}", 1);
            }
            catch (System.Exception e)
            {
                ModEntry.Log.Warn($"[store] failed to load state file: {e.Message}", 1);
            }
        }
        else
        {
            ModEntry.Log.Info($"[store] new state file for seed {seed}: {_filePath}", 1);
        }
    }

    /// <summary>
    /// Force a fresh read from disk. "Save &amp; quit" returns to the main menu and "Continue" builds a
    /// new RunState in the same process; the static cache would otherwise keep the in-room mutations
    /// (claimed/used tarots) that vanilla rolls back.
    /// </summary>
    public static void ReloadFromDisk(IRunState runState)
    {
        try
        {
            Seed = null;
            _filePath = null;
            _satchels.Clear();
            ModEntry.Log.Info("[store] reloading state from disk (new run instance)", 1);
            InitForRun(runState);
        }
        catch (System.Exception e)
        {
            ModEntry.Log.Warn($"[store] reload from disk failed: {e.Message}", 1);
        }
    }

    public static PlayerSatchel ForPlayer(Player player)
    {
        if (!_satchels.TryGetValue(player.NetId, out var st))
        {
            // Do NOT write here: the file must only change at official save points, otherwise a
            // lazily created satchel would persist mid-room state that vanilla rolls back.
            st = new PlayerSatchel();
            _satchels[player.NetId] = st;
        }
        return st;
    }

    /// <summary>
    /// Mark state as changed. The file is only written when the game itself saves the run
    /// (<c>SaveManager.SaveRun</c>), so the mod state save/loads exactly like vanilla state.
    /// </summary>
    public static void MarkDirty() => _dirty = true;

    /// <summary>Write the state file if anything changed since the last game save.</summary>
    public static void FlushIfDirty()
    {
        if (!_dirty) return;
        _dirty = false;
        Save();
    }

    public static void Save()
    {
        if (_filePath == null) return;
        try
        {
            var data = _satchels.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(data, JsonOpts));
            var summary = string.Join("; ", _satchels.Select(kv =>
                $"{kv.Key}: slots=[{string.Join("|", kv.Value.Slots)}] queued=[{string.Join("|", kv.Value.Queued)}] pity={kv.Value.Pity:F2}"));
            ModEntry.Log.Info($"[store] wrote {Path.GetFileName(_filePath)}: {summary}", 1);
        }
        catch (System.Exception e)
        {
            ModEntry.Log.Warn($"[store] failed to save state: {e.Message}", 1);
        }
    }

    /// <summary>Compact state summary for save/load diagnostics.</summary>
    public static string SnapshotSummary()
    {
        try
        {
            return string.Join("; ", _satchels.Select(kv =>
                $"{kv.Key}: slots=[{string.Join("|", kv.Value.Slots)}] queued=[{string.Join("|", kv.Value.Queued)}] pity={kv.Value.Pity:F2}"));
        }
        catch
        {
            return "?";
        }
    }

    /// <summary>Resolve a saved ModelId string back to the canonical card model.</summary>
    public static CardModel ResolveCard(string id)
    {
        try { return ModelDb.GetByIdOrNull<CardModel>(ModelId.Deserialize(id)); }
        catch { return null; }
    }
}
