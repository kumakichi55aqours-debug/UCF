using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using System.Collections.Generic;

public class SkillManager : MonoBehaviour
{
    private void OnEnable()
    {
        EventBus.Subscribe<SkillTriggerEvent>(OnSkillTrigger);
        EventBus.Subscribe<ChainSkillActivationRequestEvent>(OnChainSkillActivationRequest);
        EventBus.Subscribe<ChainSkillExecutionEvent>(OnChainSkillExecution);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<SkillTriggerEvent>(OnSkillTrigger);
        EventBus.Unsubscribe<ChainSkillActivationRequestEvent>(OnChainSkillActivationRequest);
        EventBus.Unsubscribe<ChainSkillExecutionEvent>(OnChainSkillExecution);
    }

    private async void OnSkillTrigger(SkillTriggerEvent e)
    {
        // オフライン時処理 ※未実装
        if (ShouldSkipOfflineOpponentSkill(e.Trigger, e.SourceCard))
        {
            e.Context?.ProcessingCompleteTcs?.TrySetResult(true);
            return;
        }
        
        // チェーンの効果処理中なら、そのチェーンが終わってから発動確認を始める
        if (SkillChainManager.Instance != null && SkillChainManager.Instance.TryEnqueueTrigger(e))
            return;

        // オンライン時処理　全てのスキルをチェーン処理を通す
        await ProcessSkillWithChain(e);
    }

    /// <summary>
    /// チェーン処理を使用してスキルを処理
    /// スキル収集を行い、SkillChainManagerにチェーン処理を委譲
    /// </summary>
    private async Task ProcessSkillWithChain(SkillTriggerEvent e)
    {
        if (SkillChainManager.Instance != null)
        {
            var collectedSkills = CollectSkillsForTrigger(e.Trigger, e.SourceCard, e.MoveTarget, e.Context, e.FromLocation, e.Phase, e.SpecialSummon);
            await SkillChainManager.Instance.ProcessChainForCollectedSkills(collectedSkills);
        }
        else
        {
            e.Context?.ProcessingCompleteTcs?.TrySetResult(true);
        }
    }

    /// <summary>
    /// プレイヤーが選んだ起動効果を、確認済みのチェーンリンクとして解決する。
    /// ボタン操作が発動確認なので、Yes/Noは出さずに効果実行と発動アニメまで進む。
    /// </summary>
    public static async Task ResolveDeclaredIgnition(CardController card, SkillDB skill, SkillContext context)
    {
        if (card == null || skill == null)
            return;

        context = context ?? new SkillContext(card, card, card.transform);
        context.SetSourceCard(card);

        if (SkillChainManager.Instance == null)
        {
            if (CardAnimationManager.Instance != null)
                await CardAnimationManager.Instance.AnimateSkillActivation(card);
            await skill.ActivateEffect(context);
            skill.TryConsumeUse(card);
            return;
        }

        var collected = CreateEmptyCollected(SkillTrigger.起動効果);
        collected.SourceCard = card;
        collected.MoveTarget = context.対象カード ?? card;
        collected.Context = context;

        var info = new SkillInfo
        {
            Card = card,
            Skill = skill,
            Trigger = SkillTrigger.起動効果,
            MoveTarget = context.対象カード,
            Context = context
        };

        bool isTurnPlayerCard = card.isMyCard == BattleManager.Instance.IsMyTurn;
        if (isTurnPlayerCard)
            collected.TPForcedSkills.Add(info);
        else
            collected.NonTPForcedSkills.Add(info);

        await SkillChainManager.Instance.ProcessChainForCollectedSkills(collected);
    }

    public static CollectedSkills CreateEmptyCollected(SkillTrigger trigger)
    {
        return new CollectedSkills
        {
            Trigger = trigger,
            TPForcedSkills = new List<SkillInfo>(),
            TPOptionalSkills = new List<SkillInfo>(),
            NonTPForcedSkills = new List<SkillInfo>(),
            NonTPOptionalSkills = new List<SkillInfo>()
        };
    }

    public static bool IsOwnPhaseTrigger(SkillTrigger trigger)
    {
        return trigger == SkillTrigger.自分のフェイズ開始時
            || trigger == SkillTrigger.自分のフェイズ終了時;
    }

