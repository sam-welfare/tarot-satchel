using System;
using MegaCrit.Sts2.Core.Localization;

namespace TarotSatchel;

/// <summary>
/// Minimal two-language text for mod-only strings (the game's own card names already come from the
/// official LocString tables). The game language comes from <see cref="LocManager.Language"/>.
/// </summary>
public static class LocText
{
    public static bool IsChinese
    {
        get
        {
            try
            {
                var lang = LocManager.Instance?.Language ?? "eng";
                return lang.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return true;
            }
        }
    }

    public static string T(string zh, string en) => IsChinese ? zh : en;

    /// <summary>Reward row prefix: "塔罗牌·" in Chinese, "Tarot · " otherwise.</summary>
    public static string TarotRowPrefix => IsChinese ? "塔罗牌·" : "Tarot · ";

    public static string Use => T("使用", "Use");
    public static string Discard => T("丢弃", "Discard");
    public static string Cancel => T("取消", "Cancel");
}
