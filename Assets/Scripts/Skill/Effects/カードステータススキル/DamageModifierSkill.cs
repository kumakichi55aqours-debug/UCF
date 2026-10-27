using System.Threading.Tasks;
using UnityEngine;

[System.Serializable]
[SkillDisplayName("(〇付与)ダメージ増減")]
public class DamageModifierSkill : SkillDB
{
    [Header("ダメージ増減スキル")]
    public int 増減値;
    public ElementList 対象属性 = ElementList.None;
    public MoveTarget 適用先 = MoveTarget.両方;
    public SearchCriteria 攻撃者条件;
    public DamageModifierType 増減タイプ = DamageModifierType.固定値;

    public override Task ActivateEffect(SkillContext context)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// ダメージ増減を計算する。増減値が正なら増え、負なら減る。
    /// 技1は1、技2は2。
    /// </summary>
    public int CalculateModifiedDamage(int originalDamage, CardController attackerCard, int moveIndex, ElementList moveElement, CardController defenderCard = null)
    {
        if (!IsApplicable(moveElement, moveIndex, attackerCard, defenderCard))
            return originalDamage;

        if (増減タイプ == DamageModifierType.固定値)
            return Mathf.Max(0, originalDamage + 増減値);

        if (増減タイプ == DamageModifierType.パーセント)
        {
            float multiplier = (100f + 増減値) / 100f;
            return Mathf.Max(0, Mathf.RoundToInt(originalDamage * multiplier));
        }

        return originalDamage;
    }

    /// <summary>
    /// 技1は1、技2は2。
    /// </summary>
    public bool IsApplicable(ElementList moveElement, int moveIndex, CardController attackerCard, CardController defenderCard = null)
    {
        if (対象属性 != ElementList.None && moveElement != 対象属性)
            return false;
        if (!AppliesToMove(適用先, moveIndex - 1))
            return false;
        if (攻撃者条件 == null)
            return true;

        var criteriaContext = defenderCard != null
            ? new SkillContext(defenderCard, attackerCard, attackerCard.transform)
            : null;
        return attackerCard.MeetsCriteria(攻撃者条件, criteriaContext);
    }
}