    /// <summary>
    /// 技を発動したカード自身のスキルだけを集めるトリガー。
    /// 技1／技2の個別タイミングも同じ。
    /// </summary>
    public static bool IsMoveActivatorOnlyTrigger(SkillTrigger trigger)
    {
        return trigger == SkillTrigger.技使用時
            || trigger == SkillTrigger.技1使用時
            || trigger == SkillTrigger.技2使用時
            || trigger == SkillTrigger.技使用後
            || trigger == SkillTrigger.技1使用後
            || trigger == SkillTrigger.技2使用後
            || trigger == SkillTrigger.技撃破時
            || trigger == SkillTrigger.技1撃破時
            || trigger == SkillTrigger.技2撃破時;
    }

    /// <summary>
    /// チェーンに乗った効果へ反応できるか。スキル名に PAS を含むものは発動のきっかけにしない。
    /// </summary>
    public static bool IsRespondableActivation(string skillName)
    {
        return string.IsNullOrEmpty(skillName) || skillName.IndexOf("PAS", StringComparison.Ordinal) < 0;
    }

    /// <summary>
    /// トリガーに関連するスキルを収集
    /// </summary>
    public static CollectedSkills CollectSkillsForTrigger(
        SkillTrigger trigger, 
        CardController sourceCard, 
        CardController moveTarget, 
        SkillContext context, 
        CardZoneType fromLocation = CardZoneType.None,
        TurnPhase phase = TurnPhase.ドローフェイズ,
        bool specialSummon = false)
    {
        var collectedSkills = new CollectedSkills
        {
            Trigger = trigger,
            SourceCard = sourceCard,
            MoveTarget = moveTarget,
            Context = context,
            Phase = phase,
            TPForcedSkills = new List<SkillInfo>(),
            TPOptionalSkills = new List<SkillInfo>(),
            NonTPForcedSkills = new List<SkillInfo>(),
            NonTPOptionalSkills = new List<SkillInfo>()
        };

        // 起動効果はプレイヤーが選んでからチェーンに載せる。
        // ここで集めると、相手の起動効果まで同じ窓に入る。
        // 効果の発動に対しては、チェーンへ乗ったあとの反応窓でだけ集める。
        if (trigger == SkillTrigger.起動効果 || trigger == SkillTrigger.他効果誘発)
            return collectedSkills;

        bool isMyTurn = BattleManager.Instance.IsMyTurn;
        bool ownPhase = IsOwnPhaseTrigger(trigger);
        var skillsSet = new HashSet<(int instanceId, SkillDB skill, SkillTrigger trigger)>();

        // オンライン時は、自分のカードのトリガーのみを収集
        //「技を受ける時」「相手の技使用時」「墓地に送られた」以外は収集処理中止
        if (BattleManager.Instance.IsOnlineMode && sourceCard != null && !sourceCard.isMyCard 
            && trigger != SkillTrigger.技を受ける時
            && trigger != SkillTrigger.相手の技使用時
            && trigger != SkillTrigger.墓地に送られた時
            )
        {
            return collectedSkills;
        }

        // 各ゾーンのカードを取得し、現在地とスキルの場所が一致するものを集める
        var allCards = GetAllCardsForTrigger(sourceCard);
        var timing = ConvertToActiveTiming(trigger);

        // 「相手の技使用時」「技を受ける時」は、受けた側＝非TP側のカードを収集する
        bool nonTurnPlayerOnly = trigger == SkillTrigger.技を受ける時
            || trigger == SkillTrigger.相手の技使用時;

        foreach (var card in allCards)
        {
            if (card != null && card.IsActionBlocked)
                continue;
            if (card != null && card.IsSlowed && IsMoveActivatorOnlyTrigger(trigger))
                continue;

            bool isMyCard = card.isMyCard;
            bool isTurnPlayerCard = isMyCard == isMyTurn;

            // 自分のフェイズ開始／終了は、フェイズの持ち主のカードだけをターン側として集める。
            // オフラインの相手ターンでは、CPU側がその持ち主になる。
            if (ownPhase)
            {
                bool phaseOwnerIsPlayer = BattleManager.Instance.IsOnlineMode || isMyTurn;
                if (isMyCard != phaseOwnerIsPlayer)
                    continue;
                isTurnPlayerCard = true;
            }

            // 登場時は場に出たそのカード自身だけ
            if ((trigger == SkillTrigger.登場時 
                || trigger == SkillTrigger.カード使用時
                || trigger == SkillTrigger.墓地に送られた時) && card != sourceCard)
                continue;

            // 非TP側収集スキルは、TP側は収集しない
            if (nonTurnPlayerOnly && isTurnPlayerCard)
                continue;

            // 技を受ける時は、技を受けるそのカードのみ
            if (trigger == SkillTrigger.技を受ける時 && card != sourceCard)
                continue;

            // 技使用時・技使用後・技撃破時は、技を発動したカード自身だけ
            if (IsMoveActivatorOnlyTrigger(trigger) && card != sourceCard)
                continue;

            // オンライン時は、自分のカードのスキルのみを収集
            if (BattleManager.Instance.IsOnlineMode && !card.isMyCard)
                continue;

            var activeLocation = card.GetCurrentActiveLocation();
            // 手札から出した直後はすでにT/Fゾーンにいる。
            // 使用したカード自身は、場所が手札のスキルも対象にする。
            // 作戦の登場時は、場所がTゾーンのスキルを、今Tゾーンにいなくても対象にする。
            bool playedFromHand = trigger == SkillTrigger.カード使用時 && card == sourceCard;
            bool tacticsOnAppear = playedFromHand && card.cardKind == CardKind.作戦;
            var skills = card.skills?.Where(s =>
                s.タイミング == timing &&
                s.HasRemainingUses &&
                (s.IncludesLocation(activeLocation) || s.IncludesLocation(ActiveLocation.None)
                    || (playedFromHand && s.IncludesLocation(ActiveLocation.手札))
                    || (tacticsOnAppear && s.IncludesLocation(ActiveLocation.Tゾーン)))
            ).ToList() ?? new List<SkillDB>();

            foreach (var skill in skills)
            {
                if (ownPhase && skill.対象フェイズ != phase)
                    continue;

                if (trigger == SkillTrigger.登場時 && !skill.MatchesAppearKind(specialSummon))
                    continue;

                // 手札に残っている作戦は、通常の登場と同じ支払いコストを払えるときだけ候補にする。
                if (card.cardKind == CardKind.作戦
                    && activeLocation == ActiveLocation.手札
                    && !BattleManager.Instance.CanPayHandTactics(card))
                    continue;

                // オフラインのCPU側は強制発動だけを集める。任意発動はプレイヤーに聞かない。
                if (!BattleManager.Instance.IsOnlineMode && !card.isMyCard && !skill.強制発動)
                    continue;

                // 「どこから」の条件チェック（未指定ならどこからでも収集。複数指定はいずれかと一致）
                if (timing == ActiveTiming.墓地に送られた時)
                {
                    if (!CardController.MatchesFromZone(skill.どこから, fromLocation, card.isMyCard))
                        continue;
                }
                
                // フェイズタイミングはカードごとのコンテキストで条件を見る。
                // 呼び出し元のContextは完了待ち用なので、発動元には使わない。
                var tempCtx = ownPhase
                    ? new SkillContext(card, card, card.transform)
                    : context ?? SkillContext.WithAttackTarget(card, moveTarget);
                if (skill.CanActivate(tempCtx))
                {
                    // 重複チェック
                    var skillKey = (card.GetInstanceID(), skill, trigger);
                    if (skillsSet.Contains(skillKey))
                        continue;

                    skillsSet.Add(skillKey);

                    var skillInfo = new SkillInfo
                    {
                        Card = card,
                        Skill = skill,
                        Trigger = trigger,
                        MoveTarget = moveTarget,
                        Context = tempCtx
                    };

                    if (skill.強制発動)
                    {
                        if (isTurnPlayerCard)
                            collectedSkills.TPForcedSkills.Add(skillInfo);
                        else
                            collectedSkills.NonTPForcedSkills.Add(skillInfo);
                    }
                    else
                    {
                        if (isTurnPlayerCard)
                            collectedSkills.TPOptionalSkills.Add(skillInfo);
                        else
                            collectedSkills.NonTPOptionalSkills.Add(skillInfo);
                    }
                }
            }
        }

        // 「墓地に送られた時」トリガーの場合、「他のカードが墓地に送られた時」スキルもチェック
        if (trigger == SkillTrigger.墓地に送られた時 && sourceCard != null)
        {
            CollectOtherCardToGraveSkills(sourceCard, isMyTurn, collectedSkills, skillsSet, fromLocation);
        }

        // 「撃破時」トリガーの場合、「他のバトラーが撃破した時」スキルもチェック
        if ((trigger == SkillTrigger.技撃破時 || trigger == SkillTrigger.技1撃破時 || trigger == SkillTrigger.技2撃破時) && sourceCard != null)
        {
            CollectOtherBattlerDefeatSkills(sourceCard, isMyTurn, collectedSkills, skillsSet);
        }

        return collectedSkills;
    }

