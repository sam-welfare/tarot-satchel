using System;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Localization;

namespace TarotSatchel;

/// <summary>
/// Use/discard popup for a tarot slot. The visuals are taken directly from the official potion
/// popup scene <c>res://scenes/potions/potion_popup.tscn</c>: its Container texture and the two
/// buttons' official Label/Background children are reused as-is, so the look matches vanilla
/// pixel for pixel. Only the click handling is ours (the official button scripts need a potion).
/// Clicking outside dismisses. Result: 1 = use, 2 = discard, 0 = dismissed.
/// </summary>
public static class TarotSlotPopup
{
    public const int ResultDismiss = 0;
    public const int ResultUse = 1;
    public const int ResultDiscard = 2;

    private const string PopupScenePath = "res://scenes/potions/potion_popup.tscn";
    private const string PopupBgPath = "res://images/potions/potion_pop_up/potion_popup.png";
    private const string PopupTopPath = "res://images/potions/potion_pop_up/potion_popup_top.png";
    private const string PopupBottomPath = "res://images/potions/potion_pop_up/potion_popup_bottom.png";
    private const string FontPath = "res://themes/kreon_bold_glyph_space_two.tres";

    private static readonly Vector2 PopupSize = new(259, 239);
    private static readonly Vector2 UseRect = new(11, 50);
    private static readonly Vector2 UseSize = new(236, 86);
    private static readonly Vector2 DiscardRect = new(12, 143);
    private static readonly Vector2 DiscardSize = new(236, 86);

    public static Task<int> ShowAsync(Control anchor, string cardName, bool inCombat)
    {
        var tcs = new TaskCompletionSource<int>();
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree?.Root == null)
        {
            tcs.TrySetResult(ResultDismiss);
            return tcs.Task;
        }

        var layer = new CanvasLayer { Name = "TarotSlotPopupLayer", Layer = 100 };
        var screenRoot = new Control { Name = "TarotSlotPopupRoot", MouseFilter = Control.MouseFilterEnum.Stop };
        screenRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(screenRoot);

        var catcher = new ColorRect { Color = new Color(0, 0, 0, 0.001f), MouseFilter = Control.MouseFilterEnum.Stop };
        catcher.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        screenRoot.AddChild(catcher);
        catcher.GuiInput += evt =>
        {
            if (evt is InputEventMouseButton { Pressed: true }) Finish(ResultDismiss);
        };

        var popup = new Control { Name = "TarotPotionPopup", Size = PopupSize, PivotOffset = new Vector2(129, 0) };
        screenRoot.AddChild(popup);

        if (!TryBuildFromOfficialScene(popup, inCombat, () => Finish(ResultUse), () => Finish(ResultDiscard)))
            BuildManualReplica(popup, inCombat, () => Finish(ResultUse), () => Finish(ResultDiscard));

        // Place the popup below the slot like the vanilla holder popup, clamped to the screen.
        var screen = tree.Root.Size;
        var slotCenter = anchor != null && GodotObject.IsInstanceValid(anchor)
            ? anchor.GetScreenPosition() + anchor.Size * 0.5f
            : new Vector2(screen.X * 0.5f, screen.Y * 0.5f);
        var desired = new Vector2(slotCenter.X - 129f, slotCenter.Y + 26f);
        popup.Position = new Vector2(
            Mathf.Clamp(desired.X, 8, Mathf.Max(8, screen.X - PopupSize.X - 8)),
            Mathf.Clamp(desired.Y, 8, Mathf.Max(8, screen.Y - PopupSize.Y - 8)));

        tree.Root.AddChild(layer);

        void Finish(int result)
        {
            if (!tcs.TrySetResult(result)) return;
            try { layer.QueueFree(); } catch { }
        }

        layer.TreeExiting += () =>
        {
            if (!tcs.Task.IsCompleted) tcs.TrySetResult(ResultDismiss);
        };

