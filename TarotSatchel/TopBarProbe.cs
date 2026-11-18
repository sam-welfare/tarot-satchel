using System;
using Godot;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Potions;

namespace TarotSatchel;

/// <summary>Debug helper: dumps the top bar / potion belt geometry once so the mod can mirror the
/// vanilla placement and reuse the exact same visuals. Enabled with TAROT_SATCHEL_PROBE=1.</summary>
public static class TopBarProbe
{
    private static bool _dumped;

    /// <summary>Dump the official potion popup once it is open (visual blueprint for our popup).</summary>
    public static void DumpPotionPopupOnce()
    {
        if (_dumpedPopup) return;
        try
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            var popup = FindFirst<MegaCrit.Sts2.Core.Nodes.Potions.NPotionPopup>(tree?.Root);
            if (popup == null) return;
            _dumpedPopup = true;
            ModEntry.Log.Info("[probe] ===== potion popup subtree =====", 1);
            Dump(popup, 0, 5);
            ModEntry.Log.Info("[probe] ===== end popup =====", 1);
        }
        catch (Exception e)
        {
            ModEntry.Log.Warn($"[probe] popup dump failed: {e.Message}", 1);
        }
    }

    /// <summary>Read the official NPotionPopup scene blueprint (node tree, types, properties, textures).</summary>
    public static void DumpPotionPopupBlueprint()
    {
        if (_dumpedBlueprint) return;
        try
        {
            var type = typeof(MegaCrit.Sts2.Core.Nodes.Potions.NPotionPopup);
            var prop = type.GetProperty("ScenePath",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                ?? type.GetProperty("ScenePath",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (prop == null) { ModEntry.Log.Warn("[probe] NPotionPopup.ScenePath not found", 1); return; }
            var path = prop.GetValue(null) as string;
            if (string.IsNullOrEmpty(path)) { ModEntry.Log.Warn("[probe] NPotionPopup scene path empty", 1); return; }
            _dumpedBlueprint = true;
            ModEntry.Log.Info($"[probe] ===== potion popup scene {path} =====", 1);

            var packed = ResourceLoader.Load<PackedScene>(path);
            if (packed == null) { ModEntry.Log.Warn("[probe] scene load failed", 1); return; }
            var state = packed.GetState();
            int count = state.GetNodeCount();
            for (int i = 0; i < count; i++)
            {
                var nodeName = state.GetNodeName(i);
                var nodeType = state.GetNodeType(i);
                var nodePath = state.GetNodePath(i);
                ModEntry.Log.Info($"[probe] node [{i}] {nodePath} ({nodeName}) type={nodeType}", 1);
                int props = state.GetNodePropertyCount(i);
                for (int p = 0; p < props; p++)
                {
                    var propName = state.GetNodePropertyName(i, p);
                    var value = state.GetNodePropertyValue(i, p);
                    string text = value.ToString();
                    try
                    {
                        if (value.VariantType == Variant.Type.Object)
                        {
                            var res = value.As<Resource>();
                            if (res != null)
                                text = string.IsNullOrEmpty(res.ResourcePath) ? $"<embedded {res.GetType().Name}>" : res.ResourcePath;
                        }
                    }
                    catch { }
                    if (text.Length > 160) text = text.Substring(0, 160) + "…";
                    ModEntry.Log.Info($"[probe]    {propName} = {text}", 1);
                }
            }
            ModEntry.Log.Info("[probe] ===== end blueprint =====", 1);
        }
        catch (Exception e)
        {
            ModEntry.Log.Warn($"[probe] blueprint dump failed: {e.Message}", 1);
        }
    }

    private static bool _dumpedBlueprint;
    private static bool _dumpedPopup;

    public static void DumpOnce()
    {
        if (_dumped) return;
        var tree = Engine.GetMainLoop() as SceneTree;
        var topBar = FindFirst<NTopBar>(tree?.Root);
        if (topBar == null) return;
        _dumped = true;

        ModEntry.Log.Info("[probe] ===== top bar children =====", 1);
        Dump(topBar, 0, 2);

        ModEntry.Log.Info("[probe] ===== named elements =====", 1);
        DumpNamed("PotionContainer", () => topBar.PotionContainer);
        DumpNamed("FloorIcon", () => topBar.FloorIcon as Node);
        DumpNamed("RoomIcon", () => topBar.RoomIcon as Node);
        DumpNamed("BossIcon", () => topBar.BossIcon as Node);
        DumpNamed("Timer", () => topBar.Timer as Node);
        DumpNamed("RoomIconsContainer", () => topBar.RoomIconsContainer);
        DumpNamed("LeftContainer", () => topBar.LeftContainer);
        DumpNamed("RightContainer", () => topBar.RightContainer);

        try
        {
            var potion = topBar.PotionContainer;
            if (potion != null)
            {
                ModEntry.Log.Info("[probe] ===== potion container subtree =====", 1);
                Dump(potion, 0, 4);
            }
        }
        catch (Exception e)
        {
            ModEntry.Log.Warn($"[probe] potion dump failed: {e.Message}", 1);
        }
        ModEntry.Log.Info("[probe] ===== end =====", 1);
    }

    private static void DumpNamed(string label, Func<Node> get)
    {
        try
        {
            var node = get();
            if (node == null) { ModEntry.Log.Info($"[probe] {label}: <null>", 1); return; }
            Info(label, node);
        }
        catch (Exception e)
        {
            ModEntry.Log.Warn($"[probe] {label}: <error {e.Message}>", 1);
        }
    }

    private static void Info(string label, Node node)
    {
        string extra = "";
        if (node is Control c)
        {
            extra = $" global={c.GlobalPosition} size={c.Size} pos={c.Position} anchor=({c.AnchorLeft:F2},{c.AnchorTop:F2},{c.AnchorRight:F2},{c.AnchorBottom:F2})";
        }
        ModEntry.Log.Info($"[probe] {label}: {node.Name} [{node.GetType().Name}]{extra}", 1);
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

    private static void Dump(Node node, int depth, int maxDepth)
    {
        if (node == null || depth > maxDepth) return;
        string extra = "";
        if (node is Control c)
        {
            extra = $" pos={c.Position} size={c.Size} global={c.GlobalPosition} scale={c.Scale}";
            if (node is TextureRect tr) extra += $" texture={tr.Texture?.ResourcePath}";
            else if (node is NinePatchRect np) extra += $" texture={np.Texture?.ResourcePath}";
            else if (node is TextureButton tb) extra += $" texture={tb.TextureNormal?.ResourcePath}";
        }
        ModEntry.Log.Info($"[probe] {new string(' ', depth * 2)}{node.Name} [{node.GetType().Name}]{extra}", 1);
        foreach (var child in node.GetChildren())
        {
            if (child is Control || child is Container) Dump(child, depth + 1, maxDepth);
        }
    }
}