    /// <summary>
    /// チェーンに乗ったカードの効果に反応できるスキルを集める。
    /// 対象カードは、発動したカード。トリガー対象条件が空なら、カードの中身では絞らない。
    /// 発動側が両方以外のときは、このスキルを持つカードから見たプレイヤー側か相手側だけを集める。
    /// excluded は、このチェーンに既に乗っている自分のスキル。
    /// </summary>
    public static CollectedSkills CollectActivationResponses(
        CardController activatingCard,
        HashSet<(int instanceId, string skillName)> excluded)
    {
        var collectedSkills = CreateEmptyCollected(SkillTrigger.他効果誘発);
        collectedSkills.SourceCard = activatingCard;
        collectedSkills.MoveTarget = activatingCard;
        if (activatingCard == null || BattleManager.Instance == null)
            return collectedSkills;

        bool isMyTurn = BattleManager.Instance.IsMyTurn;
        var seen = new HashSet<(int instanceId, SkillDB skill)>();

        foreach (var card in GetAllCardsForTrigger(null))
        {
            if (card == null || card.IsActionBlocked)
                continue;
            if (BattleManager.Instance.IsOnlineMode && !card.isMyCard)
                continue;

            bool isTurnPlayerCard = card.isMyCard == isMyTurn;
            var activeLocation = card.GetCurrentActiveLocation();
            var skills = card.skills?.Where(s =>
                s != null &&
                s.タイミング == ActiveTiming.他効果誘発 &&
                s.HasRemainingUses &&
                (s.IncludesLocation(activeLocation) || s.IncludesLocation(ActiveLocation.None))
            ).ToList() ?? new List<SkillDB>();

            foreach (var skill in skills)
            {
                if (!BattleManager.Instance.IsOnlineMode && !card.isMyCard && !skill.強制発動)
                    continue;

                string skillName = skill.スキル名 ?? string.Empty;
                if (excluded != null && excluded.Contains((card.GetInstanceID(), skillName)))
                    continue;

                if (!skill.MatchesActivationOwner(card, activatingCard))
                    continue;

                if (skill.トリガー対象条件 != null && skill.トリガー対象条件.HasCondition()
                    && !activatingCard.MeetsCriteria(skill.トリガー対象条件))
                    continue;

                if (card.cardKind == CardKind.作戦
                    && activeLocation == ActiveLocation.手札
                    && !BattleManager.Instance.CanPayHandTactics(card))
                    continue;

                var tempCtx = new SkillContext(card, activatingCard, card.transform);
                if (!skill.CanActivate(tempCtx))
                    continue;

                if (!seen.Add((card.GetInstanceID(), skill)))
                    continue;

                var skillInfo = new SkillInfo
                {
                    Card = card,
                    Skill = skill,
                    Trigger = SkillTrigger.他効果誘発,
                    MoveTarget = activatingCard,
                    Context = tempCtx
                };

                if (skill.強制発動)
                {
                    if (isTurnPlayerCard)
                        collectedSkills.TPForcedSkills.Add(skillInfo);
                    else
                        collectedSkills.NonTPForcedSkills.Add(skillInfo);
                }
                else
                {
                    if (isTurnPlayerCard)
                        collectedSkills.TPOptionalSkills.Add(skillInfo);
                    else
                        collectedSkills.NonTPOptionalSkills.Add(skillInfo);
                }
            }
        }

        return collectedSkills;
    }

