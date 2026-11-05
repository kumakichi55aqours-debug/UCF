using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// フィールドにいるあいだ、条件を満たすバトラーが受けるダメージを増減する。
/// 技ダメージと効果ダメージのどちらに適用するかは、効果ごとに選ぶ。ダメージ計算のときだけ見る。
/// </summary>
[Serializable]
[SkillDisplayName("被ダメージ変更")]
public class IncomingDamageModifierSkill : SkillDB
{
    public int 増減値 = 1;

    [Tooltip("オンなら、技で受けるダメージに増減を適用する。")]
    public bool 技ダメージに適用 = true;

    [Tooltip("オンなら、ダメージ効果（DamageSkill）で受けるダメージに増減を適用する。")]
    public bool 効果ダメージに適用 = false;

    [Tooltip("このカードの持ち主から見た対象。プレイヤーは自分側、相手は相手側、両方はどちらも。")]
    public PlayerType 対象側 = PlayerType.相手;

    [Tooltip("ダメージを受けるカードの種類やカテゴリ。空ならカードの中身では絞らない。")]
    public SearchCriteria 対象条件;

    [Tooltip("上の条件に合うカードのうち、この状態も満たすときだけ増減する。空なら状態では絞らない。")]
    [SerializeReference]
    public List<ISkillCondition> 被ダメージ条件 = new List<ISkillCondition>();

    public override Task ActivateEffect(SkillContext context)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// 両B、Fゾーンのこの効果のうち、技ダメージに適用するものだけを足す。
    /// </summary>
    public static int Apply(CardController defender, int damage)
    {
        return Apply(defender, damage, effectDamage: false);
    }

    /// <summary>
    /// 両B、Fゾーンのこの効果のうち、効果ダメージに適用するものだけを足す。
    /// </summary>
    public static int ApplyToEffectDamage(CardController defender, int damage)
    {
        return Apply(defender, damage, effectDamage: true);
    }

    static int Apply(CardController defender, int damage, bool effectDamage)
    {
        if (defender == null || defender.cardKind != CardKind.バトラー)
            return damage;

        var battle = BattleManager.Instance;
        if (battle == null)
            return damage;

        int bonus = Sum(battle.pBattlerZone, defender, effectDamage) + Sum(battle.eBattlerZone, defender, effectDamage)
                  + Sum(battle.pFieldZone, defender, effectDamage) + Sum(battle.eFieldZone, defender, effectDamage);
        if (bonus == 0)
            return damage;

        return Mathf.Max(0, damage + bonus);
    }

    static int Sum(Transform zone, CardController defender, bool effectDamage)
    {
        if (zone == null)
            return 0;

        int bonus = 0;
        foreach (var field in zone.GetComponentsInChildren<CardController>())
        {
            foreach (var skill in PrintedSkills<IncomingDamageModifierSkill>(field))
            {
                if (!(effectDamage ? skill.効果ダメージに適用 : skill.技ダメージに適用))
                    continue;
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
        if (対象条件 != null && !対象条件.MatchesCard(defender, field, checkSource: true))
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
