using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

[System.Serializable]
[SkillDisplayName("バウンス")]
public class ReturnToHandSkill : SkillDB
{
    [SerializeField] public TargetList 効果対象;
    [SerializeField] public PlayerType 対象側 = PlayerType.プレイヤー;
    public SearchCriteria 対象条件;

    public override TargetList GetEffectTarget() => 効果対象;
    public override SearchCriteria GetTargetCriteria() => 対象条件;

    public override async Task ActivateEffect(SkillContext context)
    {
        var toReturn = await CollectTargetsAsync(効果対象, 対象側, 対象条件, context);

        // ③ それぞれのカードを手札に（アニメーション付き）
        bool addedToPlayer = false;
        bool addedToEnemy = false;
        foreach (var card in toReturn)
        {
            // 元の場所を記録
            RectTransform originalZone = card.transform.parent as RectTransform;
            
            if (card.cardKind == CardKind.バトラー || card.cardKind == CardKind.フィールド)
            {
                card.curHp = card.cardKind == CardKind.バトラー
                    ? ((BattlerModel)card.model).maxHp
                    : ((FieldModel)card.model).maxHp;
                card.passiveEffects.Clear();
                card.ClearStatusAilments();
                card.ResetBonuses();
                if (card.cardKind == CardKind.バトラー)
                    card.ResetMoveElements();
                card.RecalculateModifiedPowers();
                card.hasUsedMove = false;
                card.GetComponent<BattleCardView>().UpdateCanUseMovePanel();
            }

            // プレイヤー or 敵の手札に戻す（アニメーション付き）
            if (card.isPlayerCard)
            {
                // アニメーションで手札に移動
                await CardAnimationManager.Instance.AnimateReturnToHand(
                    card, 
                    originalZone, 
                    BattleManager.Instance.pHandZone as RectTransform
                );
                // 表示を更新
                card.UpdateDisplay();
                // BattleCardViewの表示も更新
                var battleCardView = card.GetComponent<BattleCardView>();
                if (battleCardView != null)
                {
                    battleCardView.Show(card.model);
                }
                addedToPlayer = true;
            }
            else
            {
                // 敵の場合は Destroy→リスト再構築する
                // CardControllerからDeckCardに必要な全ての情報を取得
                CardKind cardKindToReturn = card.cardKind;
                int cardIdToReturn = card.cardID;
                int cardCostToReturn = card.cost; // CardControllerから実際のコストを取得
                List<CardCategoryList> cardCategoryToReturn = card.category; // CardControllerから実際のカテゴリを取得
                CardTypeList cardTypeToReturn = card.type;

                Object.Destroy(card.gameObject); // フィールドのCardControllerオブジェクトを削除

                // DeckCardのコンストラクタを使用して新しいインスタンスを作成し、敵の手札リストに追加
                BattleManager.Instance.enemyHand.Add(new DeckCard(
                    cardKindToReturn,
                    cardIdToReturn,
                    cardCostToReturn,
                    cardCategoryToReturn,
                    cardTypeToReturn
                ));

                addedToEnemy = true;
            }
        }

        if (addedToPlayer || addedToEnemy)
            BattleManager.Instance.ShuffleHandAfterAddition(addedToPlayer, addedToEnemy);
    }
}