    /// <summary>
    /// 「他のカードが墓地に送られた時」スキルを収集
    /// </summary>
    private static void CollectOtherCardToGraveSkills(
        CardController cardSentToGrave,
        bool isMyTurn,
        CollectedSkills collectedSkills,
        HashSet<(int instanceId, SkillDB skill, SkillTrigger trigger)> skillsSet,
        CardZoneType fromLocation = CardZoneType.None)
    {
        var allCardsOnField = GetAllCardsOnField();

        foreach (var card in allCardsOnField)
        {
            if (card == cardSentToGrave)
                continue;
            if (card != null && card.IsActionBlocked)
                continue;

            // オンライン時は、自分のカードのスキルのみを収集
            if (BattleManager.Instance.IsOnlineMode && !card.isMyCard)
                continue;

            bool isMyCard = card.isMyCard;
            bool isTurnPlayerCard = isMyCard == isMyTurn;

            var activeLocation = card.GetCurrentActiveLocation();

            var otherCardGraveyardSkills = card.skills?.Where(s =>
                s.タイミング == ActiveTiming.他のカードが墓地に送られた時 &&
                s.HasRemainingUses &&
                (s.IncludesLocation(activeLocation) || s.IncludesLocation(ActiveLocation.None))
            ).ToList() ?? new List<SkillDB>();

            foreach (var skill in otherCardGraveyardSkills)
            {
                if (!BattleManager.Instance.IsOnlineMode && !card.isMyCard && !skill.強制発動)
                    continue;

                // 「どこから」の条件チェック（スキル所持カードから見たゾーン）
                if (!CardController.MatchesFromZone(skill.どこから, fromLocation, card.isMyCard))
                    continue;

                if (!skill.MatchesActivationOwner(card, cardSentToGrave))
                    continue;

                // トリガー対象条件をチェック
                if (skill.トリガー対象条件 != null && skill.トリガー対象条件.HasCondition())
                {
                    if (!cardSentToGrave.MeetsCriteria(skill.トリガー対象条件))
                        continue;
                }

                var tempCtx = new SkillContext(card, cardSentToGrave, card.transform);
                if (skill.CanActivate(tempCtx))
                {
                    var skillKey = (card.GetInstanceID(), skill, SkillTrigger.他カードが墓地に送られた時);
                    if (skillsSet.Contains(skillKey))
                        continue;

                    skillsSet.Add(skillKey);

                    var skillInfo = new SkillInfo
                    {
                        Card = card,
                        Skill = skill,
                        Trigger = SkillTrigger.他カードが墓地に送られた時,
                        MoveTarget = cardSentToGrave,
                        Context = tempCtx
                    };

                    if (skill.強制発動)
                    {
                        if (isTurnPlayerCard)
                            collectedSkills.TPForcedSkills.Add(skillInfo);
                        else
                            collectedSkills.NonTPForcedSkills.Add(skillInfo);
                    }
                    else
                    {
                        if (isTurnPlayerCard)
                            collectedSkills.TPOptionalSkills.Add(skillInfo);
                        else
                            collectedSkills.NonTPOptionalSkills.Add(skillInfo);
                    }
                }
            }
        }
    }

