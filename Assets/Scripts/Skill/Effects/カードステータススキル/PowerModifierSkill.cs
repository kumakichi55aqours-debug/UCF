using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

[System.Serializable]
[SkillDisplayName("(〇付与)パワー")]
public class PowerModifierSkill : SkillDB
{
    public TargetList 効果対象;
    public PlayerType 対象側 = PlayerType.プレイヤー;
    public SearchCriteria 対象条件;
    public List<ElementList> 対象属性;
    public MoveTarget 適用先 = MoveTarget.両方;
    public int 効果数;

    [Header("1体ずつ判定する。スキルの発動条件とは別")]
    [SerializeReference]
    public List<ISkillCondition> 対象ごとの条件 = new List<ISkillCondition>();

    public override TargetList GetEffectTarget() => 効果対象;
    public override SearchCriteria GetTargetCriteria() => 対象条件;

    public override async Task ActivateEffect(SkillContext context)
    {
        var targetCards = KeepContextCard(await CollectTargetsAsync(効果対象, 対象側, 対象条件, context), context);

        foreach (var card in targetCards)
        {
            if (card.cardKind != CardKind.バトラー)
                continue;

            if (対象条件 != null && !card.MeetsCriteria(対象条件, context))
                continue;

            if (!ConditionsMet(対象ごとの条件, card))
                continue;

            var battler = card.model as BattlerModel;
            bool apply1 = AppliesToMoveElement(適用先, 対象属性, battler.moveElement1, 0);
            bool apply2 = AppliesToMoveElement(適用先, 対象属性, battler.moveElement2, 1);

            if (apply1) card.bonusPower1 += 効果数;
            if (apply2) card.bonusPower2 += 効果数;

            card.RecalculateModifiedPowers();
        }
    }

    protected override void CopyPolymorphicFieldsTo(SkillDB clone)
    {
        if (clone is PowerModifierSkill power)
        {
            power.対象ごとの条件 = 対象ごとの条件 != null
                ? new List<ISkillCondition>(対象ごとの条件)
                : new List<ISkillCondition>();
        }
    }
}
