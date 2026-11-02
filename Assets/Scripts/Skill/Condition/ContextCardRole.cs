/// <summary>
/// 発動条件が、SkillContext のどのカードを見るか。
/// 先頭は発動カードなので、この項目がない既存データはそのまま発動カードを見る。
/// </summary>
public enum ContextCardRole
{
    発動カード,
    対象カード,
    攻撃者,
}

public static class ContextCardRoles
{
    public static CardController Resolve(ContextCardRole role, SkillContext context)
    {
        if (context == null)
            return null;

        switch (role)
        {
            case ContextCardRole.対象カード:
                return context.対象カード;
            case ContextCardRole.攻撃者:
                return context.Attacker;
            default:
                return context.発動カード;
        }
    }
}