    /// <summary>
    /// 「他のバトラーが撃破した時」スキルを収集
    /// </summary>
    private static void CollectOtherBattlerDefeatSkills(
        CardController battlerWhoDefeated,
        bool isMyTurn,
        CollectedSkills collectedSkills,
        HashSet<(int instanceId, SkillDB skill, SkillTrigger trigger)> skillsSet)
    {
        var allCardsOnField = GetAllCardsOnField();

        foreach (var card in allCardsOnField)
        {
            if (card == battlerWhoDefeated)
                continue;
            if (card != null && card.IsActionBlocked)
                continue;

            // オンライン時は、自分のカードのスキルのみを収集
            if (BattleManager.Instance.IsOnlineMode && !card.isMyCard)
                continue;

            bool isMyCard = card.isMyCard;
            bool isTurnPlayerCard = isMyCard == isMyTurn;

            var activeLocation = card.GetCurrentActiveLocation();

            var otherBattlerDefeatSkills = card.skills?.Where(s =>
                s.タイミング == ActiveTiming.他のバトラーが撃破した時 &&
                s.HasRemainingUses &&
                (s.IncludesLocation(activeLocation) || s.IncludesLocation(ActiveLocation.None))
            ).ToList() ?? new List<SkillDB>();

            foreach (var skill in otherBattlerDefeatSkills)
            {
                if (!BattleManager.Instance.IsOnlineMode && !card.isMyCard && !skill.強制発動)
                    continue;

                // 撃破したバトラーがいるゾーン。未指定ならどこでも。スキル所持カードから見たゾーン
                var defeaterZone = CardController.ZoneTypeFromTransform(
                    battlerWhoDefeated != null ? battlerWhoDefeated.transform.parent : null);
                if (!CardController.MatchesFromZone(skill.いる場所, defeaterZone, card.isMyCard))
                    continue;

                if (!skill.MatchesActivationOwner(card, battlerWhoDefeated))
                    continue;

                // トリガー対象条件をチェック
                if (skill.トリガー対象条件 != null && skill.トリガー対象条件.HasCondition())
                {
                    if (!battlerWhoDefeated.MeetsCriteria(skill.トリガー対象条件))
                        continue;
                }

                var tempCtx = new SkillContext(card, battlerWhoDefeated, card.transform);
                if (skill.CanActivate(tempCtx))
                {
                    var skillKey = (card.GetInstanceID(), skill, SkillTrigger.他バトラー撃破時);
                    if (skillsSet.Contains(skillKey))
                        continue;

                    skillsSet.Add(skillKey);

                    var skillInfo = new SkillInfo
                    {
                        Card = card,
                        Skill = skill,
                        Trigger = SkillTrigger.他バトラー撃破時,
                        MoveTarget = battlerWhoDefeated,
                        Context = tempCtx
                    };

                    if (skill.強制発動)
                    {
                        if (isTurnPlayerCard)
                            collectedSkills.TPForcedSkills.Add(skillInfo);
                        else
                            collectedSkills.NonTPForcedSkills.Add(skillInfo);
                    }
                    else
                    {
                        if (isTurnPlayerCard)
                            collectedSkills.TPOptionalSkills.Add(skillInfo);
                        else
                            collectedSkills.NonTPOptionalSkills.Add(skillInfo);
                    }
                }
            }
        }
    }

