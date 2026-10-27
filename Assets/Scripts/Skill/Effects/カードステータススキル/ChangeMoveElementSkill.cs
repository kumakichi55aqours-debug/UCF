using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// バトラーの技の属性を変更する。
/// 変更は、そのカードが墓地へ送られるか手札に戻るまで残る。
/// </summary>
[Serializable]
[SkillDisplayName("技属性変更")]
public class ChangeMoveElementSkill : SkillDB
{
    public TargetList 効果対象;
    public PlayerType 対象側 = PlayerType.プレイヤー;
    public SearchCriteria 対象条件;
    public List<ElementList> 対象属性;
    public MoveTarget 適用先 = MoveTarget.両方;
    public ElementList 変更後属性;

    public override TargetList GetEffectTarget() => 効果対象;
    public override SearchCriteria GetTargetCriteria() => 対象条件;

    public override async Task ActivateEffect(SkillContext context)
    {
        var targetCards = KeepContextCard(await CollectTargetsAsync(効果対象, 対象側, 対象条件, context), context);

        foreach (var card in targetCards)
        {
            if (card == null || card.cardKind != CardKind.バトラー)
                continue;

            if (対象条件 != null && !card.MeetsCriteria(対象条件, context))
                continue;

            var battler = card.model as BattlerModel;
            if (battler == null)
                continue;

            bool apply1 = AppliesToMoveElement(適用先, 対象属性, battler.moveElement1, 0);
            bool apply2 = AppliesToMoveElement(適用先, 対象属性, battler.moveElement2, 1);

            if (apply1)
            {
                battler.moveElement1 = 変更後属性;
                card.moveElement1 = 変更後属性;
            }

            if (apply2)
            {
                battler.moveElement2 = 変更後属性;
                card.moveElement2 = 変更後属性;
            }

            if (apply1 || apply2)
            {
                card.UpdateDisplay();
                Debug.Log($"{card.cardName} の技属性を {変更後属性} に変更 Move1?{apply1} Move2?{apply2}");
            }
        }
    }
}
