using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace TarotSatchel;

/// <summary>
/// Tarot belt. The chrome is a script-stripped duplicate of the vanilla potion belt (pixel-identical
/// panel/rounding/spacing), mirrored across the floor icon. Slots use the official card icon
/// (grey when empty, full colour when filled).
/// </summary>
public static class SatchelBelt
{
    private const string CardIconPath = "res://images/ui/reward_screen/reward_icon_card.png";

    private static Control _root;
    private static Control _panel;
    private static Node _parentNode;
    private static NPotionContainer _anchorPotion;
    private static NinePatchRect _bgSource;
    private static NinePatchRect _errorBgSource;
    private static NinePatchRect _errorBg;
    private static Control _floorAnchor;
    private static int _capacity = SatchelStore.DefaultCapacity;
    private static List<Vector2> _lastGoodOffsets;
    private static Vector2 _slotSize = new(60, 60);
    private static Vector2 _fixedPanelSize = new(159, 71);
    private static Vector2 _fixedPanelPos = new(0, 4);
    private static readonly List<Control> _slots = new();
    private static readonly List<TextureRect> _slotIcons = new();
    private static Texture2D _cardIcon;
    private static double _lastSearchLog;
    private static string _lastRenderState;

    public static void RefreshAll()
    {
        try
        {
            EnsureAttached();
            Rebuild();
        }
        catch (Exception e)
        {
            ModEntry.Log.Warn($"[ui] refresh failed: {e.Message}", 1);
        }
    }

