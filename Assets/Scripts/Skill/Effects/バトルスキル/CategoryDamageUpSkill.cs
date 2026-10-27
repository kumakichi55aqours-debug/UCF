using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 条件を満たすとき、指定した技のダメージを計算時に足す。
/// 対象条件が空なら相手を問わない。永続効果としてカードに置く。場の再計算では足さない。
/// </summary>
[System.Serializable]
[SkillDisplayName("(〇付与)カテゴリアップ")]
public class CategoryDamageUpSkill : SkillDB
{
    [SerializeField] public SearchCriteria 対象条件;
    [SerializeField] public int 追加ダメージ量;
    [SerializeField] public MoveTarget 適用先 = MoveTarget.両方;

    public override Task ActivateEffect(SkillContext context)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// この攻撃者が、この相手へ指定した技を当てるときに足すダメージ。
    /// </summary>
    public static int GetBonus(CardController attacker, CardController target, int moveIndex)
    {
        if (attacker == null || target == null)
            return 0;

        int bonus = 0;
        ActiveLocation location = attacker.GetCurrentActiveLocation();

        foreach (var category in PrintedSkills<CategoryDamageUpSkill>(attacker))
        {
            if (category.IsPersistentAt(attacker, location)
                && AppliesToMove(category.適用先, moveIndex)
                && category.Matches(target))
                bonus += category.追加ダメージ量;
        }

        foreach (var category in GrantedSkills<CategoryDamageUpSkill>(attacker))
        {
            if (AppliesToMove(category.適用先, moveIndex) && category.Matches(target))
                bonus += category.追加ダメージ量;
        }

        return bonus;
    }

    bool Matches(CardController target)
    {
        return target != null && target.MeetsCriteria(対象条件);
    }
}