    /// <summary>
    /// 両プレイヤーの各ゾーンにいるカードの取得
    /// 起点カードがまだゾーン配下に無い場合も結果に含める。
    /// </summary>
    private static List<CardController> GetAllCardsForTrigger(CardController sourceCard)
    {
        var cards = new List<CardController>();
        var seen = new HashSet<int>();

        void AddCard(CardController card)
        {
            if (card == null || !seen.Add(card.GetInstanceID())) return;
            cards.Add(card);
        }

        AddCard(sourceCard);

        var allZones = new[]
        {
            BattleManager.Instance.pBattlerZone,
            BattleManager.Instance.pHandZone,
            BattleManager.Instance.pGraveZone,
            BattleManager.Instance.pTacticsZone,
            BattleManager.Instance.pFieldZone,
            BattleManager.Instance.eBattlerZone,
            BattleManager.Instance.eHandZone,
            BattleManager.Instance.eGraveZone,
            BattleManager.Instance.eTacticsZone,
            BattleManager.Instance.eFieldZone
        };

        foreach (var zone in allZones)
        {
            if (zone == null) continue;
            foreach (var card in zone.GetComponentsInChildren<CardController>())
                AddCard(card);
        }

        return cards;
    }

    /// <summary>
    /// 場にいるすべてのカードを取得（Bゾーン、Tゾーン、Fゾーン）
    /// </summary>
    private static List<CardController> GetAllCardsOnField()
    {
        var cards = new List<CardController>();
        var fieldZones = new Transform[]
        {
            BattleManager.Instance.pBattlerZone,
            BattleManager.Instance.pTacticsZone,
            BattleManager.Instance.pFieldZone,
            BattleManager.Instance.eBattlerZone,
            BattleManager.Instance.eTacticsZone,
            BattleManager.Instance.eFieldZone
        };

        foreach (var zone in fieldZones)
        {
            if (zone == null) continue;
            cards.AddRange(zone.GetComponentsInChildren<CardController>());
        }

        return cards;
    }

