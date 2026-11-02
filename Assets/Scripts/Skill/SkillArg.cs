/// <summary>
/// 複合スキルの中で、前の効果から次の効果へ渡す数値。
/// 書くスキルと読むスキルで、同じものを選ぶ。なしは渡さない。
/// 種類を増やすときは、ここへ名前を足す。
/// </summary>
public enum SkillArg
{
    なし,
    ドロー数,
    送ったカードのHP,
    倒したバトラーの元々のHP,
}

public static class SkillArgKey
{
    public static bool TryGetKey(SkillArg arg, out string key)
    {
        switch (arg)
        {
            case SkillArg.ドロー数:
                key = "DrawCount";
                return true;
            case SkillArg.送ったカードのHP:
                key = "SentCardHp";
                return true;
            case SkillArg.倒したバトラーの元々のHP:
                key = "DefeatedOriginalHp";
                return true;
            default:
                key = null;
                return false;
        }
    }
}
