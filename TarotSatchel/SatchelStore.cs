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

    /// <summary>Slot capacity; grows when future relics add tarot slots (default 2).</summary>
    public int Capacity { get; set; } = SatchelStore.DefaultCapacity;

    /// <summary>
    /// Fixed-length slot array: index = slot position, null = empty slot. Cards never shift between
    /// slots; claiming fills the leftmost null and using/discarding clears that position.
    /// </summary>
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
    /// <summary>Default slot capacity; future relics may raise <see cref="PlayerSatchel.Capacity"/>.</summary>
    public const int DefaultCapacity = 2;

    /// <summary>Hard cap for the tarot slot capacity (user decision: never more than 3).</summary>
    public const int MaxCapacity = 3;
    public const int MaxSlots = DefaultCapacity; // legacy name, kept for older call sites

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
                foreach (var st in _satchels.Values) NormalizeSlots(st);
                var summary = string.Join("; ", _satchels.Select(kv =>
                    $"{kv.Key}: cap={kv.Value.Capacity} slots=[{string.Join("|", kv.Value.Slots.Select(s => s ?? "-"))}] queued=[{string.Join("|", kv.Value.Queued)}] pity={kv.Value.Pity:F2}"));
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
        NormalizeSlots(st);
        return st;
    }

    /// <summary>
    /// Normalize <see cref="PlayerSatchel.Slots"/> to a fixed-length array (index = slot position,
    /// null = empty). Legacy saves stored a compact list (old capacity was fixed 2 and cards shifted);
    /// those entries are kept left-aligned and missing slots are padded with null.
    /// </summary>
    public static void NormalizeSlots(PlayerSatchel st)
    {
        if (st == null) return;
        if (st.Capacity < 1) st.Capacity = DefaultCapacity;
        st.Capacity = System.Math.Clamp(st.Capacity, 1, MaxCapacity);
        // Test hook: TAROT_SATCHEL_SLOTS=<n> overrides capacity to preview the layout (1..MaxCapacity).
        try
        {
            var overrideValue = System.Environment.GetEnvironmentVariable("TAROT_SATCHEL_SLOTS");
            if (int.TryParse(overrideValue, out int slots) && slots >= 1)
                st.Capacity = System.Math.Clamp(slots, 1, MaxCapacity);
        }
        catch { }
        st.Slots ??= new List<string>();
        if (st.Slots.Count > st.Capacity)
            st.Slots.RemoveRange(st.Capacity, st.Slots.Count - st.Capacity);
        while (st.Slots.Count < st.Capacity) st.Slots.Add(null);
    }

    /// <summary>Leftmost empty slot index, or -1 when the satchel is full.</summary>
    public static int FirstEmptySlot(PlayerSatchel st)
    {
        NormalizeSlots(st);
        for (int i = 0; i < st.Slots.Count; i++)
            if (string.IsNullOrEmpty(st.Slots[i])) return i;
        return -1;
    }

    /// <summary>Number of stored (non-empty) slots.</summary>
    public static int UsedSlots(PlayerSatchel st)
    {
        if (st?.Slots == null) return 0;
        return st.Slots.Count(s => !string.IsNullOrEmpty(s));
    }

    private static string FormatSlots(PlayerSatchel st) =>
        string.Join("|", st.Slots.Select(s => string.IsNullOrEmpty(s) ? "-" : s));

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
                $"{kv.Key}: cap={kv.Value.Capacity} slots=[{FormatSlots(kv.Value)}] queued=[{string.Join("|", kv.Value.Queued)}] pity={kv.Value.Pity:F2}"));
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
                $"{kv.Key}: cap={kv.Value.Capacity} slots=[{FormatSlots(kv.Value)}] queued=[{string.Join("|", kv.Value.Queued)}] pity={kv.Value.Pity:F2}"));
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