    /// <summary>
    /// Refresh after a short delay so Godot's container layout/animation has settled. Used when the
    /// official potion bar changes (add/remove/discard) instead of waiting for the 1s poll tick.
    /// </summary>
    public static void RefreshSoon()
    {
        try
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree == null)
            {
                RefreshAll();
                return;
            }
            var timer = tree.CreateTimer(0.05);
            timer.Timeout += () => { try { RefreshAll(); } catch { } };
        }
        catch
        {
            try { RefreshAll(); } catch { }
        }
    }

    /// <summary>Debug: show the replicated potion popup so its visuals can be screenshot-verified.</summary>
    public static void ShowTestPopup()
    {
        var anchor = SlotAnchor(0);
        if (anchor == null) { ModEntry.Log.Info("[ui] popup test: no slot anchor", 1); return; }
        _ = TarotSlotPopup.ShowAsync(anchor, "塔罗牌·测试", false);
        ModEntry.Log.Info("[ui] popup test: shown", 1);
    }

    /// <summary>Anchor control for official hover-tip previews (e.g. the combat-start card preview).</summary>
    public static Control SlotAnchor(int index = 0)
    {
        try
        {
            if (index >= 0 && index < _slots.Count && GodotObject.IsInstanceValid(_slots[index])) return _slots[index];
        }
        catch { }
        return null;
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

    private static void DestroyExisting(SceneTree tree)
    {
        try { if (_root != null && GodotObject.IsInstanceValid(_root)) _root.QueueFree(); } catch { }
        _root = null;
        _panel = null;
        _slots.Clear();
        _slotIcons.Clear();
        _lastGoodOffsets = null;
        if (tree?.Root == null) return;
        foreach (var stale in tree.Root.FindChildren("TarotSatchelBelt", "", true, false))
        {
            try { stale.QueueFree(); } catch { }
        }
    }

    private static void EnsureAttached()
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        var sceneRoot = tree?.Root;
        if (sceneRoot == null) return;

        if (_root != null && GodotObject.IsInstanceValid(_root) && _root.IsInsideTree()
            && _anchorPotion != null && GodotObject.IsInstanceValid(_anchorPotion))
            return;

        DestroyExisting(tree);

        var topBar = FindFirst<NTopBar>(sceneRoot);
        if (topBar == null) { LogSearchOnce("no NTopBar in scene tree"); return; }

        NPotionContainer potions = null;
        try { potions = topBar.PotionContainer; } catch { }
        potions ??= FindFirst<NPotionContainer>(topBar);
        if (potions == null) { LogSearchOnce("no potion container"); return; }

        Control floorIcon = null;
        try { floorIcon = topBar.FloorIcon as Control; } catch { }

        // Capacity is dynamic: future relics can raise it, so layout is computed from it.
        _capacity = CurrentCapacity();

        // Official slot geometry.
        Vector2 slotSize = new(60, 60);
        List<Vector2> offsets = new();
        try
        {
            var holders = potions.Holders;
            if (holders != null && holders.Count > 0)
            {
                slotSize = holders[0].Size;
                foreach (var h in holders.Take(_capacity)) offsets.Add(h.Position);
            }
        }
        catch { }
        if (offsets.Count == 0)
        {
            float spacing = slotSize.X + 6;
            for (int i = 0; i < _capacity; i++) offsets.Add(new Vector2(6 + i * spacing, 6));
        }

        _cardIcon ??= LoadCardIcon();
        _anchorPotion = potions;
        // Follow official potion-bar resizes immediately instead of waiting for the 1s poll tick,
        // otherwise the belt visibly lags behind when potions are added/removed.
        try { potions.Resized += OnPotionContainerResized; } catch { }
        _floorAnchor = floorIcon;
        _slotSize = slotSize;

        _bgSource = null;
        _errorBgSource = null;
        foreach (var n in Descendants(potions))
        {
            if (n is NinePatchRect np && n.Name.ToString().StartsWith("PotionBg", StringComparison.OrdinalIgnoreCase))
                _bgSource = np;
            else if (n is NinePatchRect err && err.Name.ToString().StartsWith("PotionErrorBg", StringComparison.OrdinalIgnoreCase))
                _errorBgSource = err;
        }

        _root = new Control { Name = "TarotSatchelBelt", MouseFilter = Control.MouseFilterEnum.Pass };
        var parentCandidate = potions.GetParent();
        Node parent = parentCandidate is Container ? topBar : parentCandidate;
        parent.AddChild(_root);
        _parentNode = parent;

        _panel = CloneBackdrop(_bgSource) ?? TryCopyPanelVisuals(potions);
        if (_panel == null)
        {
            var bg = new PanelContainer { Name = "Bg" };
            var style = new StyleBoxFlat
            {
                BgColor = new Color("#1b2430"),
                CornerRadiusTopLeft = 8,
                CornerRadiusTopRight = 8,
                CornerRadiusBottomLeft = 8,
                CornerRadiusBottomRight = 8,
                BorderWidthLeft = 2,
                BorderWidthTop = 2,
                BorderWidthRight = 2,
                BorderWidthBottom = 2,
                BorderColor = new Color("#32435a"),
            };
            bg.AddThemeStyleboxOverride("panel", style);
            _panel = bg;
        }
        _panel.Name = "TarotSatchelPanel";
        _panel.MouseFilter = Control.MouseFilterEnum.Pass;
        _panel.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        _panel.Size = potions.Size;
        _root.AddChild(_panel);

        _errorBg = CloneBackdrop(_errorBgSource) as NinePatchRect;
        if (_errorBg != null)
        {
            _errorBg.Name = "TarotErrorBg";
            _errorBg.Modulate = new Color(1, 1, 1, 0);
            _errorBg.MouseFilter = Control.MouseFilterEnum.Ignore;
            _root.AddChild(_errorBg);
        }

        for (int i = 0; i < _capacity; i++)
        {
            int slotIndex = i;
            var slot = new Control
            {
                Name = $"TarotSlot{i}",
                Size = slotSize,
                CustomMinimumSize = slotSize,
                MouseFilter = Control.MouseFilterEnum.Stop,
            };
            float iconSide = Mathf.Min(52f, Mathf.Min(slotSize.X, slotSize.Y) - 8f);
            var icon = new TextureRect
            {
                Name = "Icon",
                Texture = _cardIcon,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Position = (slotSize - new Vector2(iconSide, iconSide)) * 0.5f,
                Size = new Vector2(iconSide, iconSide),
            };
            slot.AddChild(icon);
            slot.MouseEntered += () => OnSlotHover(slotIndex, true);
            slot.MouseExited += () => OnSlotHover(slotIndex, false);
            slot.GuiInput += evt => OnSlotInput(slotIndex, evt);
            _root.AddChild(slot);
            _slots.Add(slot);
            _slotIcons.Add(icon);
        }

        // Panel shape is fixed by the slot layout (never derived from the potion container size);
        // only the position mirrors the potion belt. This keeps the belt shape stable as potions grow.
        float leftPad = offsets.Count > 0 ? offsets[0].X : 6f;
        float topPad = offsets.Count > 0 ? offsets[0].Y : 6f;
        float gap = offsets.Count > 1 ? Mathf.Max(0f, offsets[1].X - offsets[0].X - slotSize.X) : 2f;
        float fixedWidth = leftPad * 2f + slotSize.X * _capacity + gap * (_capacity - 1);
        float fixedHeight = _bgSource != null && GodotObject.IsInstanceValid(_bgSource) && _bgSource.Size.Y > 0
            ? _bgSource.Size.Y
            : slotSize.Y + topPad * 2f;
        _fixedPanelSize = new Vector2(fixedWidth, fixedHeight);
        _fixedPanelPos = _bgSource != null && GodotObject.IsInstanceValid(_bgSource) ? _bgSource.Position : new Vector2(0, 4);

        UpdateGeometry(offsets);
        _lastRenderState = null;
        ModEntry.Log.Info($"[ui] belt built (official clone): potion={potions.GlobalPosition} size={potions.Size} slot={slotSize} floor={(floorIcon?.GlobalPosition.ToString() ?? "null")} at={_root.Position}", 1);
    }

    private static void OnPotionContainerResized()
    {
        try { RefreshAll(); } catch { }
    }

    private static bool CloseEnough(List<Vector2> a, List<Vector2> b, float eps)
    {
        int n = Mathf.Min(a.Count, b.Count);
        for (int i = 0; i < n; i++)
            if ((a[i] - b[i]).Length() > eps) return false;
        return true;
    }

    private static Control CloneBackdrop(NinePatchRect src)
    {
        if (src == null || !GodotObject.IsInstanceValid(src) || src.Texture == null) return null;
        try
        {
            return new NinePatchRect
            {
                Texture = src.Texture,
                PatchMarginLeft = src.PatchMarginLeft,
                PatchMarginTop = src.PatchMarginTop,
                PatchMarginRight = src.PatchMarginRight,
                PatchMarginBottom = src.PatchMarginBottom,
                AxisStretchHorizontal = src.AxisStretchHorizontal,
                AxisStretchVertical = src.AxisStretchVertical,
                MouseFilter = Control.MouseFilterEnum.Pass,
            };
        }
        catch (Exception e)
        {
            ModEntry.Log.Warn($"[ui] backdrop clone failed: {e.Message}", 1);
            return null;
        }
    }

    private static Control TryCopyPanelVisuals(NPotionContainer potions)
    {
        try
        {
            Control best = null;
            float bestArea = 0;
            foreach (var n in Descendants(potions))
            {
                if (n is NinePatchRect np && np.Texture != null)
                {
                    float a = np.Size.X * np.Size.Y;
                    if (a > bestArea) { best = np; bestArea = a; }
                }
                else if (n is TextureRect tr && tr.Texture != null && tr.GetType() == typeof(TextureRect))
                {
                    float a = tr.Size.X * tr.Size.Y;
                    if (a > bestArea) { best = tr; bestArea = a; }
                }
            }

            if (best is NinePatchRect srcNp)
            {
                return new NinePatchRect
                {
                    Texture = srcNp.Texture,
                    PatchMarginLeft = srcNp.PatchMarginLeft,
                    PatchMarginTop = srcNp.PatchMarginTop,
                    PatchMarginRight = srcNp.PatchMarginRight,
                    PatchMarginBottom = srcNp.PatchMarginBottom,
                    AxisStretchHorizontal = srcNp.AxisStretchHorizontal,
                    AxisStretchVertical = srcNp.AxisStretchVertical,
                    MouseFilter = Control.MouseFilterEnum.Pass,
                };
            }
            if (best is TextureRect srcTr)
            {
                return new TextureRect
                {
                    Texture = srcTr.Texture,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.Scale,
                    MouseFilter = Control.MouseFilterEnum.Pass,
                };
            }

            // Fallback: copy a panel stylebox if present.
            foreach (var n in Descendants(potions))
            {
                if (n is Panel p)
                {
                    var sb = p.GetThemeStylebox("panel") as StyleBox;
                    if (sb != null)
                    {
                        var panel = new Panel { MouseFilter = Control.MouseFilterEnum.Pass };
                        panel.AddThemeStyleboxOverride("panel", (StyleBox)sb.Duplicate());
                        return panel;
                    }
                }
            }
        }
        catch (Exception e)
        {
            ModEntry.Log.Warn($"[ui] panel visual copy failed, using fallback: {e.Message}", 1);
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

    private static Texture2D LoadCardIcon()
    {
        try
        {
            var tex = ResourceLoader.Load<Texture2D>(CardIconPath);
            if (tex != null) return tex;
        }
        catch (Exception e)
        {
            ModEntry.Log.Warn($"[ui] card icon load failed: {e.Message}", 1);
        }
        return null;
    }

    private static void LogSearchOnce(string message)
    {
        double now = Time.GetTicksMsec() / 1000.0;
        if (now - _lastSearchLog < 10) return;
        _lastSearchLog = now;
        ModEntry.Log.Warn($"[ui] {message}", 1);
    }

    private static RunState Run() => ModEntry.Model?.CurrentRun;

    private static void UpdateGeometry(List<Vector2> offsets = null)
    {
        try
        {
            if (_root == null || !GodotObject.IsInstanceValid(_root)) return;
            if (_anchorPotion == null || !GodotObject.IsInstanceValid(_anchorPotion)) return;

            var potionSize = _anchorPotion.Size;
            if (_bgSource != null && GodotObject.IsInstanceValid(_bgSource))
            {
                if (_panel != null && GodotObject.IsInstanceValid(_panel))
                {
                    if (_panel.Position != _fixedPanelPos) _panel.Position = _fixedPanelPos;
                    if (_panel.Size != _fixedPanelSize) _panel.Size = _fixedPanelSize;
                }
                if (_errorBg != null && GodotObject.IsInstanceValid(_errorBg))
                {
                    if (_errorBg.Position != _fixedPanelPos) _errorBg.Position = _fixedPanelPos;
                    if (_errorBg.Size != _fixedPanelSize) _errorBg.Size = _fixedPanelSize;
                }
            }
            else if (_panel != null && GodotObject.IsInstanceValid(_panel) && _panel.Size != potionSize)
            {
                _panel.Size = potionSize;
            }

            List<Vector2> current = offsets;
            if (current == null)
            {
                current = new List<Vector2>();
                try
                {
                    var potionsGlobal = _anchorPotion.GlobalPosition;
                    var holders = _anchorPotion.Holders;
                    if (holders != null)
                        foreach (var h in holders.Take(_capacity))
                            current.Add(h.GlobalPosition - potionsGlobal);
                }
                catch { }
            }
            if (current.Count == 0)
            {
                for (int i = 0; i < _capacity; i++)
                    current.Add(new Vector2(18 + i * 62, 9));
            }
            // Mid-layout the top bar reports zeroed positions; fall back to the official 18/62 layout
            // instead of stacking every slot at the same spot.
            bool badLayout = current.Count == 0;
            for (int i = 1; i < current.Count; i++)
                if (Mathf.Abs(current[i].X - current[0].X) < 1f) badLayout = true;
            if (badLayout)
            {
                // Mid-layout measurements can collapse to zero; reuse the last good offsets instead of
                // jumping to the hardcoded fallback (that fallback churn was the visible belt jitter).
                if (_lastGoodOffsets != null && _lastGoodOffsets.Count >= _capacity)
                    current = new List<Vector2>(_lastGoodOffsets);
                else
                {
                    current.Clear();
                    for (int i = 0; i < _capacity; i++)
                        current.Add(new Vector2(18 + i * 62, 9));
                }
            }
            else
            {
                // Ignore sub-pixel churn from the live potion holders so the belt stops moving when
                // nothing actually changed.
                if (_lastGoodOffsets == null || _lastGoodOffsets.Count != current.Count || !CloseEnough(current, _lastGoodOffsets, 1f))
                    _lastGoodOffsets = new List<Vector2>(current);
                else
                    current = new List<Vector2>(_lastGoodOffsets);
            }

            // Panel shape follows the slot layout only (never the potion container width), and is
            // recomputed from the live holder offsets so an early/mid-layout measurement self-heals.
            float leftPad = current[0].X;
            float topPad = current[0].Y;
            float gap = current.Count > 1 ? Mathf.Max(0f, current[1].X - current[0].X - _slotSize.X) : 2f;
            float fixedWidth = leftPad * 2f + _slotSize.X * _capacity + gap * (_capacity - 1);
            float fixedHeight = _bgSource != null && GodotObject.IsInstanceValid(_bgSource) && _bgSource.Size.Y > 0
                ? _bgSource.Size.Y
                : _slotSize.Y + topPad * 2f;
            _fixedPanelSize = new Vector2(fixedWidth, fixedHeight);
            if (_bgSource != null && GodotObject.IsInstanceValid(_bgSource))
                _fixedPanelPos = _bgSource.Position;

            for (int i = 0; i < _slots.Count; i++)
            {
                Vector2 off = i < current.Count ? current[i] : new Vector2(18 + i * 62, 9);
                // Slots are laid out left-to-right inside the panel: slot 0 is the leftmost one, so
                // "fill the leftmost empty slot" matches what the player sees. The panel itself is
                // still mirrored/anchored to the top bar via _root.Position.
                float x = leftPad + i * (_slotSize.X + gap);
                var slotPos = new Vector2(x, off.Y);
                if (_slots[i].Position != slotPos) _slots[i].Position = slotPos;

                if (_slots[i].Size != _slotSize) _slots[i].Size = _slotSize;
                var icon = _slotIcons[i];
                float iconSide = Mathf.Min(52f, Mathf.Min(_slotSize.X, _slotSize.Y) - 8f);
                var iconPos = (_slotSize - new Vector2(iconSide, iconSide)) * 0.5f;
                if (icon.Size != new Vector2(iconSide, iconSide)) icon.Size = new Vector2(iconSide, iconSide);
                if (icon.Position != iconPos) icon.Position = iconPos;
            }

            var pos = ComputeMirroredPosition(_parentNode, _anchorPotion, potionSize, _floorAnchor);
            // Threshold: skip sub-pixel moves so the belt does not visibly drift every tick.
            if ((_root.Position - pos).Length() >= 1f) _root.Position = pos;
        }
        catch (Exception e)
        {
            ModEntry.Log.Warn($"[ui] geometry update failed: {e.Message}", 1);
        }
    }

    private static Vector2 ComputeMirroredPosition(Node parent, Control potions, Vector2 panelSize, Control floorIcon)
    {
        try
        {
            if (floorIcon != null && GodotObject.IsInstanceValid(floorIcon))
            {
                var floorCenter = floorIcon.GlobalPosition + floorIcon.Size * 0.5f;
                float mirroredLeft = 2f * floorCenter.X - (potions.GlobalPosition.X + potions.Size.X);
                var parentGlobal = (parent as Control)?.GlobalPosition ?? Vector2.Zero;
                return new Vector2(mirroredLeft - parentGlobal.X, potions.Position.Y);
            }
            return potions.Position + new Vector2(potions.Size.X + 12, 0);
        }
        catch
        {
            return potions.Position + new Vector2(potions.Size.X + 12, 0);
        }
    }

    private static void Rebuild()
    {
        if (_root == null || !GodotObject.IsInstanceValid(_root)) return;

        // Capacity may grow (future slot relic); rebuild the layout when it changes.
        int capacity = CurrentCapacity();
        if (capacity != _capacity)
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            DestroyExisting(tree);
            EnsureAttached();
            if (_root == null || !GodotObject.IsInstanceValid(_root)) return;
        }

        UpdateGeometry();

        var runState = Run();
        if (runState == null || runState.Players.Count == 0) return;

        Player player = runState.Players[0];
        var st = SatchelStore.ForPlayer(player);
        bool combat = CombatProbe.IsInLiveCombat(runState);

        for (int i = 0; i < _slotIcons.Count; i++)
        {
            var icon = _slotIcons[i];
            bool filled = i < st.Slots.Count && !string.IsNullOrEmpty(st.Slots[i]);
            var tint = filled ? Colors.White : new Color(0.42f, 0.45f, 0.5f, 0.85f);
            if (combat) tint = new Color(tint.R, tint.G, tint.B, Mathf.Min(tint.A, 0.5f));
            icon.Modulate = tint;
            _slots[i].MouseFilter = combat ? Control.MouseFilterEnum.Ignore : Control.MouseFilterEnum.Stop;
        }

        string state = $"{FormatSlots(st)}/{(combat ? "c" : "m")}";
        if (state != _lastRenderState)
        {
            _lastRenderState = state;
            ModEntry.Log.Info($"[ui] render slots=[{FormatSlots(st)}] combat={combat} pity={st.Pity:F2}", 1);
        }
    }

    private static string FormatSlots(PlayerSatchel st) =>
        string.Join("|", st.Slots.Select(s => string.IsNullOrEmpty(s) ? "-" : s));

    private static int CurrentCapacity()
    {
        try
        {
            var run = Run();
            if (run != null && run.Players.Count > 0)
                return Mathf.Max(1, SatchelStore.ForPlayer(run.Players[0]).Capacity);
        }
        catch { }
        return SatchelStore.DefaultCapacity;
    }

    private static void OnSlotHover(int slotIndex, bool entered)
    {
        try
        {
            var runState = Run();
            if (runState == null || runState.Players.Count == 0) return;
            var st = SatchelStore.ForPlayer(runState.Players[0]);
            var slot = _slots[slotIndex];
            if (!entered)
            {
                try { NHoverTipSet.Remove(slot); } catch { }
                return;
            }
            if (slotIndex < st.Slots.Count && !string.IsNullOrEmpty(st.Slots[slotIndex]))
            {
                var card = SatchelStore.ResolveCard(st.Slots[slotIndex]);
                var tips = HoverTipFactory.FromCardWithCardHoverTips(card, card?.IsUpgraded ?? false);
                if (tips != null) NHoverTipSet.CreateAndShow(slot, tips, HoverTipAlignment.Right);
                ModEntry.Log.Info($"[ui] hover slot {slotIndex} -> {st.Slots[slotIndex]}", 1);
            }
        }
        catch (Exception e)
        {
            ModEntry.Log.Warn($"[ui] hover failed: {e.Message}", 1);
        }
    }

    private static void OnSlotInput(int slotIndex, InputEvent evt)
    {
        if (evt is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;
        var runState = Run();
        if (runState == null || runState.Players.Count == 0) return;
        if (CombatProbe.IsInLiveCombat(runState))
        {
            ModEntry.Log.Info("[ui] slot click ignored (combat)", 1);
            return;
        }
        var st = SatchelStore.ForPlayer(runState.Players[0]);
        if (slotIndex >= st.Slots.Count || string.IsNullOrEmpty(st.Slots[slotIndex])) return; // empty slot: no popup

        var card = SatchelStore.ResolveCard(st.Slots[slotIndex]);
        string name = TarotNames.GetDisplayName(card);
        var slot = _slots[slotIndex];
        try { NHoverTipSet.Remove(slot); } catch { }
        _ = HandlePopupAsync(slotIndex, slot, name);
    }

    private static async System.Threading.Tasks.Task HandlePopupAsync(int slotIndex, Control slot, string cardName)
    {
        try
        {
            int result = await TarotSlotPopup.ShowAsync(slot, cardName, false);
            var runState = Run();
            if (runState == null || runState.Players.Count == 0) return;
            var model = ModEntry.Model;
            if (result == TarotSlotPopup.ResultUse) model.UseSlot(runState.Players[0], runState, slotIndex);
            else if (result == TarotSlotPopup.ResultDiscard) model.DiscardSlot(runState.Players[0], slotIndex);
            Rebuild();
        }
        catch (Exception e)
        {
            ModEntry.Log.Warn($"[ui] popup failed: {e.Message}", 1);
        }
    }

    /// <summary>
    /// Potion-full style feedback. The red overlay is the official <c>PotionErrorBg</c> texture and
    /// the shake replicates <c>NPotionContainer</c>'s own add-failed animation (the official method
    /// animates the potion container only, so the parameters are mirrored here).
    /// </summary>
    public static void PlayFullFeedback()
    {
        try
        {
            if (_root == null || !GodotObject.IsInstanceValid(_root)) return;

            if (_errorBg != null && GodotObject.IsInstanceValid(_errorBg))
            {
                var tweenErr = _errorBg.CreateTween();
                _errorBg.Modulate = new Color(1, 1, 1, 0.9f);
                tweenErr.TweenProperty(_errorBg, "modulate:a", 0.15f, 0.10);
                tweenErr.TweenProperty(_errorBg, "modulate:a", 0.9f, 0.10);
                tweenErr.TweenProperty(_errorBg, "modulate:a", 0f, 0.18);
            }

            var baseX = _root.Position.X;
            var tween = _root.CreateTween();
            tween.TweenProperty(_root, "position:x", baseX + 6, 0.04);
            tween.TweenProperty(_root, "position:x", baseX - 6, 0.08);
            tween.TweenProperty(_root, "position:x", baseX + 4, 0.08);
            tween.TweenProperty(_root, "position:x", baseX, 0.06);
        }
        catch (Exception e)
        {
            ModEntry.Log.Warn($"[ui] full feedback failed: {e.Message}", 1);
        }
    }

    // Test hooks for the automated driver (bypasses the popup; the UI path is verified manually).
    public static bool TestUseFirstSlot()
    {
        var runState = Run();
        if (runState == null || runState.Players.Count == 0) return false;
        var st = SatchelStore.ForPlayer(runState.Players[0]);
        int slot = FirstUsedSlot(st);
        if (slot < 0) return false;
        return ModEntry.Model.UseSlot(runState.Players[0], runState, slot);
    }

    public static bool TestDiscardFirstSlot()
    {
        var runState = Run();
        if (runState == null || runState.Players.Count == 0) return false;
        var st = SatchelStore.ForPlayer(runState.Players[0]);
        int slot = FirstUsedSlot(st);
        if (slot < 0) return false;
        return ModEntry.Model.DiscardSlot(runState.Players[0], slot);
    }

    private static int FirstUsedSlot(PlayerSatchel st)
    {
        SatchelStore.NormalizeSlots(st);
        for (int i = 0; i < st.Slots.Count; i++)
            if (!string.IsNullOrEmpty(st.Slots[i])) return i;
        return -1;
    }
}


