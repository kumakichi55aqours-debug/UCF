using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

[Serializable]
[SkillDisplayName("(付与〇)カードリソース")]
public class ResourceModifierSkill : SkillDB
{
    public TargetList 効果対象;
    public PlayerType 対象側 = PlayerType.プレイヤー;
    public SearchCriteria 対象条件;
    public ResourceType リソースタイプ = ResourceType.エネルギー;
    public ElementList 対象属性;
    public MoveTarget 適用先;
    public int 効果数 = 1;

    public override TargetList GetEffectTarget() => 効果対象;
    public override SearchCriteria GetTargetCriteria() => 対象条件;

    public override async Task ActivateEffect(SkillContext context)
    {
        Debug.Log($"[ResourceModifierSkill] 発動開始 - リソースタイプ: {リソースタイプ}, 効果対象: {効果対象}, 対象属性: {対象属性}, 適用先: {適用先}, 効果数: {効果数}");
        var targetCards = await CollectTargetsAsync(効果対象, 対象側, 対象条件, context);

        if (context != null && context.RestrictTargetsToContextCard && context.対象カード != null)
        {
            if ((対象条件 == null || context.対象カード.MeetsCriteria(対象条件, context)) && !targetCards.Contains(context.対象カード))
            {
                targetCards.Add(context.対象カード);
            }

            targetCards = targetCards.Where(c => c == context.対象カード).ToList();
        }

        // 各ターゲットカードにリソース修正を適用
        foreach (var card in targetCards)
        {
            if (card == null)
                continue;

            switch (リソースタイプ)
            {
                case ResourceType.エネルギー:
                {
                    var battler = card.model as BattlerModel;
                    if (battler == null)
                        break;

                    bool apply1 = AppliesToMoveElement(適用先, 対象属性, battler.moveElement1, 0);
                    bool apply2 = AppliesToMoveElement(適用先, 対象属性, battler.moveElement2, 1);

                    if (apply1)
                    {
                        card.bonusEnergy1 += 効果数;
                        Debug.Log($"[ResourceModifierSkill] {card.cardName} Move1 bonus: {card.bonusEnergy1}");
                    }
                    if (apply2)
                    {
                        card.bonusEnergy2 += 効果数;
                        Debug.Log($"[ResourceModifierSkill] {card.cardName} Move2 bonus: {card.bonusEnergy2}");
                    }

                    card.RecalculateModifiedPowers();
                    card.UpdateDisplay();

                    Debug.Log($"{card.cardName} に対して Move1?{apply1} Move2?{apply2} のエネルギーコスト {効果数} を適用");
                    break;
                }

                case ResourceType.コスト:
                    card.bonusCost += 効果数;
                    var costView = card.GetComponent<BattleCardView>();
                    if (costView == null || !costView.IsBack)
                        card.UpdateDisplay();
                    Debug.Log($"{card.cardName} のコスト補正が {効果数} 変動しました。支払いコスト: {card.GetModifiedCost()}");
                    break;

                case ResourceType.HP:
                    // HP修正（直接変更）
                    if (効果数 > 0)
                    {
                        // HP回復
                        card.curHp += 効果数;
                        Debug.Log($"{card.cardName} のHPが {効果数} 回復しました。現在のHP: {card.curHp}");
                    }
                    else if (効果数 < 0)
                    {
                        // HPダメージ
                        card.curHp += 効果数; // 負の値なので減算される
                        if (card.curHp < 0) card.curHp = 0;
                        Debug.Log($"{card.cardName} のHPが {-効果数} 減少しました。現在のHP: {card.curHp}");
                    }
                    card.UpdateDisplay();
                    break;
            }
        }
    }
}