    /// <summary>
    /// チェーンスキル発動確認要求イベントを処理
    /// </summary>
    private void OnChainSkillActivationRequest(ChainSkillActivationRequestEvent e)
    {
        var link = e.Link;
        Debug.Log($"[SkillManager] スキル発動確認: ChainId={link.ChainId}, ChainNumber={link.ChainNumber}, Card={link.SourceCard.cardName}, IsMyCard={link.IsMyCard}");

        // オンライン時、相手のカードは相手側で処理される
        // オフラインのCPUカードは、このクライアントが強制発動を解決する
        if (!link.IsMyCard && BattleManager.Instance.IsOnlineMode)
            return;

        if (link.Skill == null)
            return;

        var inst = link.Skill;

        // 条件チェック。攻撃対象がいないときは発動カードで埋めない。
        var tempCtx = link.Context ?? SkillContext.WithAttackTarget(link.SourceCard, link.MoveTarget);

        // 条件不一致ならスキップ
        if (!inst.CanActivate(tempCtx))
        {
            SkillChainManager.Instance?.NotifyActivationComplete(link.ChainId, false, link.IsMyCard);
            return;
        }

        // 手札の作戦は、出す時点で支払いコストが足りないなら聞かない。
        if (BattleManager.Instance.IsHandTacticsAwaitingAppearance(link.SourceCard)
            && !BattleManager.Instance.CanPayHandTactics(link.SourceCard))
        {
            SkillChainManager.Instance?.NotifyActivationComplete(link.ChainId, false, link.IsMyCard);
            return;
        }

        // 強制発動の場合は自動的にYes
        if (link.Skill.強制発動)
        {
            SkillChainManager.Instance?.NotifyActivationComplete(link.ChainId, true, link.IsMyCard);
            return;
        }

        // Yes/No選択UIを表示
        if (YesOrNoSelectionUI.Instance != null)
        {
            // 相手側の操作をブロック開始
            if (BattleManager.Instance.IsOnlineMode)
                OnlineSyncManager.Instance.SyncBeginOpponentDecisionBlock();

            YesOrNoSelectionUI.Instance.ShowSelectionUI(
                link.SourceCard,
                link.Skill,
                async () =>
                {
                    // Yes選択。手札の作戦は、ここで場に出してコストを払う。
                    bool commit = await BattleManager.Instance.TryCommitHandTacticsAppearance(link.SourceCard);
                    SkillChainManager.Instance?.NotifyActivationComplete(link.ChainId, commit, link.IsMyCard);
                    if (BattleManager.Instance.IsOnlineMode)
                        OnlineSyncManager.Instance.SyncEndOpponentDecisionBlock();
                },
                async () =>
                {
                    // No選択
                    SkillChainManager.Instance?.NotifyActivationComplete(link.ChainId, false, link.IsMyCard);
                    if (BattleManager.Instance.IsOnlineMode)
                        OnlineSyncManager.Instance.SyncEndOpponentDecisionBlock();
                    await Task.CompletedTask;
                }
            );
        }
        else
            SkillChainManager.Instance?.NotifyActivationComplete(link.ChainId, false, link.IsMyCard);
    }

