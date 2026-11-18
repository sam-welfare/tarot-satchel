using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Debug;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace TarotSatchel;

/// <summary>
/// Debug-only driver used to verify the mod end-to-end without the player
/// (enabled with the TAROT_SATCHEL_TEST=1 environment variable).
/// It walks the main menu (abandoning any existing run), starts a run, wins combats with the
/// dev console, picks the offered tarot and exercises one use per run.
/// </summary>
public static class TestDriver
{
    private static Godot.Timer _timer;
    private static bool _clickedStandard;
    private static bool _clickedCharacter;
    private static bool _clickedConfirm;
    private static CombatRoom _killedRoom;
    private static bool _claimedThisRoom;
    private static bool _pressedProceed;
    private static bool _usedFromBelt;
    private static int _deckProbeStage;
    private static int _menuTicks;
    private static int _tickCount;
    private static int _fightTicks;
    private static int _eventTicks;
    private static int _mapTicks;
    private static bool _potionSent;
    private static bool _potionClicked;
    private static bool _popupShown;
    private static bool _previewFightSent;
    private static bool _relicsApplied;
    private static int _saveTestPhase;
    private static bool _saveFightSent;
    private static bool _saveDamaged;
    private static bool _savePauseClicked;
    private static bool _saveQuitClicked;

    private static void CollectPauseButtons(Node node, List<MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu.NPauseMenuButton> into)
    {
        if (node is MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu.NPauseMenuButton b) into.Add(b);
        foreach (var child in node.GetChildren()) CollectPauseButtons(child, into);
    }

    /// <summary>Click a Control with synthesized real mouse events (works with NClickableControl).</summary>
    private static void ClickControlWithMouse(Control control)
    {
        try
        {
            var rect = control.GetGlobalRect();
            var pos = rect.Position + rect.Size * 0.5f;
            Input.WarpMouse(pos);
            var motion = new InputEventMouseMotion { Position = pos, GlobalPosition = pos };
            Input.ParseInputEvent(motion);
            Input.ParseInputEvent(new InputEventMouseButton
            {
                Position = pos, GlobalPosition = pos,
                ButtonIndex = MouseButton.Left, ButtonMask = MouseButtonMask.Left, Pressed = true,
            });
            Input.ParseInputEvent(new InputEventMouseButton
            {
                Position = pos, GlobalPosition = pos,
                ButtonIndex = MouseButton.Left, ButtonMask = MouseButtonMask.Left, Pressed = false,
            });
        }
        catch (Exception e)
        {
            ModEntry.Log.Warn($"[test] mouse click failed: {e.Message}", 1);
        }
    }

    private static bool IsTravelable(MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapPoint point)
    {
        try
        {
            var prop = typeof(MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapPoint).GetProperty("IsTravelable",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            return prop?.GetValue(point) is bool b && b;
        }
        catch
        {
            return false;
        }
    }

    private static void CollectMapPoints(Node node, List<MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapPoint> into)    {
        if (node == null) return;
        if (node is MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapPoint p) into.Add(p);
        foreach (var child in node.GetChildren()) CollectMapPoints(child, into);
    }
    private static bool _abandonClicked;

    public static bool Enabled => System.Environment.GetEnvironmentVariable("TAROT_SATCHEL_TEST") == "1";

    public static void Start()
    {
        if (!Enabled || _timer != null) return;
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree?.Root == null) return;
        _timer = new Godot.Timer { Name = "TarotSatchelTestDriver", WaitTime = 1.5f, Autostart = true, OneShot = false };
        tree.Root.AddChild(_timer);
        _timer.Timeout += Tick;
        ModEntry.Log.Info("[test] driver started", 1);
    }

    private static void Log(string msg) => ModEntry.Log.Info("[test] " + msg, 1);

    private static void Tick()
    {
        try
        {
            _tickCount++;
            if (_tickCount == 1) Log("first tick");
            if (_tickCount % 20 == 0)
            {
                var rs = ModEntry.Model?.CurrentRun;
                Log($"heartbeat tick={_tickCount} run={(rs != null)} room={rs?.CurrentRoom?.RoomType} fightTicks={_fightTicks}");
            }
            var runState = ModEntry.Model?.CurrentRun;
            if (runState == null) { DriveMenu(); return; }
            DriveRun(runState);
        }
        catch (Exception e)
        {
            ModEntry.Log.Warn($"[test] tick error: {e.Message}", 1);
        }
    }

