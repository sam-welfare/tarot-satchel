using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace TarotSatchel;

/// <summary>
/// Small async helper that shows a native PopupMenu (a Window, always above the game UI) and
/// awaits the picked item index. Returns -1 when the menu is dismissed without a choice.
/// </summary>
public static class ChoicePopup
{
    public static Task<int> ShowAsync(string title, IReadOnlyList<string> options)
    {
        var tcs = new TaskCompletionSource<int>();
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree?.Root == null)
        {
            tcs.SetResult(-1);
            return tcs.Task;
        }

        var popup = new PopupMenu { Name = "TarotSatchelChoice" };
        popup.AddItem(title, -1);
        popup.SetItemDisabled(0, true);
        for (int i = 0; i < options.Count; i++) popup.AddItem(options[i], i);

        popup.IdPressed += id =>
        {
            int result = (int)id;
            try { popup.QueueFree(); } catch { }
            tcs.TrySetResult(result);
        };
        popup.PopupHide += () =>
        {
            try { popup.QueueFree(); } catch { }
            tcs.TrySetResult(-1);
        };

        tree.Root.AddChild(popup);
        var screen = tree.Root.Size;
        popup.Popup(new Rect2I((int)(screen.X * 0.5f) - 160, (int)(screen.Y * 0.5f) - 80, 0, 0));
        return tcs.Task;
    }
}
