using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

[Serializable]
[SkillDisplayName("登場")]
public class SpecialSummonSkill : SkillDB
{
    public List<CardZoneType> 参照ゾーンリスト = new List<CardZoneType>();
    public SearchCriteria 対象条件;

    public override async Task ActivateEffect(SkillContext context)
    {
        // 複合スキルの中にある登場は、親の発動条件では見ない。ここで自分の条件を見る。
        if (!MeetsOwnConditions(context))
        {
            Debug.Log("SpecialSummonSkill: 発動条件を満たしていないため登場しません。");
            return;
        }

        var cond = 対象条件;

        // --- 1) すべての選択候補をリストアップして整理 ---
        var existingCandidates = new List<CardController>();
        var deckCandidates = new List<DeckCard>();

        foreach (var zone in 参照ゾーンリスト)
        {
            if (CardZones.IsDeck(zone))
            {
                deckCandidates.AddRange(CardZones.Deck(zone, true).Where(d => SearchCriteria.AllowsDeck(cond, d, context)));
                continue;
            }

            if (!IsHandOrGrave(zone))
            {
                Debug.LogWarning($"SpecialSummonSkill: 未対応のゾーン {zone}");
                continue;
            }

            existingCandidates.AddRange(
                CardZones.Absolute(zone)
                    .GetComponentsInChildren<CardController>()
                    .Where(c => c.MeetsCriteria(cond, context)));
        }

        // --- 2) 同一カード制限のチェック：候補リストから同一カードを排除 ---
        // 既存カードの候補から、同一カード制限により場に出せないカードを排除
        existingCandidates = existingCandidates
            .Where(c => !UniqueCardRestrictionHelper.IsRestrictedByUniqueCardRule(c))
            .ToList();

        // デッキカードの候補から、同一カード制限により場に出せないカードを排除
        bool isMyCard = context.発動カード != null && context.発動カード.isMyCard;
        deckCandidates = deckCandidates
            .Where(d => !UniqueCardRestrictionHelper.IsDeckCardRestrictedByUniqueCardRule(d, isMyCard))
            .ToList();

        // --- 3) 候補がないチェック ---
        if (existingCandidates.Count == 0 && deckCandidates.Count == 0)
        {
            Debug.Log("SpecialSummonSkill: 対象カードが見つかりません。");
            return;
        }

        // --- 4) 候補が1枚しかない場合は自動選択 ---
        SelectionItem sel;
        if (existingCandidates.Count + deckCandidates.Count == 1)
        {
            // 候補が1枚しかない場合は自動選択
            if (existingCandidates.Count == 1)
            {
                sel = new SelectionItem
                {
                    source = SelectionItem.SourceType.Existing,
                    existingCard = existingCandidates[0]
                };
            }
            else
            {
                sel = new SelectionItem
                {
                    source = SelectionItem.SourceType.Deck,
                    deckData = deckCandidates[0]
                };
            }
        }
        else
        {
            // --- 5) UI で選択（既存カードがあれば優先） ---
            var selections = await CardSelectionUI.Instance
                .ShowAsync(existingCandidates, deckCandidates, 1);
            if (selections == null || selections.Count == 0) return;

            sel = selections[0];
        }
        CardController toSummon = null;

        // --- 6) 選択結果に応じて本格処理 ---
        SummonSource summonSource = SummonSource.手札; // デフォルトは手札

        if (sel.source == SelectionItem.SourceType.Existing)
        {
            toSummon = sel.existingCard;
            // 既存カードの場合は召喚元を判定
            if (toSummon.transform.parent == BattleManager.Instance.pGraveZone ||
                toSummon.transform.parent == BattleManager.Instance.eGraveZone)
            {
                summonSource = SummonSource.デッキ;
            }
            else if (toSummon.transform.parent == BattleManager.Instance.pHandZone ||
                     toSummon.transform.parent == BattleManager.Instance.eHandZone)
            {
                summonSource = SummonSource.手札;
            }
        }
        else // Deck
        {
            var pick = sel.deckData;
            summonSource = SummonSource.墓地;

            // デッキリストから削除
            if (BattleManager.Instance.pBattleDeck.Contains(pick)) BattleManager.Instance.pBattleDeck.Remove(pick);
            else if (BattleManager.Instance.eBattleDeck.Contains(pick)) BattleManager.Instance.eBattleDeck.Remove(pick);

            // 親ゾーン
            var parentZone = context.発動カード.isPlayerCard
                ? BattleManager.Instance.pDeckZone
                : BattleManager.Instance.eDeckZone;

            // カードを作成
            toSummon = BattleManager.Instance.CreateCard(
                pick.カード種類,
                pick.カードID,
                parentZone
            );
        }

        // --- 7) 選択されたカードが同一カード制限により場に出せないか再チェック（念のため） ---
        if (toSummon != null && UniqueCardRestrictionHelper.IsRestrictedByUniqueCardRule(toSummon))
        {
            Debug.LogWarning($"SpecialSummonSkill: {toSummon.cardName}は同一カード制限により場に出せません。");
            return;
        }

        // --- 8) SummonRoutine で召喚処理 ---
        if (toSummon != null)
        {
            // 適切なバトラーゾーンを選択
            var targetZone = toSummon.isMyCard 
                ? BattleManager.Instance.pBattlerZone 
                : BattleManager.Instance.eBattlerZone;
            
            await BattleManager.Instance.SummonCard(toSummon, targetZone, playVoice: true, deductCost: false, summonSource: summonSource, specialSummon: true);
            context?.SetProcessedCards(new[] { toSummon });
        }

        BattleManager.Instance.UpdateDeckUI();
    }

    bool MeetsOwnConditions(SkillContext context)
    {
        if (条件 == null || 条件.Count == 0)
            return true;

        foreach (var condition in 条件)
        {
            if (condition == null)
                continue;
            if (context == null || !condition.IsConditionMet(context))
                return false;
        }

        return true;
    }

    static bool IsHandOrGrave(CardZoneType zone)
    {
        return zone == CardZoneType.プレイヤー手札
            || zone == CardZoneType.プレイヤー墓地
            || zone == CardZoneType.相手手札
            || zone == CardZoneType.相手墓地;
    }
}