    private static bool IsShown(Node node)
    {
        if (node == null || !node.IsInsideTree()) return false;
        if (node is Control c) return c.IsVisibleInTree();
        return true;
    }

    private static bool HasVisibleClickable(Node node)
    {
        if (node == null || !node.IsInsideTree()) return false;
        foreach (var c in AllClickables(node))
            if (c.IsVisibleInTree()) return true;
        return false;
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

    private static List<Control> AllClickables(Node node)
    {
        var list = new List<Control>();
        Walk(node, list);
        return list;
    }

    private static void Walk(Node node, List<Control> into)
    {
        if (node is Control c && (c is BaseButton || c is MegaCrit.Sts2.Core.Nodes.GodotExtensions.NClickableControl))
            into.Add(c);
        foreach (var child in node.GetChildren()) Walk(child, into);
    }

    private static bool TryClick(Control control)
    {
        try
        {
            if (control is BaseButton bb) bb.EmitSignal(BaseButton.SignalName.Pressed);
            else control.EmitSignal("Released", control);
            return true;
        }
        catch (Exception e)
        {
            ModEntry.Log.Warn($"[test] click {control.Name} failed: {e.Message}", 1);
            return false;
        }
    }

    private static Control FindClickableByName(Node root, params string[] names)
    {
        foreach (var c in AllClickables(root))
        {
            var n = c.Name.ToString();
            foreach (var want in names)
                if (n.Equals(want, StringComparison.OrdinalIgnoreCase)) return c;
        }
        foreach (var c in AllClickables(root))
        {
            var n = c.Name.ToString();
            foreach (var want in names)
                if (n.Contains(want, StringComparison.OrdinalIgnoreCase)) return c;
        }
        return null;
    }

    private static void DriveMenu()
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        var root = tree?.Root;
        if (root == null) return;

        var clickables = AllClickables(root);
        if (clickables.Count == 0) return;
        _menuTicks++;
        if (_menuTicks % 10 == 1)
        {
            foreach (var c in clickables.Where(c => c.Visible).Take(10))
                ModEntry.Log.Info($"[test] menu control: {c.Name} [{c.GetType().Name}]", 1);
        }

        if (!_clickedStandard)
        {
            // Preferred: resume the existing saved run (the user starts one manually).
            var cont = FindClickableByName(root, "ContinueButton", "Continue");
            if (cont != null && cont.Visible)
            {
                if (TryClick(cont)) { _clickedStandard = true; Log("clicked Continue"); }
                return;
            }

            var single = FindClickableByName(root, "SingleplayerButton", "Singleplayer");
            if (single != null && single.Visible)
            {
                if (TryClick(single)) { _clickedStandard = true; Log("clicked Singleplayer"); }
                return;
            }

            if (!_abandonClicked)
            {
                var abandon = FindClickableByName(root, "AbandonRunButton", "Abandon");
                if (abandon != null && abandon.Visible)
                {
                    TryClick(abandon);
                    _abandonClicked = true;
                    Log("clicked AbandonRun (confirmation pending)");
                }
                return;
            }

            // Confirmation popup: click a confirm-ish button.
            var confirmAbandon = AllClickables(root).FirstOrDefault(c => c.Visible &&
                (c.Name.ToString().Contains("Confirm", StringComparison.OrdinalIgnoreCase)
                 || c.Name.ToString().Contains("Yes", StringComparison.OrdinalIgnoreCase)
                 || c.Name.ToString().Contains("Ok", StringComparison.OrdinalIgnoreCase)
                 || c.Name.ToString().Contains("Accept", StringComparison.OrdinalIgnoreCase)));
            if (confirmAbandon != null)
            {
                TryClick(confirmAbandon);
                Log($"clicked abandon confirm: {confirmAbandon.Name}");
            }
            return;
        }

        if (!_clickedCharacter)
        {
            var container = root.FindChild("CharSelectButtons/ButtonContainer", true, false);
            Control first = null;
            if (container != null)
            {
                foreach (var child in container.GetChildren())
                {
                    if (child is BaseButton || child is MegaCrit.Sts2.Core.Nodes.GodotExtensions.NClickableControl)
                    { first = child as Control; break; }
                }
            }
            if (first != null && TryClick(first))
            {
                _clickedCharacter = true;
                Log($"clicked first character control {first.Name}");
            }
            return;
        }

        if (!_clickedConfirm)
        {
            var confirm = root.FindChild("ConfirmButton", true, false) as Control;
            if (confirm != null && TryClick(confirm))
            {
                _clickedConfirm = true;
                Log($"clicked ConfirmButton {confirm.GetType().Name}");
            }
        }
    }

