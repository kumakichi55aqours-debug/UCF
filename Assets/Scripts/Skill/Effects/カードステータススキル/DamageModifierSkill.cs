using System.Collections.Generic;
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
    public DamageModifierType 増減タイプ = DamageModifierType.固定値;

    [Header("このカードがダメージを受けるとき、この状態も満たすときだけ増減する。空なら状態では絞らない")]
    [SerializeReference]
    public List<ISkillCondition> 対象状態 = new List<ISkillCondition>();

    public override IReadOnlyList<ISkillCondition> GetTargetState() => 対象状態;

    public override Task ActivateEffect(SkillContext context)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// ダメージ増減を計算する。増減値が正なら増え、負なら減る。
    /// 技1は1、技2は2。
    /// </summary>
    public int CalculateModifiedDamage(int originalDamage, int moveIndex, ElementList moveElement)
    {
        if (!IsApplicable(moveElement, moveIndex))
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
    public bool IsApplicable(ElementList moveElement, int moveIndex)
    {
        if (対象属性 != ElementList.None && moveElement != 対象属性)
            return false;
        return AppliesToMove(適用先, moveIndex - 1);
    }

    protected override void CopyPolymorphicFieldsTo(SkillDB clone)
    {
        if (clone is DamageModifierSkill damage)
        {
            damage.対象状態 = 対象状態 != null
                ? new List<ISkillCondition>(対象状態)
                : new List<ISkillCondition>();
        }
    }
}
