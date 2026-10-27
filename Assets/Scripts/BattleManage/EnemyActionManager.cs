using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// オフライン時のCPU行動を管理するクラス
/// </summary>
public class EnemyActionManager : MonoBehaviour
{
    public static EnemyActionManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }

    /// <summary>
    /// エネミーのメインフェイズ自動処理
    /// </summary>
    public async Task ExecuteEnemyMainPhase()
    {
        await Task.Delay(300);

        Debug.Log("エネミー メインフェイズ処理（自動）");
        await EnemyUseFieldCardAsync();
        await EnemySummonCardAsync();

        await Task.Delay(300);
    }

    /// <summary>
    /// エネミーのバトルフェイズ自動処理
    /// </summary>
    public async Task ExecuteEnemyBattlePhase()
    {
        await Task.Delay(300);

        Debug.Log("エネミー バトルフェイズ処理（自動）");

        while (true)
        {
            CardController attacker = SelectBestEnemyAttacker();
            if (attacker == null)
                break;

            await EnemyUseMove(attacker);
            await Task.Delay(500);
        }

        await Task.Delay(300);
    }

    /// <summary>
    /// 相手手札の印刷コストに、Fゾーンの永続効果で付いた補正を足した支払いコスト。
    /// </summary>
    int ModifiedHandCost(DeckCard deck)
    {
        if (deck == null)
            return 0;

        CardController match = FindEnemyHandCard(deck);
        if (match != null)
            return match.GetModifiedCost();

        int printed = Mathf.Max(0, deck.コスト);
        var battle = BattleManager.Instance;
        if (battle == null)
            return printed;
        return Mathf.Max(0, printed + battle.GetPlayCostAdjustment(false, deck.カード種類));
    }

    /// <summary>
    /// メインフェイズ開始時、手札のフィールドカードを1枚ランダムに使用する。
    /// Fゾーンが空で、コストを払えるカードだけが候補。
    /// </summary>
    async Task EnemyUseFieldCardAsync()
    {
        try
        {
            var battle = BattleManager.Instance;
            if (battle?.eFieldZone == null || battle.eFieldZone.childCount > 0)
                return;

            PersistentEffectManager.Instance?.RecalculateImmediately();

            List<DeckCard> fieldCards = battle.enemyHand.FindAll(CanPlayFieldFromHand);
            if (fieldCards.Count == 0)
                return;

            var chosen = fieldCards[UnityEngine.Random.Range(0, fieldCards.Count)];
            CardController card = EnsureEnemyHandCard(chosen);
            if (card == null)
            {
                Debug.LogError("EnemyUseFieldCardAsync: カードが見つかりませんでした");
                return;
            }

            if (CannotNormalSummonSkill.Blocks(card) || UniqueCardRestrictionHelper.IsRestrictedByUniqueCardRule(card))
                return;

            int pay = card.GetModifiedCost();
            if (battle.eResources.Cost.currentCost < pay)
                return;

            battle.eResources.Cost.TakeCost(pay);
            battle.enemyHand.Remove(chosen);
            await PlayFromEnemyHand(card, battle.eFieldZone);
        }
        catch (Exception ex)
        {
            Debug.LogError($"EnemyUseFieldCardAsync でエラー: {ex.Message}");
        }
    }

    bool CanPlayFieldFromHand(DeckCard deck)
    {
        if (deck == null || deck.カード種類 != CardKind.フィールド)
            return false;
        if (UniqueCardRestrictionHelper.IsDeckCardRestrictedByUniqueCardRule(deck, false))
            return false;
        if (BattleManager.Instance.eResources.Cost.currentCost < ModifiedHandCost(deck))
            return false;

        CardController card = FindEnemyHandCard(deck);
        return card == null || (!CannotNormalSummonSkill.Blocks(card) && !UniqueCardRestrictionHelper.IsRestrictedByUniqueCardRule(card));
    }

    CardController FindEnemyHandCard(DeckCard deck)
    {
        if (deck == null || BattleManager.Instance?.eHandZone == null)
            return null;

        return BattleManager.Instance.eHandZone.GetComponentsInChildren<CardController>()
            .FirstOrDefault(card => card != null && card.cardKind == deck.カード種類 && card.cardID == deck.カードID);
    }

    /// <summary>
    /// 手札表示が無いときだけ、その1枚を生成して返す。
    /// </summary>
    CardController EnsureEnemyHandCard(DeckCard deck)
    {
        CardController card = FindEnemyHandCard(deck);
        if (card != null)
            return card;

        var battle = BattleManager.Instance;
        card = battle.CreateCard(deck.カード種類, deck.カードID, battle.eHandZone);
        card.GetComponent<BattleCardView>()?.ShowCardBack();
        return card;
    }

    async Task PlayFromEnemyHand(CardController card, Transform zone)
    {
        await BattleManager.Instance.SummonRoutine(card, zone, playVoice: true, SummonSource.手札);
        if (card != null && card.transform != null)
            card.UpdateDisplay();
        await Task.Delay(100);
    }

    /// <summary>
    /// 払えるバトラーのうちコストが最も高いものを、Bゾーンが埋まるか出せなくなるまで出す。
    /// </summary>
    async Task EnemySummonCardAsync()
    {
        try
        {
            var battle = BattleManager.Instance;
            while (battle.eBattlerZone.GetComponentsInChildren<CardController>().Length < 5)
            {
                PersistentEffectManager.Instance?.RecalculateImmediately();

                int available = battle.eResources.Cost.currentCost;
                var affordable = battle.enemyHand
                    .Where(card => card != null && card.カード種類 == CardKind.バトラー && !CannotNormalSummonSkill.Blocks(card))
                    .Select(card => (card, cost: ModifiedHandCost(card)))
                    .Where(entry => entry.cost <= available)
                    .ToList();
                if (affordable.Count == 0)
                    return;

                int highestCost = affordable.Max(entry => entry.cost);
                var candidates = affordable.Where(entry => entry.cost == highestCost).Select(entry => entry.card).ToList();
                var chosen = candidates[UnityEngine.Random.Range(0, candidates.Count)];

                battle.eResources.Cost.TakeCost(highestCost);
                battle.enemyHand.Remove(chosen);

                CardController card = EnsureEnemyHandCard(chosen);
                if (card == null)
                {
                    Debug.LogError("EnemySummonCardAsync: カードが見つかりませんでした");
                    return;
                }

                await PlayFromEnemyHand(card, battle.eBattlerZone);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"EnemySummonCardAsync でエラー: {ex.Message}");
        }
    }

    /// <summary>
    /// まだ技を使えるバトラーのうち、出せる技のダメージが最も高いものを選ぶ。
    /// </summary>
    CardController SelectBestEnemyAttacker()
    {
        var battle = BattleManager.Instance;
        if (battle.pResources.Hp.currentHp == 0)
            return null;

        var best = new List<CardController>();
        int maxDamage = 0;
        foreach (var card in battle.eBattlerZone.GetComponentsInChildren<CardController>())
        {
            if (card.hasUsedMove || card.IsActionBlocked || card.cardKind != CardKind.バトラー)
                continue;

            int moveIndex = ChooseEnemyMoveIndex(card);
            if (moveIndex < 0)
                continue;

            int damage = card.ModifyOutgoingMoveDamage(battle.GetMoveBaseDamage(card, moveIndex));
            if (damage == 0)
                continue;

            if (damage > maxDamage)
            {
                maxDamage = damage;
                best.Clear();
            }

            if (damage == maxDamage)
                best.Add(card);
        }

        if (best.Count == 0)
            return null;
        return best[UnityEngine.Random.Range(0, best.Count)];
    }

    /// <summary>
    /// 払える技のうち、技2を優先する。バトラー対象で対象が一人もいない技は選ばない。
    /// </summary>
    int ChooseEnemyMoveIndex(CardController card)
    {
        var battle = BattleManager.Instance;
        if (card == null || battle == null)
            return -1;

        int energy = battle.eResources.Energy.currentEnergy;
        if (energy >= card.GetMoveEnergyCost(1) && MoveTargetEvasion.HasLegalTarget(card, 1))
            return 1;
        if (energy >= card.GetMoveEnergyCost(0) && MoveTargetEvasion.HasLegalTarget(card, 0))
            return 0;
        return -1;
    }

    /// <summary>
    /// エネミーが技を使う処理
    /// </summary>
    async Task EnemyUseMove(CardController enemyCard)
    {
        if (enemyCard == null || enemyCard.IsActionBlocked || enemyCard.cardKind != CardKind.バトラー)
            return;

        var battle = BattleManager.Instance;
        int moveIndex = ChooseEnemyMoveIndex(enemyCard);
        if (moveIndex < 0)
            return;

        int energyCost = enemyCard.GetMoveEnergyCost(moveIndex);
        if (energyCost > 0)
            battle.eResources.Energy.TakeEnergy(energyCost);

        bool moveHit = enemyCard.RollMoveHit();
        int extraHits = moveHit ? MultiHitSkill.RollExtraHits(enemyCard, moveIndex) : 0;
        battle.SetPendingMoveHit(moveHit, extraHits);

        var enemyBattler = (BattlerModel)enemyCard.model;
        TargetList targetType = enemyBattler.GetMoveTarget(moveIndex);
        PlayerType moveSide = enemyBattler.GetMoveSide(moveIndex);
        if (targetType == TargetList.自分)
        {
            await battle.ResolveSelfTargetMove(enemyCard, moveIndex, payEnergy: false);
            return;
        }

        if (MoveTargetEvasion.IsConfusedMove(enemyCard, moveIndex))
        {
            if (MoveTargetEvasion.ListZoneBattlers(enemyCard, PlayerType.両方).Count > 0)
            {
                await ShowCpuTargetCandidates(MoveTargetEvasion.ListConfusionCandidates(enemyCard));
                await battle.ResolveConfusedMove(enemyCard, moveIndex, payEnergy: false);
                return;
            }
        }

        if (MoveTargetEvasion.IsMultiSelect(enemyCard, moveIndex))
        {
            await UseMultiTargetMove(enemyCard, moveIndex);
            return;
        }

        if (targetType == TargetList.バトラー全員)
        {
            var present = MoveTargetEvasion.ListZoneBattlers(enemyCard, moveSide);
            if (present.Count > 0)
            {
                var legal = MoveTargetEvasion.LegalBattlers(present, targetType, moveSide);
                if (legal.Count == 0)
                {
                    MarkMoveUsed(enemyCard);
                    return;
                }

                if (!await OpenMoveUseWindow(enemyCard, moveIndex))
                    return;

                await battle.ApplyDamageToAllEnemyBattlers(enemyCard, moveIndex, legal);
                return;
            }
        }

        if (MoveTargetEvasion.IsSelectableBattlerTarget(targetType))
        {
            var legalTargets = MoveTargetEvasion.ListLegalTargets(enemyCard, moveIndex);
            if (legalTargets.Count > 0)
            {
                await ShowCpuTargetCandidates(legalTargets);
                var picked = legalTargets[UnityEngine.Random.Range(0, legalTargets.Count)];
                await battle.SelectTargetRoutine(picked, enemyCard, moveIndex);
                return;
            }
        }
        else if (targetType != TargetList.バトラー全員)
        {
            var playerCards = battle.pBattlerZone.GetComponentsInChildren<CardController>();
            CardController target = await PickSingleBattlerTarget(playerCards, targetType, moveSide);
            if (target != null)
            {
                await battle.SelectTargetRoutine(target, enemyCard, moveIndex);
                return;
            }
        }

        var fieldCards = battle.pFieldZone.GetComponentsInChildren<CardController>();
        if (fieldCards.Length > 0 && fieldCards[0].cardKind == CardKind.フィールド)
        {
            await battle.SelectTargetRoutine(fieldCards[0], enemyCard, moveIndex);
            return;
        }

        await AttackPlayerHp(enemyCard, moveIndex);
    }

    async Task UseMultiTargetMove(CardController enemyCard, int moveIndex)
    {
        var select = MoveTargetEvasion.MoveSelect(enemyCard, moveIndex);
        var legal = MoveTargetEvasion.ListLegalTargets(enemyCard, moveIndex);
        int pickCount = select.選択方式 == TargetCountMode.まで
            ? Mathf.Min(select.Count, legal.Count)
            : select.Count;
        var picked = PickRandomTargets(legal, pickCount);
        if (picked.Count < select.MinimumCount)
        {
            MarkMoveUsed(enemyCard);
            return;
        }

        await ShowCpuTargetCandidates(legal);
        await BattleManager.Instance.ResolveSelectedMoveTargets(enemyCard, moveIndex, picked, payEnergy: false);
    }

    async Task<CardController> PickSingleBattlerTarget(CardController[] playerCards, TargetList targetType, PlayerType side)
    {
        if (playerCards.Length == 0)
            return null;

        var candidates = MoveTargetEvasion.IsBattlerTarget(targetType)
            ? MoveTargetEvasion.LegalBattlers(playerCards, targetType, side)
            : playerCards.ToList();
        if (candidates.Count == 0)
            return null;

        await ShowCpuTargetCandidates(candidates);
        return candidates[UnityEngine.Random.Range(0, candidates.Count)];
    }

    async Task AttackPlayerHp(CardController enemyCard, int moveIndex)
    {
        var battle = BattleManager.Instance;
        if (!await OpenMoveUseWindow(enemyCard, moveIndex))
            return;

        if (!battle.ConsumeMoveHit(enemyCard))
        {
            Vector3 missPos = battle.pFieldZone != null
                ? battle.pFieldZone.position
                : enemyCard.transform.position;
            await battle.FinishMissedMove(enemyCard, moveIndex, null, missPos);
            return;
        }

        await CardAnimationManager.Instance.PlayMoveImpact(enemyCard.transform, battle.pFieldZone, null);

        int directDamageBase = battle.GetMoveBaseDamage(enemyCard, moveIndex);
        int protectedDamage = battle.ApplyHPProtectionToDirectHP(enemyCard, moveIndex, directDamageBase, battle.pResources.Hp.currentHp);
        int finalDamage = enemyCard.ModifyOutgoingMoveDamage(protectedDamage);

        int hpBefore = battle.pResources.Hp.currentHp;
        await battle.ShowDamageEffectToPlayerHP(finalDamage, enemyCard.GetDamageEffectColor(moveIndex, finalDamage));
        await Task.Delay(900);

        battle.pResources.Hp.LoseHp(finalDamage);
        await battle.PlayExtraDirectHits(enemyCard, moveIndex);
        if (hpBefore > 0 && battle.pResources.Hp.currentHp <= 0)
            battle.pResources.Hp.NotifyHpDepleted();
        battle.ClearTargetHighlights();
        await battle.ProcessMoveUsedAfterSkills(enemyCard, moveIndex);
        MarkMoveUsed(enemyCard);
    }

    /// <summary>
    /// 「相手の技使用時」のあと、キャンセルでなければCPUの技使用時を処理する。
    /// キャンセルしたときは false。
    /// </summary>
    async Task<bool> OpenMoveUseWindow(CardController attacker, int moveIndex)
    {
        var battle = BattleManager.Instance;
        await battle.TriggerOpponentMoveUsedSkills(attacker, moveIndex, null);
        if (battle.IsApplyOpponentMoveCancellation(attacker))
        {
            battle.HandleMoveCancelled(attacker);
            return false;
        }

        var specific = moveIndex == 0 ? SkillTrigger.技1使用時 : SkillTrigger.技2使用時;
        await battle.ProcessSkillTriggerAndWait(specific, attacker, null, moveIndex: moveIndex);
        await battle.ProcessSkillTriggerAndWait(SkillTrigger.技使用時, attacker, null, moveIndex: moveIndex);
        return true;
    }

    static void MarkMoveUsed(CardController card)
    {
        card.hasUsedMove = true;
        card.GetComponent<BattleCardView>()?.UpdateCanUseMovePanel();
    }

    /// <summary>
    /// 候補を全部 TargetPanel にして少し見せる。
    /// そのあと解決側が選んだカードだけを残し、ダメージ表示のあとまで点灯したままにする。
    /// </summary>
    static async Task ShowCpuTargetCandidates(IEnumerable<CardController> candidates)
    {
        if (BattleManager.Instance == null || candidates == null)
            return;

        var list = candidates.Where(card => card != null).Distinct().ToList();
        if (list.Count == 0)
            return;

        BattleManager.Instance.ShowSpecificTargetHighlights(list);
        await Task.Delay(150);
    }

    static List<CardController> PickRandomTargets(List<CardController> candidates, int count)
    {
        var pool = new List<CardController>();
        if (candidates != null)
            pool.AddRange(candidates);

        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            var swap = pool[i];
            pool[i] = pool[j];
            pool[j] = swap;
        }

        if (count < 0)
            count = 0;
        if (pool.Count > count)
            pool.RemoveRange(count, pool.Count - count);
        return pool;
    }
}