    private static void DriveRun(RunState runState)
    {
        if (runState.Players.Count == 0) return;

        try
        {
            var relicList = System.Environment.GetEnvironmentVariable("TAROT_SATCHEL_RELICS");
            if (!string.IsNullOrEmpty(relicList) && !_relicsApplied)
            {
                _relicsApplied = true;
                foreach (var id in relicList.Split(',', ';'))
                {
                    var trimmed = id.Trim();
                    if (trimmed.Length == 0) continue;
                    RunConsoleCommand($"relic add {trimmed}");
                    Log($"relic add {trimmed}");
                }
            }
        }
        catch (Exception e)
        {
            ModEntry.Log.Warn($"[test] relic setup failed: {e.Message}", 1);
        }

        // Passive mode: only set the run up (menu / relics), then leave gameplay to the player.
        if (System.Environment.GetEnvironmentVariable("TAROT_SATCHEL_PASSIVE") == "1") return;

        // Save-point experiment: enter a fight, damage the player via console, then save & quit via
        // synthesized mouse clicks, so the written save can be compared with the current HP.
        try
        {
            if (System.Environment.GetEnvironmentVariable("TAROT_SATCHEL_SAVE_TEST") == "1")
            {
                bool live = CombatProbe.IsInLiveCombat(runState);
                if (!_saveFightSent)
                {
                    _saveFightSent = true;
                    RunConsoleCommand("fight TOADPOLES_WEAK");
                    Log("save-test: console 'fight TOADPOLES_WEAK' sent");
                }
                else if (live && !_saveDamaged)
                {
                    int hp = -1;
                    try { hp = runState.Players[0].Creature?.CurrentHp ?? -1; } catch { }
                    RunConsoleCommand("damage 10 0");
                    _saveDamaged = true;
                    Log($"save-test: console 'damage 10 0' sent, hp before = {hp}");
                }
                else if (_saveDamaged && !_savePauseClicked)
                {
                    _savePauseClicked = true;
                    var treeS = Engine.GetMainLoop() as SceneTree;
                    var pauseButton = FindFirst<MegaCrit.Sts2.Core.Nodes.TopBar.NTopBarPauseButton>(treeS?.Root);
                    if (pauseButton != null) ClickControlWithMouse(pauseButton);
                    Log($"save-test: clicked pause button ({(pauseButton != null)})");
                }
                else if (_savePauseClicked && !_saveQuitClicked)
                {
                    var treeS = Engine.GetMainLoop() as SceneTree;
                    var pauseMenu = FindFirst<MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu.NPauseMenu>(treeS?.Root);
                    var buttons = new List<MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu.NPauseMenuButton>();
                    if (pauseMenu != null) CollectPauseButtons(pauseMenu, buttons);
                    if (buttons.Count > 0)
                    {
                        _saveQuitClicked = true;
                        var target = buttons[buttons.Count - 1];
                        ClickControlWithMouse(target);
                        Log($"save-test: clicked last pause button ({target.Name})");
                    }
                }
            }
        }
        catch (Exception e)
        {
            ModEntry.Log.Warn($"[test] save test failed: {e.Message}", 1);
        }

        try
        {
            bool previewTest = System.Environment.GetEnvironmentVariable("TAROT_SATCHEL_PREVIEW_TEST") == "1";
            if (previewTest && runState.Players.Count > 0)
            {
                var st = SatchelStore.ForPlayer(runState.Players[0]);
                if (SatchelStore.UsedSlots(st) > 0 && st.Queued.Count == 0)
                {
                    bool used = SatchelBelt.TestUseFirstSlot();
                    Log($"preview test: used first slot -> {used}, queued=[{string.Join("|", st.Queued)}]");
                }
            }
        }
        catch { }

        try
        {
            if (System.Environment.GetEnvironmentVariable("TAROT_SATCHEL_POPUP_TEST") == "1" && !_popupShown)
            {
                SatchelBelt.ShowTestPopup();
                _popupShown = true;
            }
        }
        catch { }

        try
        {
            var potionId = System.Environment.GetEnvironmentVariable("TAROT_SATCHEL_POTION");
            if (!string.IsNullOrEmpty(potionId))
            {
                if (!_potionSent)
                {
                    RunConsoleCommand($"potion {potionId}");
                    _potionSent = true;
                    Log($"console potion {potionId} requested");
                }
                else if (!_potionClicked)
                {
                    var treeP = Engine.GetMainLoop() as SceneTree;
                    var container = FindFirst<MegaCrit.Sts2.Core.Nodes.Potions.NPotionContainer>(treeP?.Root);
                    var holders = container?.Holders;
                    if (holders != null && holders.Count > 0)
                    {
                        // Real mouse events: NClickableControl needs press+release state to open the popup.
                        var holder = holders[0];
                        var rect = holder.GetGlobalRect();
                        var pos = rect.Position + rect.Size * 0.5f;
                        try
                        {
                            Input.WarpMouse(pos);
                            var motion = new InputEventMouseMotion { Position = pos, GlobalPosition = pos };
                            Input.ParseInputEvent(motion);
                            var press = new InputEventMouseButton
                            {
                                Position = pos, GlobalPosition = pos,
                                ButtonIndex = MouseButton.Left, ButtonMask = MouseButtonMask.Left, Pressed = true,
                            };
                            Input.ParseInputEvent(press);
                            var release = new InputEventMouseButton
                            {
                                Position = pos, GlobalPosition = pos,
                                ButtonIndex = MouseButton.Left, ButtonMask = MouseButtonMask.Left, Pressed = false,
                            };
                            Input.ParseInputEvent(release);
                        }
                        catch (Exception e) { ModEntry.Log.Warn($"[test] mouse synth failed: {e.Message}", 1); }
                        _potionClicked = true;
                        Log($"clicked potion holder at {pos} to open popup");
                    }
                }
            }
        }
        catch (Exception e)
        {
            ModEntry.Log.Warn($"[test] potion probe failed: {e.Message}", 1);
        }

        // If the pause menu opened (e.g. from a stray Escape), click the resume button.
        try
        {
            var treeP = Engine.GetMainLoop() as SceneTree;
            var pauseMenu = FindFirst<MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu.NPauseMenu>(treeP?.Root);
            if (pauseMenu != null)
            {
                foreach (var c in AllClickables(pauseMenu))
                {
                    var n = c.Name.ToString();
                    if (n.Contains("Resume", StringComparison.OrdinalIgnoreCase)
                        || n.Contains("Continue", StringComparison.OrdinalIgnoreCase))
                    {
                        TryClick(c);
                        Log($"resumed from pause menu via {n}");
                        break;
                    }
                }
                return;
            }
        }
        catch { }

        // Official card-reward selection screen (tarot reward uses the vanilla CardReward UI).
        try
        {
            var treeCs = Engine.GetMainLoop() as SceneTree;
            var cardScreen = FindFirst<MegaCrit.Sts2.Core.Nodes.Screens.CardSelection.NCardRewardSelectionScreen>(treeCs?.Root);
            if (cardScreen != null)
            {
                var holder = FindFirst<MegaCrit.Sts2.Core.Nodes.Cards.Holders.NCardHolder>(cardScreen);
                if (holder != null)
                {
                    try
                    {
                        var mi = holder.GetType().GetMethod("EmitSignalPressed",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        mi?.Invoke(holder, new object[] { holder });
                    }
                    catch
                    {
                        holder.EmitSignal("Pressed", holder);
                    }
                    Log("selected card on official reward screen");
                }
                return;
            }
        }
        catch (Exception e)
        {
            ModEntry.Log.Warn($"[test] official card screen handling failed: {e.Message}", 1);
        }

        try
        {
            var tree0 = Engine.GetMainLoop() as SceneTree;
            var offerLayer = tree0?.Root?.FindChild("TarotOfferLayer", true, false);
            if (offerLayer != null)
            {
                var pick = offerLayer.FindChild("TarotOfferCard0", true, false) as BaseButton;
                if (pick != null)
                {
                    pick.EmitSignal(BaseButton.SignalName.Pressed);
                    Log("picked offered tarot card");
                }
                else
                {
                    var skip = offerLayer.FindChild("Skip", true, false) as BaseButton;
                    if (skip != null) { skip.EmitSignal(BaseButton.SignalName.Pressed); Log("skipped offered tarot card"); }
                }
                return;
            }
        }
        catch { }

        if (System.Environment.GetEnvironmentVariable("TAROT_SATCHEL_DECKPROBE") == "1" && _deckProbeStage == 0 && !CombatProbe.IsInLiveCombat(runState))
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            var topBar = FindFirst<NTopBar>(tree?.Root);
            Node deckButton = null;
            try { deckButton = topBar?.Deck as Node; } catch { }
            if (deckButton != null)
            {
                try { deckButton.EmitSignal("Released", deckButton); } catch { }
                _deckProbeStage = 1;
                Log("opened deck view (hook-iteration NRE probe)");
            }
        }
        else if (_deckProbeStage == 1)
        {
            try
            {
                var esc = new InputEventKey { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = true };
                Input.ParseInputEvent(esc);
                var escUp = new InputEventKey { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = false };
                Input.ParseInputEvent(escUp);
            }
            catch { }
            _deckProbeStage = 2;
            Log("closed deck view probe");
        }

        // Keep the run moving: if nothing is happening (no live combat, no reward screen), force one.
        try
        {
            var treeF = Engine.GetMainLoop() as SceneTree;
            bool liveCombat = CombatProbe.IsInLiveCombat(runState);
            var rewardScreen = FindFirst<NRewardsScreen>(treeF?.Root);
            var cardScreenNode = FindFirst<MegaCrit.Sts2.Core.Nodes.Screens.CardSelection.NCardRewardSelectionScreen>(treeF?.Root);
            bool rewardUi = HasVisibleClickable(rewardScreen) || HasVisibleClickable(cardScreenNode);
            bool previewTest = System.Environment.GetEnvironmentVariable("TAROT_SATCHEL_PREVIEW_TEST") == "1";
            if (!liveCombat && (previewTest || !rewardUi))
            {
                if (previewTest)
                {
                    // One shot: let the fight actually load and stay alive for the animation capture.
                    if (!_previewFightSent)
                    {
                        _previewFightSent = true;
                        RunConsoleCommand("fight TOADPOLES_WEAK");
                        Log("preview test: single forced fight");
                    }
                }
                else
                {
                    _fightTicks++;
                    if (_fightTicks % 5 == 2)
                    {
                        RunConsoleCommand("fight TOADPOLES_WEAK");
                        Log("forced a fight via console");
                    }
                }
            }
        }
        catch { }

        // Advance the map by clicking the first travelable map node (official NMapPoint.IsTravelable).
        if (System.Environment.GetEnvironmentVariable("TAROT_SATCHEL_PREVIEW_TEST") != "1")
        try
        {
            var treeM = Engine.GetMainLoop() as SceneTree;
            var points = new List<MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapPoint>();
            CollectMapPoints(treeM?.Root, points);
            var target = points.FirstOrDefault(p => IsTravelable(p) && p.IsVisibleInTree());
            if (target != null)
            {
                _mapTicks++;
                if (_mapTicks % 4 == 2)
                {
                    TryClick(target);
                    Log("clicked travelable map point");
                }
            }
        }
        catch { }

        // Advance event screens by pressing an event option.
        if (!CombatProbe.IsInLiveCombat(runState))
        {
            try
            {
                var treeE = Engine.GetMainLoop() as SceneTree;
                var option = FindFirst<MegaCrit.Sts2.Core.Nodes.Events.NEventOptionButton>(treeE?.Root);
                if (option != null)
                {
                    _eventTicks++;
                    if (_eventTicks % 4 == 2)
                    {
                        TryClick(option);
                        Log($"pressed event option {option.Name}");
                    }
                    return;
                }
            }
            catch { }
        }

        if (runState.CurrentRoom is CombatRoom combatRoom)
        {
            if (!ReferenceEquals(combatRoom, _killedRoom))
            {
                _killedRoom = combatRoom;
                _claimedThisRoom = false;
                _pressedProceed = false;
                if (System.Environment.GetEnvironmentVariable("TAROT_SATCHEL_PREVIEW_TEST") != "1")
                {
                    RunConsoleCommand("kill all");
                    Log($"sent 'kill all' in {combatRoom.RoomType}");
                }
                else
                {
                    Log("preview test: combat left alive so the animation can be inspected");
                }
            }

            var tree = Engine.GetMainLoop() as SceneTree;
            var screen = FindFirst<NRewardsScreen>(tree?.Root);
            if (screen == null) return;

            // Persistence test: leave the reward unclaimed, then save & quit from the pause menu.
            if (System.Environment.GetEnvironmentVariable("TAROT_SATCHEL_SKIP_CLAIM") == "1")
            {
                var pauseMenu = FindFirst<MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu.NPauseMenu>(tree?.Root);
                if (pauseMenu == null)
                {
                    var pauseButton = FindFirst<MegaCrit.Sts2.Core.Nodes.TopBar.NTopBarPauseButton>(tree?.Root);
                    if (pauseButton != null) TryClick(pauseButton);
                }
                else
                {
                    var buttons = AllClickables(pauseMenu);
                    if (buttons.Count > 0)
                    {
                        TryClick(buttons[buttons.Count - 1]);
                        Log("pressed last pause menu button (save & quit)");
                    }
                }
                return;
            }

            if (!_claimedThisRoom)
            {
                var controls = AllClickables(screen);
                foreach (var c in controls)
                {
                    TryClick(c);
                }
                _claimedThisRoom = true;
                var st = SatchelStore.ForPlayer(runState.Players[0]);
                Log($"pressed {controls.Count} reward controls; belt=[{string.Join("|", st.Slots.Select(s => s ?? "-"))}]");
            }
            else if (!_pressedProceed)
            {
                var proceed = FindFirst<NProceedButton>(screen);
                if (proceed != null)
                {
                    try { proceed.EmitSignal(BaseButton.SignalName.Pressed); } catch { }
                    _pressedProceed = true;
                    Log("pressed proceed button");
                }
            }
        }
    }

