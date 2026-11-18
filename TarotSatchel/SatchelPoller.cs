using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace TarotSatchel;

/// <summary>
/// Polls run state every second: live-combat entry/exit bookkeeping, queued-tarot injection into
/// the draw pile, belt refresh, reward-row label fix, and debug helpers (probe / screenshot).
/// </summary>
public static class SatchelPoller
{
    private static Godot.Timer _timer;
    private static bool _wasInCombat;
    private static CombatRoom _loggedCombatRoom;
    private static int _shots;
    private static int _ticks;
    private static MegaCrit.Sts2.Core.Rooms.AbstractRoom _injectedCombatRoom;
    private static int _injectTicks;

    private static bool SafeCombatUiReady()
    {
        try { return MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance != null; }
        catch { return false; }
    }

    private static T FindFirst<T>(Node node) where T : Node
    {
        if (node == null) return null;
        if (node is T match) return match;
        foreach (var child in node.GetChildren())
        {
            var found = FindFirst<T>(child);
            if (found != null) return found;
        }
        return null;
    }

    private static IEnumerable<Node> Descendants(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            yield return child;
            foreach (var sub in Descendants(child)) yield return sub;
        }
    }

    /// <summary>Rename the tarot reward row to "Tarot · Card Name" (official label is generic).</summary>
    private static void FixRewardRowLabels()
    {
        try
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            var screen = FindFirst<MegaCrit.Sts2.Core.Nodes.Screens.NRewardsScreen>(tree?.Root);
            if (screen == null) return;
            foreach (var btn in Descendants(screen).OfType<MegaCrit.Sts2.Core.Nodes.Rewards.NRewardButton>())
            {
                string desired = null;
                var reward = btn.Reward;
                if (reward is TarotCardReward tcr) desired = "塔罗牌·" + TarotNames.GetDisplayName(tcr.Tarot);
                if (desired == null) continue;
                var label = FindFirst<MegaCrit.Sts2.addons.mega_text.MegaRichTextLabel>(btn);
                if (label != null && label.Text != desired) label.Text = desired;
            }
        }
        catch (Exception e)
        {
            ModEntry.Log.Warn($"[ui] reward label fix failed: {e.Message}", 1);
        }
    }

    /// <summary>Debug: capture a burst of frames so a short animation can be inspected.</summary>
    public static void BurstShots(string basePath, int frames = 10, float step = 0.15f)
    {
        try
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree?.Root == null || string.IsNullOrEmpty(basePath)) return;
            for (int i = 0; i < frames; i++)
            {
                int index = i;
                var timer = tree.CreateTimer(step * (i + 1));
                if (timer == null) continue;
                timer.Timeout += () =>
                {
                    try
                    {
                        var image = tree.Root.GetTexture()?.GetImage();
                        image?.SavePng($"{basePath}_{index}.png");
                    }
                    catch { }
                };
            }
            ModEntry.Log.Info($"[shot] burst of {frames} frames scheduled to {basePath}_*.png", 1);
        }
        catch (Exception e)
        {
            ModEntry.Log.Warn($"[shot] burst failed: {e.Message}", 1);
        }
    }

    private static void MaybeScreenshot()
    {
        _ticks++;
        var path = System.Environment.GetEnvironmentVariable("TAROT_SATCHEL_SHOT");
        if (string.IsNullOrEmpty(path)) return;
        if (_ticks % 10 != 0) return;
        try
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            var image = tree?.Root?.GetTexture()?.GetImage();
            if (image != null)
            {
                _shots++;
                image.SavePng(path);
                ModEntry.Log.Info($"[shot] saved {path} #{_shots}", 1);
            }
        }
        catch (Exception e)
        {
            ModEntry.Log.Warn($"[shot] failed: {e.Message}", 1);
        }
    }

    public static void Start()
    {
        if (_timer != null) return;
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree?.Root == null) return;
        _timer = new Godot.Timer { Name = "TarotSatchelPoller", WaitTime = 1.0f, Autostart = true, OneShot = false };
        tree.Root.AddChild(_timer);
        _timer.Timeout += Tick;
    }

    private static void Tick()
    {
        try
        {
            var runState = ModEntry.Model?.CurrentRun;
            if (runState == null)
            {
                _wasInCombat = false;
                return;
            }

            bool live = CombatProbe.IsInLiveCombat(runState);
            if (live)
            {
                var combatRoom = runState.CurrentRoom as CombatRoom;
                ModEntry.Model.MarkCombatSeen();
                if (combatRoom != null)
                {
                    if (!ReferenceEquals(runState.CurrentRoom, _injectedCombatRoom))
                    {
                        _injectedCombatRoom = runState.CurrentRoom;
                        _injectTicks = 0;
                    }
                    _injectTicks++;
                    // Wait until the combat UI exists (animations need its card-pile nodes) and give
                    // the room a beat to finish entering before injecting the queued tarot.
                    bool uiReady = SafeCombatUiReady();
                    if (uiReady && _injectTicks >= 2)
                        ModEntry.Model.InjectQueuedIntoCombat(combatRoom);
                    else if (_injectTicks <= 3)
                        ModEntry.Log.Info($"[combat] injection waiting: uiReady={uiReady} tick={_injectTicks}", 1);
                }

                if (!ReferenceEquals(runState.CurrentRoom, _loggedCombatRoom))
                {
                    _loggedCombatRoom = combatRoom;
                    int poolCount = -1;
                    try { poolCount = TarotCards.All?.Count ?? -1; } catch { }
                    foreach (var player in runState.Players)
                    {
                        var st = SatchelStore.ForPlayer(player);
                        int queuedInPile = -1;
                        try
                        {
                            var pile = CardPile.Get(PileType.Draw, player);
                            queuedInPile = pile.Cards.Count(c => st.Queued.Contains(c.Id.ToString()));
                        }
                        catch { }
                        ModEntry.Log.Info($"[combat] entered {_loggedCombatRoom?.RoomType}: tarotPool={poolCount} slots=[{string.Join("|", st.Slots.Select(s => s ?? "-"))}] queued=[{string.Join("|", st.Queued)}] queuedInDrawPile={queuedInPile} pity={st.Pity:F2}", 1);
                    }
                }
            }
            else if (_wasInCombat)
            {
                ModEntry.Log.Info("[combat] finished", 1);
                ModEntry.Model.RemoveQueuedAfterCombat();
                _loggedCombatRoom = null;
                if (TestDriver.Enabled) TestDriver.TryAutoUse();
            }

            _wasInCombat = live;
            SatchelBelt.RefreshAll();
            if (System.Environment.GetEnvironmentVariable("TAROT_SATCHEL_PROBE") == "1") TopBarProbe.DumpOnce();
            if (System.Environment.GetEnvironmentVariable("TAROT_SATCHEL_PROBE_POPUP") == "1") TopBarProbe.DumpPotionPopupOnce();
            if (System.Environment.GetEnvironmentVariable("TAROT_SATCHEL_BLUEPRINT") == "1") TopBarProbe.DumpPotionPopupBlueprint();
            MaybeScreenshot();
            FixRewardRowLabels();
        }
        catch (Exception e)
        {
            ModEntry.Log.Warn($"[poll] tick failed: {e.Message}", 1);
        }
    }
}