    /// <summary>
    /// チェーンスキル実行イベントを処理
    /// </summary>
    private async void OnChainSkillExecution(ChainSkillExecutionEvent e)
    {
        var link = e.Link;
        Debug.Log($"[SkillManager] チェーンスキル実行: ChainId={link.ChainId}, ChainNumber={link.ChainNumber}, Card={link.SourceCard.cardName}, IsMyCard={link.IsMyCard}");

        // オンライン時、相手のカードは相手側で処理される
        if (!link.IsMyCard && BattleManager.Instance.IsOnlineMode)
            return;

        SkillContext.PushResolvingSource(link.SourceCard);
        try
        {
            // 強制発動の手札作戦は確認を通らないので、効果の直前に出す。
            if (!await BattleManager.Instance.TryCommitHandTacticsAppearance(link.SourceCard))
            {
                link.ExecutionTcs?.TrySetResult(false);
                return;
            }

            var inst = link.Skill;
            if (inst == null)
            {
                link.ExecutionTcs?.TrySetResult(false);
                return;
            }

            if (link.EffectNegated)
            {
                link.Skill.TryConsumeUse(link.SourceCard);
                Debug.Log($"[SkillManager] 効果を無効化したため実行を飛ばす: ChainId={link.ChainId}, Card={link.SourceCard.cardName}, Skill={link.Skill.スキル名}");
                link.ExecutionTcs?.TrySetResult(true);
                return;
            }

            SkillContext ctx;

            if (link.Context != null)
            {
                ctx = link.Context;
                // ContextのSourceCardがnullの場合は、link.SourceCard（自分自身）を設定
                ctx.SetSourceCard(link.SourceCard);
            }
            else
            {
                ctx = SkillContext.WithAttackTarget(link.SourceCard, link.MoveTarget);
            }

            if (BattleManager.Instance.IsOnlineMode)
            {
                OnlineSyncManager.Instance.SyncRequestOpponentSkillActivation(
                    link.SourceCard,
                    link.Trigger.ToString()
                );
            }

            // 自分側のスキル発動演出
            await CardAnimationManager.Instance.AnimateSkillActivation(link.SourceCard);

            if (BattleManager.Instance.IsOnlineMode)
            {
                await OnlineSyncManager.Instance.WaitForSkillActivationAck();
            }

            // スキル効果を実行
            await inst.ActivateEffect(ctx);
            if (BattleManager.Instance.IsOnlineMode)
                await OnlineSyncManager.Instance.WaitForAllOpponentAnimations();

            link.Skill.TryConsumeUse(link.SourceCard);

            Debug.Log($"[SkillManager] チェーンスキル実行完了: ChainId={link.ChainId}, Card={link.SourceCard.cardName}, Skill={link.Skill.スキル名}");
            link.ExecutionTcs?.TrySetResult(true);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SkillManager] チェーンスキル実行でエラー: {ex.Message}");
            // エラー時も実行完了を通知（エラーハンドリング）
            link.ExecutionTcs?.TrySetResult(false);
        }
        finally
        {
            SkillContext.PopResolvingSource();
        }
    }

    /// <summary>
    /// オフラインでCPUカードを起点にしたスキル処理をスキップするか
    /// </summary>
    public static bool ShouldSkipOfflineOpponentSkill(SkillTrigger trigger, CardController sourceCard)
    {
        if (BattleManager.Instance.IsOnlineMode || sourceCard == null || sourceCard.isMyCard)
            return false;
        if (trigger == SkillTrigger.相手の技使用時 || trigger == SkillTrigger.墓地に送られた時)
        {
            return false;
        }
        return false;
    }

    /// <summary>
    /// SkillTriggerをActiveTimingに変換する。
    /// 同名の値はそのまま対応させ、カード使用時だけ登場時のスキルを参照する。
    /// </summary>
    public static ActiveTiming ConvertToActiveTiming(SkillTrigger trigger)
    {
        if (trigger == SkillTrigger.カード使用時)
            return ActiveTiming.登場時;

        return Enum.TryParse(trigger.ToString(), out ActiveTiming timing)
            ? timing
            : ActiveTiming.None;
    }
}

/// <summary>
/// 収集されたスキル情報
/// </summary>
public class CollectedSkills
{
    public SkillTrigger Trigger { get; set; }
    public TurnPhase Phase { get; set; }
    public CardController SourceCard { get; set; }
    public CardController MoveTarget { get; set; }
    public SkillContext Context { get; set; }
    public List<SkillInfo> TPForcedSkills { get; set; }
    public List<SkillInfo> TPOptionalSkills { get; set; }
    public List<SkillInfo> NonTPForcedSkills { get; set; }
    public List<SkillInfo> NonTPOptionalSkills { get; set; }
}

/// <summary>
/// スキル情報
/// </summary>
public class SkillInfo
{
    public CardController Card { get; set; }
    public SkillDB Skill { get; set; }
    public SkillTrigger Trigger { get; set; }
    public CardController MoveTarget { get; set; }
    public SkillContext Context { get; set; }
    public string SentZoneName { get; set; } = string.Empty;
    public int SentZoneIndex { get; set; } = -1;
}