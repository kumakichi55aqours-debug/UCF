using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// フィールドにいるあいだ、条件を満たすバトラーが技で受けるダメージを増減する。
/// 効果ダメージは対象にしない。ダメージ計算のときだけ見る。
/// </summary>
[Serializable]
[SkillDisplayName("被ダメージ変更")]
public class IncomingDamageModifierSkill : SkillDB
{
    public int 増減値 = 1;

    [Tooltip("このカードの持ち主から見た対象。プレイヤーは自分側、相手は相手側、両方はどちらも。")]
    public PlayerType 対象側 = PlayerType.相手;

    [SerializeReference]
    public List<ISkillCondition> 被ダメージ条件 = new List<ISkillCondition>();

    public override Task ActivateEffect(SkillContext context)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// 両B、Fゾーンのこの効果を、技ダメージへ足す。
    /// </summary>
    public static int Apply(CardController defender, int damage)
    {
        if (defender == null || defender.cardKind != CardKind.バトラー)
            return damage;

        var battle = BattleManager.Instance;
        if (battle == null)
            return damage;

        int bonus = Sum(battle.pBattlerZone, defender) + Sum(battle.eBattlerZone, defender)
                  + Sum(battle.pFieldZone, defender) + Sum(battle.eFieldZone, defender);
        if (bonus == 0)
            return damage;

        return Mathf.Max(0, damage + bonus);
    }

    static int Sum(Transform zone, CardController defender)
    {
        if (zone == null)
            return 0;

        int bonus = 0;
        foreach (var field in zone.GetComponentsInChildren<CardController>())
        {
            foreach (var skill in PrintedSkills<IncomingDamageModifierSkill>(field))
            {
                if (skill.AppliesTo(field, defender))
                    bonus += skill.増減値;
            }
        }

        return bonus;
    }

    bool AppliesTo(CardController field, CardController defender)
    {
        if (!IsPersistentAt(field, ActiveLocation.Fゾーン))
            return false;
        bool ownerSide = field.isPlayerCard == defender.isPlayerCard;
        if (ownerSide && !TargetScope.IncludesOwner(対象側))
            return false;
        if (!ownerSide && !TargetScope.IncludesOpponent(対象側))
            return false;
        return ConditionsMet(被ダメージ条件, defender);
    }

    protected override void CopyPolymorphicFieldsTo(SkillDB clone)
    {
        if (clone is IncomingDamageModifierSkill damage)
        {
            damage.被ダメージ条件 = 被ダメージ条件 != null
                ? new List<ISkillCondition>(被ダメージ条件)
                : new List<ISkillCondition>();
        }
    }
}
