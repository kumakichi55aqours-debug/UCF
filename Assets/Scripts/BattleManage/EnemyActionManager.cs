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

    BattleManager battle;

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
        {
            Destroy(gameObject);
            return;
        }

        battle = BattleManager.Instance;
    }

    /// <summary>
    /// エネミーのメインフェイズ自動処理
    /// </summary>
    public async Task ExecuteEnemyMainPhase()
    {
        if (battle == null) return;

        await Task.Delay(300);

        await EnemyUseFieldCardAsync();
        await EnemyUseTacticsAsync();
        await EnemySummonCardAsync();

        await Task.Delay(300);
    }

    /// <summary>
    /// エネミーのバトルフェイズ自動処理
    /// </summary>
    public async Task ExecuteEnemyBattlePhase()
    {
        if (battle == null) return;

        await Task.Delay(300);

        while (true)
        {
            CardController attacker = SelectBestEnemyAttacker();
            if (attacker == null)
                break;

            await EnemyUseMove(attacker);
            await Task.Delay(500);
        }

        await EnemyUseTacticsAsync(card => PhaseCondition.CardRequires(card, TurnPhase.バトルフェイズ));

        await Task.Delay(300);
    }

    /// <summary>
    /// 相手手札の印刷コストに、Fゾーンの永続効果で付いた補正を足した支払いコスト。
    /// </summary>
    int ModifiedHandCost(DeckCard deck)
    {
        if (deck == null || battle == null)
            return 0;

        CardController match = FindEnemyHandCard(deck);
        if (match != null)
            return match.GetModifiedCost();

        int printed = Mathf.Max(0, deck.コスト);
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
        if (battle == null || battle?.eFieldZone == null || battle.eFieldZone.childCount > 0)
            return;

        PersistentEffectManager.Instance?.RecalculateImmediately();

        List<DeckCard> fieldCards = battle.enemyHand.FindAll(CanPlayFieldFromHand);
        if (fieldCards.Count == 0)
            return;

        var chosen = fieldCards[UnityEngine.Random.Range(0, fieldCards.Count)];
        CardController card = EnsureEnemyHandCard(chosen);
        if (card == null)
            return;

        if (CannotNormalSummonSkill.Blocks(card) || UniqueCardRestrictionHelper.IsRestrictedByUniqueCardRule(card))
            return;

        int pay = card.GetModifiedCost();
        if (battle.eResources.Cost.currentCost < pay)
            return;

        battle.eResources.Cost.TakeCost(pay);
        battle.enemyHand.Remove(chosen);
        await PlayFromEnemyHand(card, battle.eFieldZone);
        await WaitWhileSkillsResolve();
    }

    bool CanPlayFieldFromHand(DeckCard deck)
    {
        if (deck == null || deck.カード種類 != CardKind.フィールド)
            return false;
        if (UniqueCardRestrictionHelper.IsDeckCardRestrictedByUniqueCardRule(deck, false))
            return false;
        if (battle.eResources.Cost.currentCost < ModifiedHandCost(deck))
            return false;

        CardController card = FindEnemyHandCard(deck);
        return card == null || (!CannotNormalSummonSkill.Blocks(card) && !UniqueCardRestrictionHelper.IsRestrictedByUniqueCardRule(card));
    }

    /// <summary>
    /// 払える作戦を、出せなくなるまで使う。
    /// コスト0は他の手札より高くなくても使う。
    /// コスト1以上は、手札の支払いコストが最も高いときだけ使う。同コストのバトラーがいればバトラーを優先して使わない。
    /// </summary>
    async Task EnemyUseTacticsAsync(Func<CardController, bool> include = null)
    {
        if (battle == null) return;

        while (true)
        {
            PersistentEffectManager.Instance?.RecalculateImmediately();

            DeckCard chosen = ChooseEnemyTactics(battle, include);
            if (chosen == null)
                return;

            CardController card = EnsureEnemyHandCard(chosen);
            if (card == null)
                return;

            int pay = card.GetModifiedCost();
            if (battle.eResources.Cost.currentCost < pay)
                return;

            battle.eResources.Cost.TakeCost(pay);
            battle.enemyHand.Remove(chosen);

            await PlayFromEnemyHand(card, battle.eTacticsZone);
            await WaitForPlayedTactics(card);
        }
    }

    /// <summary>
    /// 今の手札から、CPUが使う作戦を1枚選ぶ。無ければ null。
    /// </summary>
    DeckCard ChooseEnemyTactics(BattleManager battle, Func<CardController, bool> include = null)
    {
        var hand = battle?.enemyHand;
        if (battle == null || hand == null || hand.Count == 0)
            return null;

        var playable = new List<DeckCard>();
        var free = new List<DeckCard>();
        foreach (var deck in hand)
        {
            if (!CanPlayEnemyTactics(deck))
                continue;
            if (include != null && !include(FindEnemyHandCard(deck)))
                continue;

            playable.Add(deck);
            if (ModifiedHandCost(deck) <= 0)
                free.Add(deck);
        }

        if (free.Count > 0)
            return free[UnityEngine.Random.Range(0, free.Count)];
        if (playable.Count == 0)
            return null;

        int availableCost = battle.eResources.Cost.currentCost;

        int highest = int.MinValue;
        bool butlerAtHighest = false;
        foreach (var deck in hand)
        {
            if (deck == null)
                continue;

            int cost = ModifiedHandCost(deck);

            if (cost > availableCost) 
                continue;

            if (cost > highest)
            {
                highest = cost;
                butlerAtHighest = deck.カード種類 == CardKind.バトラー;
            }
            else if (cost == highest && deck.カード種類 == CardKind.バトラー)
            {
                butlerAtHighest = true;
            }
        }

        if (highest <= 0 || butlerAtHighest)
            return null;

        var top = new List<DeckCard>();
        foreach (var deck in playable)
        {
            if (ModifiedHandCost(deck) == highest)
                top.Add(deck);
        }

        if (top.Count == 0)
            return null;
        return top[UnityEngine.Random.Range(0, top.Count)];
    }

    /// <summary>
    /// 手札から通常の作戦として出せるか。支払えて、手札の登場時スキルがあるとき。
    /// </summary>
    bool CanPlayEnemyTactics(DeckCard deck)
    {
        if (deck == null || deck.カード種類 != CardKind.作戦)
            return false;
        if (battle != null && !battle.TacticsZoneHasRoom(false))
            return false;
        if (UniqueCardRestrictionHelper.IsDeckCardRestrictedByUniqueCardRule(deck, false))
            return false;

        if (battle == null || battle.eResources == null || battle.eResources.Cost == null)
            return false;
        if (battle.eResources.Cost.currentCost < ModifiedHandCost(deck))
            return false;

        CardController card = FindEnemyHandCard(deck);
        if (card == null || card.IsActionBlocked)
            return false;
        if (NegateZoneEffectSkill.Blocks(card) || UniqueCardRestrictionHelper.IsRestrictedByUniqueCardRule(card))
            return false;

        return AppearanceCanResolve(card);
    }

    /// <summary>
    /// 登場時スキルは、手札を離れたあとに条件を見る。
    /// 判定中だけ手札から外し、解決時と同じ条件と対象の有無で出せるか決める。
    /// </summary>
    bool AppearanceCanResolve(CardController card)
    {
        if (battle == null || card == null || card.skills == null)
            return false;

        Transform parent = card.transform.parent;
        int sibling = card.transform.GetSiblingIndex();
        Transform hold = battle.mainCanvas != null
            ? battle.mainCanvas.transform
            : null;
        card.transform.SetParent(hold, true);
        try
        {
            var ctx = SkillContext.WithAttackTarget(card, null);
            foreach (var skill in card.skills)
            {
                if (skill == null || !skill.CanUseAt(ActiveTiming.登場時, ActiveLocation.手札, ctx))
                    continue;
                if (!ConditionsHold(skill, ctx) || !EffectCanResolve(skill, card, ctx))
                    continue;
                return true;
            }

            return false;
        }
        finally
        {
            card.transform.SetParent(parent, true);
            if (parent != null)
                card.transform.SetSiblingIndex(sibling);
        }
    }

    static bool ConditionsHold(SkillDB skill, SkillContext ctx)
    {
        if (skill?.条件 == null || skill.条件.Count == 0)
            return true;

        for (int i = 0; i < skill.条件.Count; i++)
        {
            var condition = skill.条件[i];
            if (condition == null || ctx == null || !condition.IsConditionMet(ctx))
                return false;
        }

        return true;
    }

    bool EffectCanResolve(SkillDB skill, CardController source, SkillContext ctx)
    {
        if (skill == null)
            return false;

        if (skill is CompositeSkill composite)
        {
            if (composite.subSkills == null)
                return true;

            foreach (var sub in composite.subSkills)
            {
                if (sub == null)
                    continue;
                if (HasOwnConditions(sub) && !(sub is ConditionalSkill))
                    continue;
                if (!EffectCanResolve(sub, source, ctx))
                    return false;
            }

            return true;
        }

        if (skill is ConditionalSkill branch)
        {
            bool met = ConditionsHold(branch, ctx);
            var chosen = met ? branch.成立時スキル : branch.不成立時スキル;
            if (chosen == null || chosen.Count == 0)
                return true;

            foreach (var sub in chosen)
            {
                if (sub != null && !EffectCanResolve(sub, source, ctx))
                    return false;
            }

            return true;
        }

        if (skill is SpecialSummonSkill summon)
            return HasSummonCandidate(summon, source, ctx);
        if (skill is ReturnWithGrantsSkill bounce)
            return bounce.HasReturnCandidate(source);

        return HasSelectedTargets(skill, source);
    }

    static bool HasOwnConditions(SkillDB skill)
    {
        return skill.条件 != null && skill.条件.Count > 0;
    }

    bool HasSelectedTargets(SkillDB skill, CardController source)
    {
        TargetList target = skill.GetEffectTarget();
        if (target != TargetList.バトラー1体
            && target != TargetList.バトラー全員
            && target != TargetList.フィールド1枚
            && target != TargetList.手札)
            return true;

        if (battle == null) return false;

        PlayerType side = PlayerType.プレイヤー;
        if (skill is GrantSkill grant)
            side = grant.対象側;
        else if (skill is DamageSkill damage)
            side = damage.対象側;
        else if (skill is BasePowerModifierSkill power)
            side = power.対象側;

        if (target == TargetList.バトラー全員)
        {
            bool ownerIsLocal = source != null && source.isMyCard;
            int count = 0;
            if (TargetScope.IncludesOwner(side))
                count += CountBattlers(CardZones.Of(CardZoneType.プレイヤーBゾーン, ownerIsLocal));
            if (TargetScope.IncludesOpponent(side))
                count += CountBattlers(CardZones.Of(CardZoneType.相手Bゾーン, ownerIsLocal));
            return count > 0;
        }

        var found = battle.ListSelectableSkillTargets(target, side, skill.GetTargetCriteria(), source, skill.GetTargetState());
        return found != null && found.Count > 0;
    }

    static int CountBattlers(Transform zone)
    {
        if (zone == null)
            return 0;

        int count = 0;
        var cards = zone.GetComponentsInChildren<CardController>();
        for (int i = 0; i < cards.Length; i++)
        {
            if (cards[i] != null && cards[i].cardKind == CardKind.バトラー)
                count++;
        }

        return count;
    }

    static bool HasSummonCandidate(SpecialSummonSkill summon, CardController source, SkillContext ctx)
    {
        if (summon == null || !ConditionsHold(summon, ctx) || summon.参照ゾーンリスト == null)
            return false;

        bool ownerIsLocal = source != null && source.isMyCard;
        foreach (var zone in summon.参照ゾーンリスト)
        {
            if (CardZones.IsDeck(zone))
            {
                var deck = CardZones.Deck(zone, ownerIsLocal);
                if (deck == null)
                    continue;

                foreach (var deckCard in deck)
                {
                    if (deckCard != null
                        && SearchCriteria.AllowsDeck(summon.対象条件, deckCard, ctx)
                        && !UniqueCardRestrictionHelper.IsDeckCardRestrictedByUniqueCardRule(deckCard, ownerIsLocal))
                        return true;
                }

                continue;
            }

            Transform zoneTransform = CardZones.Of(zone, ownerIsLocal);
            if (zoneTransform == null)
                continue;

            var cards = zoneTransform.GetComponentsInChildren<CardController>();
            for (int i = 0; i < cards.Length; i++)
            {
                var candidate = cards[i];
                if (candidate != null
                    && candidate.MeetsCriteria(summon.対象条件, ctx)
                    && !UniqueCardRestrictionHelper.IsRestrictedByUniqueCardRule(candidate))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 作戦を出したあと、効果と墓地送りが終わるまで待つ。
    /// </summary>
    async Task WaitForPlayedTactics(CardController card)
    {
        if (battle == null) return;

        await WaitWhileSkillsResolve();

        Transform tactics = battle != null ? battle.eTacticsZone : null;
        Transform grave = battle != null ? battle.eGraveZone : null;
        for (int i = 0; i < 80; i++)
        {
            if (card == null || card.transform == null)
                break;

            Transform parent = card.transform.parent;
            if (parent == grave || (tactics != null && parent != tactics))
                break;
            if (card.type == CardTypeList.強化 && battle != null && battle.HasLivingGrant(card))
                break;
            await Task.Delay(50);
        }

        await Task.Delay(250);
        await WaitWhileSkillsResolve();
    }

    async Task WaitWhileSkillsResolve()
    {
        if (battle == null) return;

        var chain = SkillChainManager.Instance;
        if (chain == null)
            return;

        await Task.Yield();
        while (chain.IsResolvingSkills)
            await Task.Delay(50);
    }

    CardController FindEnemyHandCard(DeckCard deck)
    {
        if (deck == null || battle.eHandZone == null)
            return null;

        return battle.eHandZone.GetComponentsInChildren<CardController>()
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

        card = battle.CreateCard(deck.カード種類, deck.カードID, battle.eHandZone);
        card.GetComponent<BattleCardView>()?.ShowCardBack();
        return card;
    }

    async Task PlayFromEnemyHand(CardController card, Transform zone)
    {
        await battle.SummonRoutine(card, zone, playVoice: true, SummonSource.手札);
        if (card != null && card.transform != null)
            card.UpdateDisplay();
        await Task.Delay(100);
    }

    /// <summary>
    /// 払えるバトラーのうちコストが最も高いものを、Bゾーンが埋まるか出せなくなるまで出す。
    /// </summary>
    async Task EnemySummonCardAsync()
    {
        if (battle == null) return;

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
                return;

            await PlayFromEnemyHand(card, battle.eBattlerZone);
        }
    }

    /// <summary>
    /// まだ技を使えるバトラーのうち、出せる技のダメージが最も高いものを選ぶ。
    /// </summary>
    CardController SelectBestEnemyAttacker()
    {
        if (battle == null || battle.pResources.Hp.currentHp == 0 || battle.curTurn == 1)
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
    /// 払える技のうち、技2を優先する。対象にできるバトラーがいない技は選ばない。対象ゾーンが空の「まで」は選ぶ。
    /// 付与スキルがないときだけの技は、付与があるあいだ選ばない。
    /// </summary>
    int ChooseEnemyMoveIndex(CardController card)
    {
        if (card == null || battle == null)
            return -1;

        int energy = battle.eResources.Energy.currentEnergy;
        if (EnemyMoveIsUsable(card, 1, energy))
            return 1;
        if (EnemyMoveIsUsable(card, 0, energy))
            return 0;
        return -1;
    }

    static bool EnemyMoveIsUsable(CardController card, int moveIndex, int energy)
    {
        return energy >= card.GetMoveEnergyCost(moveIndex)
            && !NoGrantMoveSkill.Blocks(card, moveIndex)
            && !MoveRestrictionSkill.Blocks(card, moveIndex)
            && MoveTargetEvasion.HasLegalTarget(card, moveIndex);
    }

    /// <summary>
    /// エネミーが技を使う処理
    /// </summary>
    async Task EnemyUseMove(CardController enemyCard)
    {
        if (battle == null) return;

        if (enemyCard == null || enemyCard.IsActionBlocked || enemyCard.cardKind != CardKind.バトラー)
            return;

        int moveIndex = ChooseEnemyMoveIndex(enemyCard);
        if (moveIndex < 0)
            return;

        bool moveHit = enemyCard.RollMoveHit();
        int extraHits = moveHit ? MultiHitSkill.RollExtraHits(enemyCard, moveIndex) : 0;
        battle.SetPendingMoveHit(moveHit, extraHits);

        var enemyBattler = (BattlerModel)enemyCard.model;
        TargetList targetType = enemyBattler.GetMoveTarget(moveIndex);
        PlayerType moveSide = enemyBattler.GetMoveSide(moveIndex);
        if (targetType == TargetList.自分)
        {
            await battle.ResolveSelfTargetMove(enemyCard, moveIndex, payEnergy: true);
            return;
        }

        if (MoveTargetEvasion.IsConfusedMove(enemyCard, moveIndex))
        {
            if (MoveTargetEvasion.ListZoneBattlers(enemyCard, PlayerType.両方).Count > 0)
            {
                await ShowCpuTargetCandidates(MoveTargetEvasion.ListConfusionCandidates(enemyCard));
                await battle.ResolveConfusedMove(enemyCard, moveIndex, payEnergy: true);
                return;
            }
        }

        if (MoveTargetEvasion.IsMultiSelect(enemyCard, moveIndex)
            && !MoveTargetEvasion.UpToMoveZoneIsEmpty(enemyCard, moveIndex))
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
                    battle.ClearPendingMoveHit();
                    return;
                }

                PayEnemyMoveEnergy(enemyCard, moveIndex);
                if (!await OpenMoveUseWindow(enemyCard, moveIndex))
                    return;

                await battle.ApplyDamageToAllEnemyBattlers(enemyCard, moveIndex, legal);
                return;
            }
        }

        if (MoveTargetEvasion.IsSelectableBattlerTarget(targetType))
        {
            var present = MoveTargetEvasion.ListZoneBattlers(enemyCard, moveSide);
            if (present.Count > 0)
            {
                var legalTargets = MoveTargetEvasion.ListLegalTargets(enemyCard, moveIndex);
                if (legalTargets.Count == 0)
                {
                    battle.ClearPendingMoveHit();
                    return;
                }

                await ShowCpuTargetCandidates(legalTargets);
                var picked = legalTargets[UnityEngine.Random.Range(0, legalTargets.Count)];
                await battle.SelectTargetRoutine(picked, enemyCard, moveIndex, payEnergy: true);
                return;
            }
        }

        var fieldCards = battle.pFieldZone.GetComponentsInChildren<CardController>();
        if (fieldCards.Length > 0 && fieldCards[0].cardKind == CardKind.フィールド)
        {
            await battle.SelectTargetRoutine(fieldCards[0], enemyCard, moveIndex, payEnergy: true);
            return;
        }

        await AttackPlayerHp(enemyCard, moveIndex);
    }

    async Task UseMultiTargetMove(CardController enemyCard, int moveIndex)
    {
        var select = MoveTargetEvasion.MoveSelect(enemyCard, moveIndex);
        var legal = MoveTargetEvasion.ListLegalTargets(enemyCard, moveIndex);
        int pickCount = select.選択方式 == CountMode.まで
            ? Mathf.Min(select.Count, legal.Count)
            : select.Count;
        var picked = PickRandomTargets(legal, pickCount);
        if (picked.Count < select.MinimumCount)
        {
            battle.ClearPendingMoveHit();
            return;
        }

        await ShowCpuTargetCandidates(legal);
        await battle.ResolveSelectedMoveTargets(enemyCard, moveIndex, picked, payEnergy: true);
    }

    void PayEnemyMoveEnergy(CardController card, int moveIndex)
    {
        int energyCost = card.GetMoveEnergyCost(moveIndex);
        if (energyCost > 0)
            battle.eResources.Energy.TakeEnergy(energyCost);
    }

    async Task AttackPlayerHp(CardController enemyCard, int moveIndex)
    {
        if (battle == null) return;

        PayEnemyMoveEnergy(enemyCard, moveIndex);
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

        await CardAnimationManager.Instance.PlayMoveImpact(enemyCard.transform, battle.GetHpTarget(true), null);

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
    /// 拡大・カットイン・技使用時のあと、「相手の技使用時」を開く。
    /// キャンセルしたときは false。
    /// </summary>
    async Task<bool> OpenMoveUseWindow(CardController attacker, int moveIndex)
    {
        bool cancelled = await battle.FinishMoveDeclaration(attacker, moveIndex, null);
        return !cancelled;
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
     async Task ShowCpuTargetCandidates(IEnumerable<CardController> candidates)
    {
        if (battle == null || candidates == null)
            return;

        var list = candidates.Where(card => card != null).Distinct().ToList();
        if (list.Count == 0)
            return;

        battle.ShowSpecificTargetHighlights(list);
        await Task.Delay(200);
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
