
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// バトラーの技の情報を変更する。
/// 変更は、そのカードが墓地へ送られるか手札に戻るまで残る。
/// </summary>
[Serializable]
[SkillDisplayName("技情報変更")]
public class ChangeMoveSkill : SkillDB
{
    public TargetList 効果対象;
    public PlayerType 対象側 = PlayerType.プレイヤー;
    public SearchCriteria 対象条件;

    [Header("候補を集めるときに1体ずつ見る。対象条件とは別")]
    [SerializeReference]
    public List<ISkillCondition> 対象状態 = new List<ISkillCondition>();

    [Header("変更する技")]
    public MoveTarget 適用先 = MoveTarget.両方;

    [Header("変更対象の属性（空欄なら全属性）")]
    public List<ElementList> 対象属性 = new List<ElementList>();

    [Header("変更する項目")]
    public bool 技名;
    public bool エネルギー;
    public bool パワー;
    public bool 技属性;
    public bool 技の対象;
    public bool 技の対象プレイヤー;
    public bool 技の対象選択;
    public bool 説明文;

    [Header("変更後の値")]
    public string 変更後技名;
    public int 変更後エネルギー;
    public int 変更後パワー;
    public ElementList 変更後属性;
    public TargetList 変更後対象;
    public PlayerType 変更後対象側 = PlayerType.プレイヤー;
    public SelectTarget 変更後対象選択 = new SelectTarget();

    [TextArea]
    public string 変更後説明文;

    public override TargetList GetEffectTarget() => 効果対象;
    public override SearchCriteria GetTargetCriteria() => 対象条件;
    public override IReadOnlyList<ISkillCondition> GetTargetState() => 対象状態;

    public override async Task ActivateEffect(SkillContext context)
    {
        var targets = KeepContextCard(
            await CollectTargetsAsync(
                効果対象, 対象側, 対象条件, context, 対象状態),
            context);

        foreach (var card in targets)
        {
            if (card == null || card.cardKind != CardKind.バトラー)
                continue;

            if (対象条件 != null && !card.MeetsCriteria(対象条件, context))
                continue;

            if (!ConditionsMet(対象状態, card))
                continue;

            if (!(card.model is BattlerModel battler))
                continue;

            bool apply1 = AppliesToMoveElement(
                適用先, 対象属性, battler.moveElement1, 0);

            bool apply2 = AppliesToMoveElement(
                適用先, 対象属性, battler.moveElement2, 1);

            if (!apply1 && !apply2)
                continue;

            if (apply1)
                ChangeMove(card, battler, 0);

            if (apply2)
                ChangeMove(card, battler, 1);

            card.RecalculateModifiedPowers();
            card.UpdateDisplay();

            Debug.Log(
                $"{card.cardName} の技情報を変更しました。技1:{apply1} 技2:{apply2}");
        }
    }

    void ChangeMove(CardController card, BattlerModel battler, int moveIndex)
    {
        if (moveIndex == 0)
        {
            if (技名)
                battler.moveName1 = 変更後技名;

            if (エネルギー)
            {
                battler.energy1 = 変更後エネルギー;
                card.energy1 = 変更後エネルギー;
            }

            if (パワー)
            {
                battler.power1 = 変更後パワー;
                card.basePower1 = 変更後パワー;
            }

            if (技属性)
            {
                battler.moveElement1 = 変更後属性;
                card.moveElement1 = 変更後属性;
            }

            if (技の対象)
            {
                battler.target1 = 変更後対象;
                card.target1 = 変更後対象;
            }

            if (技の対象プレイヤー)
            {
                battler.対象側1 = 変更後対象側;
                card.対象側1 = 変更後対象側;
            }

            if (技の対象選択)
                battler.対象選択1 = SelectTarget.Copy(変更後対象選択);

            if (説明文)
                battler.description1 = 変更後説明文;
        }
        else
        {
            if (技名)
                battler.moveName2 = 変更後技名;

            if (エネルギー)
            {
                battler.energy2 = 変更後エネルギー;
                card.energy2 = 変更後エネルギー;
            }

            if (パワー)
            {
                battler.power2 = 変更後パワー;
                card.basePower2 = 変更後パワー;
            }

            if (技属性)
            {
                battler.moveElement2 = 変更後属性;
                card.moveElement2 = 変更後属性;
            }

            if (技の対象)
            {
                battler.target2 = 変更後対象;
                card.target2 = 変更後対象;
            }

            if (技の対象プレイヤー)
            {
                battler.対象側2 = 変更後対象側;
                card.対象側2 = 変更後対象側;
            }

            if (技の対象選択)
                battler.対象選択2 = SelectTarget.Copy(変更後対象選択);

            if (説明文)
                battler.description2 = 変更後説明文;
        }
    }

    protected override void CopyPolymorphicFieldsTo(SkillDB clone)
    {
        if (clone is ChangeMoveSkill change)
        {
            change.対象状態 = 対象状態 != null
                ? new List<ISkillCondition>(対象状態)
                : new List<ISkillCondition>();

            change.対象属性 = 対象属性 != null
                ? new List<ElementList>(対象属性)
                : new List<ElementList>();

            change.変更後対象選択 = SelectTarget.Copy(変更後対象選択);
        }
    }
}