        return tcs.Task;
    }

    /// <summary>Extract the official scene's visual nodes (no scripts) and wire our own clicks.</summary>
    private static bool TryBuildFromOfficialScene(Control popup, bool disabled, Action onUse, Action onDiscard)
    {
        try
        {
            var packed = ResourceLoader.Load<PackedScene>(PopupScenePath);
            if (packed == null) return false;
            var inst = packed.Instantiate();
            if (inst == null) return false;

            var container = inst.GetNodeOrNull<Control>("Container");
            if (container == null) { inst.QueueFree(); return false; }

            container.GetParent()?.RemoveChild(container);
            container.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
            container.Position = Vector2.Zero;
            container.Size = PopupSize;
            container.MouseFilter = Control.MouseFilterEnum.Ignore;
            popup.AddChild(container);

            ExtractButton(container, "UseButton", UseRect, UseSize, "使用", disabled, onUse);
            ExtractButton(container, "DiscardButton", DiscardRect, DiscardSize, "丢弃", disabled, onDiscard);
            inst.QueueFree();
            ModEntry.Log.Info("[ui] popup built from official potion scene", 1);
            return true;
        }
        catch (Exception e)
        {
            ModEntry.Log.Warn($"[ui] official popup scene failed ({e.Message}); using manual replica", 1);
            return false;
        }
    }

    private static void ExtractButton(Control container, string nodePath,
        Vector2 pos, Vector2 size, string text, bool disabled, Action onPressed)
    {
        try
        {
            var source = container.GetNodeOrNull<Control>(nodePath);
            var target = new Control { Name = nodePath, Position = pos, Size = size, MouseFilter = Control.MouseFilterEnum.Ignore };
            if (source != null)
            {
                // Detach the original button node so its official script/label never renders; we only
                // reuse its visual children (Background texture etc.).
                source.GetParent()?.RemoveChild(source);
                foreach (var child in source.GetChildren())
                {
                    // Skip the official label: its MegaLabel renderer keeps drawing the original
                    // "Use"/"Discard" string; we draw the localised text with the same style instead.
                    if (child.Name.ToString() == "Label") continue;
                    child.GetParent()?.RemoveChild(child);
                    target.AddChild(child);
                }
                AddStyledLabel(target, nodePath == "UseButton"
                    ? new Color(1f, 0.9647f, 0.8863f, 1f)
                    : new Color(1f, 0.3333f, 0.3333f, 1f), text);
            }
            else
            {
                AddFallbackButtonVisuals(target, nodePath == "UseButton" ? PopupTopPath : PopupBottomPath,
                    nodePath == "UseButton" ? new Vector2(-2, -39) : new Vector2(-3, -3),
                    nodePath == "UseButton" ? new Vector2(241, 129) : new Vector2(241, 93),
                    nodePath == "UseButton" ? new Color(1f, 0.9647f, 0.8863f, 1f) : new Color(1f, 0.3333f, 0.3333f, 1f),
                    text);
            }
            container.AddChild(target);

            var click = new Button
            {
                Name = "ClickArea",
                Flat = true,
                FocusMode = Control.FocusModeEnum.None,
                MouseFilter = Control.MouseFilterEnum.Stop,
                Position = Vector2.Zero,
                Size = size,
            };
            foreach (var state in new[] { "normal", "hover", "pressed", "focus", "disabled" })
                click.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
            if (disabled)
            {
                target.Modulate = new Color(1, 1, 1, 0.45f);
                click.Disabled = true;
            }
            else
            {
                click.Pressed += onPressed;
                click.MouseEntered += () => { var tw = target.CreateTween(); tw.TweenProperty(target, "scale", new Vector2(1.03f, 1.03f), 0.08); };
                click.MouseExited += () => { var tw = target.CreateTween(); tw.TweenProperty(target, "scale", Vector2.One, 0.08); };
            }
            target.AddChild(click);
        }
        catch (Exception e)
        {
            ModEntry.Log.Warn($"[ui] popup button {nodePath} extract failed: {e.Message}", 1);
        }
    }

    /// <summary>Numeric replica of the official layout, used only if the scene cannot be loaded.</summary>
    private static void BuildManualReplica(Control popup, bool disabled, Action onUse, Action onDiscard)
    {
        var bg = new TextureRect
        {
            Name = "Container",
            Texture = LoadTex(PopupBgPath),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Position = Vector2.Zero,
            Size = PopupSize,
        };
        popup.AddChild(bg);

        AddManualButton(popup, "UseButton", UseRect, UseSize, PopupTopPath, new Vector2(-2, -39), new Vector2(241, 129),
            new Color(1f, 0.9647f, 0.8863f, 1f), LocOr("USE", "使用"), disabled, onUse);
        AddManualButton(popup, "DiscardButton", DiscardRect, DiscardSize, PopupBottomPath, new Vector2(-3, -3), new Vector2(241, 93),
            new Color(1f, 0.3333f, 0.3333f, 1f), LocOr("DISCARD", "丢弃"), disabled, onDiscard);
    }

    private static void AddManualButton(Control parent, string name, Vector2 pos, Vector2 size,
        string bgPath, Vector2 bgOffset, Vector2 bgSize, Color labelColor, string text, bool disabled, Action onPressed)
    {
        var button = new Control { Name = name, Position = pos, Size = size, MouseFilter = Control.MouseFilterEnum.Stop };
        parent.AddChild(button);
        AddFallbackButtonVisuals(button, bgPath, bgOffset, bgSize, labelColor, text);
        button.AddChild(new Button
        {
            Name = "ClickArea",
            Flat = true,
            FocusMode = Control.FocusModeEnum.None,
            MouseFilter = Control.MouseFilterEnum.Stop,
            Position = Vector2.Zero,
            Size = size,
        });
        if (!disabled)
        {
            var click = (Button)button.GetChild(button.GetChildCount() - 1);
            foreach (var state in new[] { "normal", "hover", "pressed", "focus", "disabled" })
                click.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
            click.Pressed += onPressed;
        }
    }

    private static void AddFallbackButtonVisuals(Control button, string bgPath, Vector2 bgOffset, Vector2 bgSize, Color labelColor, string text)
    {
        var background = new TextureRect
        {
            Name = "Background",
            Texture = LoadTex(bgPath),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            Modulate = new Color(1, 1, 1, 0.2667f),
            Position = bgOffset,
            Size = bgSize,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        button.AddChild(background);

        var label = new Label
        {
            Name = "Label",
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Position = Vector2.Zero,
            Size = button.Size,
        };
        label.AddThemeColorOverride("font_color", labelColor);
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.502f));
        label.AddThemeConstantOverride("outline_size", 12);
        label.AddThemeFontSizeOverride("font_size", 32);
        var font = ResourceLoader.Load<Font>(FontPath);
        if (font != null) label.AddThemeFontOverride("font", font);
        button.AddChild(label);
    }

    /// <summary>Official potion-popup label style (font, size, outline, colour).</summary>
    private static void AddStyledLabel(Control parent, Color color, string text)
    {
        var label = new Label
        {
            Name = "TarotLabel",
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Position = Vector2.Zero,
            Size = parent.Size,
        };
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.502f));
        label.AddThemeConstantOverride("outline_size", 12);
        label.AddThemeFontSizeOverride("font_size", 32);
        var font = ResourceLoader.Load<Font>(FontPath);
        if (font != null) label.AddThemeFontOverride("font", font);
        parent.AddChild(label);
    }

    /// <summary>MegaLabel (official popup label) needs SetTextAutoSize; plain Label uses Text.</summary>
    private static void SetLabelText(Control label, string text)
    {
        try
        {
            var method = label.GetType().GetMethod("SetTextAutoSize",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (method != null)
            {
                method.Invoke(label, new object[] { text });
                var refresh = label.GetType().GetMethod("RefreshFont",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                refresh?.Invoke(label, null);
                return;
            }
            if (label is Label plain) plain.Text = text;
        }
        catch (Exception e)
        {
            ModEntry.Log.Warn($"[ui] popup label text failed: {e.Message}", 1);
            if (label is Label fallback) fallback.Text = text;
        }
    }

    private static Texture2D LoadTex(string path)
    {
        try { return ResourceLoader.Load<Texture2D>(path); }
        catch { return null; }
    }

    /// <summary>Game localisation with a literal fallback (official popup uses loc keys "USE"/"DISCARD").</summary>
    private static string LocOr(string key, string fallback)
    {
        foreach (var table in new[] { "gameplay_ui", "ui", "common" })
        {
            try
            {
                var loc = LocString.GetIfExists(table, key);
                if (loc == null) continue;
                var text = loc.GetFormattedText();
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }
            catch { }
        }
        return fallback;
    }
}
