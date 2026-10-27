using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// 自分のBゾーンに、自分以外で対象条件を満たすバトラーがいる間、バトラーを選ぶ技の対象にならない。
/// 相手側のバトラー全員への技は普通に受ける。複数体を選ぶ技も、選ばれなければ受けない。
/// 対象条件が空なら、自分以外のバトラーがいるだけで単体技の対象にならない。
/// 永続効果としてカードに置く。付与されたときも同じように見る。
/// </summary>
[System.Serializable]
[SkillDisplayName("(〇付与)技対象回避")]
public class EvadeMoveTargetSkill : SkillDB
{
    public SearchCriteria 対象条件;

    public override Task ActivateEffect(SkillContext context)
    {
        return Task.CompletedTask;
    }

    public static bool IsEvading(CardController card)
    {
        if (card == null || card.cardKind != CardKind.バトラー)
            return false;
        if (card.GetCurrentActiveLocation() != ActiveLocation.Bゾーン)
            return false;

        foreach (var skill in ActiveSkills(card))
        {
            if (HasOtherMatchingBattler(card, skill.対象条件))
                return true;
        }

        return false;
    }

    static IEnumerable<EvadeMoveTargetSkill> ActiveSkills(CardController card)
    {
        foreach (var evade in PrintedSkills<EvadeMoveTargetSkill>(card))
        {
            if (evade.IsPersistentAt(card, ActiveLocation.Bゾーン))
                yield return evade;
        }

        foreach (var evade in GrantedSkills<EvadeMoveTargetSkill>(card))
        {
            if (evade.ConditionsMet(card))
                yield return evade;
        }
    }

    static bool HasOtherMatchingBattler(CardController self, SearchCriteria criteria)
    {
        foreach (var other in MoveTargetEvasion.ListZoneBattlers(self, PlayerType.プレイヤー))
        {
            if (other == self)
                continue;

            if (criteria == null || other.MeetsCriteria(criteria, new SkillContext(self, other, other.transform)))
                return true;
        }

        return false;
    }
}