    public static void TryAutoUse()
    {
        if (!Enabled || _usedFromBelt) return;
        var runState = ModEntry.Model?.CurrentRun;
        if (runState == null || runState.Players.Count == 0) return;
        var st = SatchelStore.ForPlayer(runState.Players[0]);
        if (SatchelStore.UsedSlots(st) == 0 || st.Queued.Count > 0) return;
        bool used = SatchelBelt.TestUseFirstSlot();
        _usedFromBelt = true;
        Log($"auto use -> {used}, queued=[{string.Join("|", st.Queued)}]");
    }

    private static void CollectButtons(Node node, List<BaseButton> into)
    {
        if (node is BaseButton b) into.Add(b);
        foreach (var child in node.GetChildren()) CollectButtons(child, into);
    }

    private static void RunConsoleCommand(string command)
    {
        try
        {
            var console = NDevConsole.InstanceOrNull;
            if (console == null) { Log("no dev console instance"); return; }
            var t = console.GetType();
            var inputField = t.GetField("_inputBuffer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var input = inputField?.GetValue(console) as LineEdit;
            if (input == null) { Log("no console input buffer"); return; }
            input.Text = command;
            var proc = t.GetMethod("ProcessCommand", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            proc?.Invoke(console, null);
            Log($"console command sent: {command}");
        }
        catch (Exception e)
        {
            Log($"console command failed: {e.Message}");
        }
    }
}




