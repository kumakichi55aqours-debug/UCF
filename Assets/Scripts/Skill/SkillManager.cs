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
            var collectedSkills = CollectSkillsForTrigger(
                e.Trigger,
                e.SourceCard,
                e.MoveTarget,
                e.Context,
                e.FromLocation,
                e.Phase,
                e.SpecialSummon,
                e.SubjectIsMyCard);
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
        if (NegateZoneEffectSkill.Blocks(card))
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
    /// 技でカードを倒したときのトリガー。対象は倒されたカード。
    /// </summary>
    public static bool IsMoveDefeatTrigger(SkillTrigger trigger)
    {
        return trigger == SkillTrigger.技撃破時
            || trigger == SkillTrigger.技1撃破時
            || trigger == SkillTrigger.技2撃破時;
    }

    /// <summary>
    /// 技を使い終わったあとのトリガー。技1／技2の個別タイミングも含む。
    /// </summary>
    public static bool IsMoveUsedAfterTrigger(SkillTrigger trigger)
    {
        return trigger == SkillTrigger.技使用後
            || trigger == SkillTrigger.技1使用後
            || trigger == SkillTrigger.技2使用後;
    }

    /// <summary>
    /// 技使用後の付与で、効果対象が技対象のとき。
    /// 技対象がバトラーでない、またはそのHPが0以下なら集めない。
    /// フィールドへの技と、プレイヤーへの直接ダメージは技対象がバトラーではない。
    /// </summary>
    static bool ShouldSkipGrantToMoveTarget(SkillTrigger trigger, SkillDB skill, CardController moveTarget)
    {
        if (!IsMoveUsedAfterTrigger(trigger))
            return false;
        if (!(skill is GrantSkill grant) || grant.効果対象 != TargetList.技対象)
            return false;

        return moveTarget == null
            || moveTarget.cardKind != CardKind.バトラー
            || moveTarget.curHp <= 0;
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
        bool specialSummon = false,
        bool subjectIsMyCard = false)
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

        if (trigger == SkillTrigger.他カードが手札に戻った時)
        {
            CollectCardReturnedToHandSkills(subjectIsMyCard, isMyTurn, collectedSkills, skillsSet, fromLocation);
            return collectedSkills;
        }

        if (trigger == SkillTrigger.他カードが登場した時)
        {
            CollectOtherCardAppearedSkills(sourceCard, isMyTurn, collectedSkills, skillsSet, fromLocation, specialSummon);
            return collectedSkills;
        }

        // オンライン時は、自分のカードのトリガーのみを収集
        // 相手カードの登場や墓地送りは、自分の反応を集めるため通す
        if (BattleManager.Instance.IsOnlineMode && sourceCard != null && !sourceCard.isMyCard 
            && trigger != SkillTrigger.技を受ける時
            && trigger != SkillTrigger.相手の技使用時
            && trigger != SkillTrigger.墓地に送られた時
            && trigger != SkillTrigger.登場時
            && trigger != SkillTrigger.カード使用時
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
            if (NegateZoneEffectSkill.Blocks(card))
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
            // CPUが手札から使うと決めた作戦は、任意の登場時も自動で解決する。
            bool cpuCommittedTactics = tacticsOnAppear && !isMyCard && !BattleManager.Instance.IsOnlineMode;
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

                // 手札に残っている作戦は、出せて、作戦ゾーンに空きがあるときだけ候補にする。
                if (card.cardKind == CardKind.作戦
                    && activeLocation == ActiveLocation.手札
                    && !BattleManager.Instance.CanCommitHandTactics(card))
                    continue;

                // オフラインのCPUは、登場時の任意効果を集めない。TRGの任意効果は集める。
                // 起動効果は宣言した1件だけをチェーンに載せる。使うと決めた作戦の登場時は自動で解決する。
                if (!BattleManager.Instance.IsOnlineMode && !card.isMyCard && !skill.強制発動 && !cpuCommittedTactics
                    && !IsTriggerTiming(skill.タイミング))
                    continue;

                // 技撃破時は、倒されたカードを見る。条件が空なら、そのカードの種類では絞らない。
                if (IsMoveDefeatTrigger(trigger))
                {
                    if (!skill.MatchesActivationOwner(card, moveTarget))
                        continue;
                    if (skill.トリガー対象条件 != null && skill.トリガー対象条件.HasCondition()
                        && (moveTarget == null || !moveTarget.MeetsCriteria(skill.トリガー対象条件)))
                        continue;
                }

                // 技使用後の付与は、技対象が生きているバトラーのときだけ集める。
                if (ShouldSkipGrantToMoveTarget(trigger, skill, moveTarget))
                    continue;

                // 「どこから」の条件チェック（未指定ならどこからでも収集。複数指定はいずれかと一致）
                if (timing == ActiveTiming.墓地に送られた時)
                {
                    if (!CardController.MatchesFromZone(skill.どこから, fromLocation, card.isMyCard))
                        continue;
                }
                
                // フェイズタイミングはカードごとのコンテキストで条件を見る。
                // 呼び出し元のContextは完了待ち用なので、発動元には使わない。
                // 相手の技使用時は発動カードが空。空のまま条件を見ると、自分のゾーンが相手側になる。
                var tempCtx = ownPhase
                    ? new SkillContext(card, card, card.transform)
                    : context ?? SkillContext.WithAttackTarget(card, moveTarget);
                tempCtx = tempCtx.AsOwnedBy(card);
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

                    AddCollected(collectedSkills, skillInfo, skill.強制発動 || cpuCommittedTactics, isTurnPlayerCard);
                }
            }
        }

        // 「墓地に送られた時」トリガーの場合、「他のカードが墓地に送られた時」スキルもチェック
        if (trigger == SkillTrigger.墓地に送られた時 && sourceCard != null)
        {
            CollectOtherCardToGraveSkills(sourceCard, isMyTurn, collectedSkills, skillsSet, fromLocation);
        }

        if ((trigger == SkillTrigger.登場時 || trigger == SkillTrigger.カード使用時) && sourceCard != null)
        {
            CollectOtherCardAppearedSkills(sourceCard, isMyTurn, collectedSkills, skillsSet, fromLocation, specialSummon);
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

        CollectOtherTriggerSkills(
            SkillTrigger.他効果誘発,
            activatingCard,
            excludedCard: null,
            BattleManager.Instance.IsMyTurn,
            collectedSkills,
            new HashSet<(int instanceId, SkillDB skill, SkillTrigger trigger)>(),
            (card, skill) =>
                (excluded == null || !excluded.Contains((card.GetInstanceID(), skill.スキル名 ?? string.Empty)))
                && skill.MatchesActivationOwner(card, activatingCard)
                && MatchesTriggerTarget(skill, activatingCard));

        return collectedSkills;
    }

    /// <summary>
    /// 「他のカードが墓地に送られた時」スキルを収集。どこから、発動側、トリガー対象条件を見る。
    /// </summary>
    private static void CollectOtherCardToGraveSkills(
        CardController cardSentToGrave,
        bool isMyTurn,
        CollectedSkills collectedSkills,
        HashSet<(int instanceId, SkillDB skill, SkillTrigger trigger)> skillsSet,
        CardZoneType fromLocation = CardZoneType.None)
    {
        CollectOtherTriggerSkills(
            SkillTrigger.他カードが墓地に送られた時,
            cardSentToGrave,
            cardSentToGrave,
            isMyTurn,
            collectedSkills,
            skillsSet,
            (card, skill) =>
                CardController.MatchesFromZone(skill.どこから, fromLocation, card.isMyCard)
                && skill.MatchesActivationOwner(card, cardSentToGrave)
                && MatchesTriggerTarget(skill, cardSentToGrave));
    }

    /// <summary>
    /// 「他のカードが登場した時」スキルを収集。
    /// 登場したカード自身は除く。通常か特殊か、トリガー対象条件、どこからを見る。
    /// </summary>
    private static void CollectOtherCardAppearedSkills(
        CardController appearedCard,
        bool isMyTurn,
        CollectedSkills collectedSkills,
        HashSet<(int instanceId, SkillDB skill, SkillTrigger trigger)> skillsSet,
        CardZoneType fromLocation,
        bool specialSummon)
    {
        CollectOtherTriggerSkills(
            SkillTrigger.他カードが登場した時,
            appearedCard,
            appearedCard,
            isMyTurn,
            collectedSkills,
            skillsSet,
            (card, skill) =>
                skill.MatchesAppearKind(specialSummon)
                && CardController.MatchesFromZone(skill.どこから, fromLocation, card.isMyCard)
                && skill.MatchesActivationOwner(card, appearedCard)
                && MatchesTriggerTarget(skill, appearedCard));
    }

    /// <summary>
    /// 「他のカードが手札に戻った時」スキルを収集。
    /// 戻ったカードの実体は見ない。誰のカードが、どのゾーンから戻ったかだけを見る。
    /// </summary>
    private static void CollectCardReturnedToHandSkills(
        bool returnedCardIsMyCard,
        bool isMyTurn,
        CollectedSkills collectedSkills,
        HashSet<(int instanceId, SkillDB skill, SkillTrigger trigger)> skillsSet,
        CardZoneType fromLocation)
    {
        CollectOtherTriggerSkills(
            SkillTrigger.他カードが手札に戻った時,
            subject: null,
            excludedCard: null,
            isMyTurn,
            collectedSkills,
            skillsSet,
            (card, skill) =>
                CardController.MatchesFromZone(skill.どこから, fromLocation, card.isMyCard)
                && skill.MatchesActivationOwner(card, returnedCardIsMyCard));
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
        // 撃破したバトラーがいるゾーン。スキル所持カードから見て「いる場所」と照らす
        var defeaterZone = CardController.ZoneTypeFromTransform(
            battlerWhoDefeated != null ? battlerWhoDefeated.transform.parent : null);

        CollectOtherTriggerSkills(
            SkillTrigger.他バトラー撃破時,
            battlerWhoDefeated,
            battlerWhoDefeated,
            isMyTurn,
            collectedSkills,
            skillsSet,
            (card, skill) =>
                CardController.MatchesFromZone(skill.いる場所, defeaterZone, card.isMyCard)
                && skill.MatchesActivationOwner(card, battlerWhoDefeated)
                && MatchesTriggerTarget(skill, battlerWhoDefeated));
    }

    /// <summary>
    /// 「他の〇〇が〇〇した時」系のスキルを集める共通処理。
    /// 両者のBゾーン、Tゾーン、Fゾーン、手札、墓地、異次元を見て、スキルの場所と今いるゾーンが合うものを集める。
    /// subject はトリガーの対象カードで、発動条件のコンテキストと MoveTarget に入る。
    /// excludedCard はトリガーを起こしたカード自身で、集めない。
    /// matches はトリガーごとの絞り込み（どこから、発動側、トリガー対象条件など）。
    /// </summary>
    private static void CollectOtherTriggerSkills(
        SkillTrigger trigger,
        CardController subject,
        CardController excludedCard,
        bool isMyTurn,
        CollectedSkills collectedSkills,
        HashSet<(int instanceId, SkillDB skill, SkillTrigger trigger)> skillsSet,
        Func<CardController, SkillDB, bool> matches)
    {
        var timing = OtherTriggerTiming(trigger);
        if (timing == ActiveTiming.None)
            return;
        bool online = BattleManager.Instance.IsOnlineMode;

        foreach (var card in GetAllCardsForTrigger(null))
        {
            if (card == null || card == excludedCard || card.IsActionBlocked)
                continue;
            if (NegateZoneEffectSkill.Blocks(card))
                continue;

            // オンライン時は、自分のカードのスキルのみを収集
            if (online && !card.isMyCard)
                continue;
            if (card.skills == null)
                continue;

            var activeLocation = card.GetCurrentActiveLocation();

            // 手札に残っている作戦は、出せて、作戦ゾーンに空きがあるときだけ候補にする。
            if (card.cardKind == CardKind.作戦
                && activeLocation == ActiveLocation.手札
                && !BattleManager.Instance.CanCommitHandTactics(card))
                continue;

            bool isTurnPlayerCard = card.isMyCard == isMyTurn;

            foreach (var skill in card.skills)
            {
                if (skill == null || skill.タイミング != timing || !skill.HasRemainingUses)
                    continue;
                if (!skill.IncludesLocation(activeLocation) && !skill.IncludesLocation(ActiveLocation.None))
                    continue;
                if (matches != null && !matches(card, skill))
                    continue;

                var tempCtx = new SkillContext(card, subject, card.transform);
                if (!skill.CanActivate(tempCtx))
                    continue;

                if (!skillsSet.Add((card.GetInstanceID(), skill, trigger)))
                    continue;

                var skillInfo = new SkillInfo
                {
                    Card = card,
                    Skill = skill,
                    Trigger = trigger,
                    MoveTarget = subject,
                    Context = tempCtx
                };
                AddCollected(collectedSkills, skillInfo, skill.強制発動, isTurnPlayerCard);
            }
        }
    }

    /// <summary>
    /// 「他の〇〇が〇〇した時」系のトリガーに対応するスキルのタイミング。名前が揃っていないので個別に対応させる。
    /// </summary>
    private static ActiveTiming OtherTriggerTiming(SkillTrigger trigger)
    {
        switch (trigger)
        {
            case SkillTrigger.他カードが墓地に送られた時: return ActiveTiming.他のカードが墓地に送られた時;
            case SkillTrigger.他カードが登場した時: return ActiveTiming.他のカードが登場した時;
            case SkillTrigger.他カードが手札に戻った時: return ActiveTiming.他のカードが手札に戻った時;
            case SkillTrigger.他バトラー撃破時: return ActiveTiming.他のバトラーが撃破した時;
            case SkillTrigger.他効果誘発: return ActiveTiming.他効果誘発;
            default: return ActiveTiming.None;
        }
    }

    /// <summary>
    /// トリガー対象条件が空なら通す。条件があるときは、対象カードがそれを満たすときだけ通す。
    /// </summary>
    private static bool MatchesTriggerTarget(SkillDB skill, CardController target)
    {
        if (skill.トリガー対象条件 == null || !skill.トリガー対象条件.HasCondition())
            return true;
        return target != null && target.MeetsCriteria(skill.トリガー対象条件);
    }

    /// <summary>
    /// 強制／任意と、ターンプレイヤー側かどうかで、確認順の分類へ振り分ける。
    /// </summary>
    private static void AddCollected(CollectedSkills collectedSkills, SkillInfo skillInfo, bool forced, bool isTurnPlayerCard)
    {
        if (forced)
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

    /// <summary>
    /// 両プレイヤーのBゾーン、Tゾーン、Fゾーン、手札、墓地、異次元にいるカードの取得。
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
            BattleManager.Instance.pTacticsZone,
            BattleManager.Instance.pFieldZone,
            BattleManager.Instance.pHandZone,
            BattleManager.Instance.pGraveZone,
            BattleManager.Instance.pDimensionZone,
            BattleManager.Instance.eBattlerZone,
            BattleManager.Instance.eTacticsZone,
            BattleManager.Instance.eFieldZone,
            BattleManager.Instance.eHandZone,
            BattleManager.Instance.eGraveZone,
            BattleManager.Instance.eDimensionZone
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
    /// チェーンスキル発動確認要求イベントを処理
    /// </summary>
    private async void OnChainSkillActivationRequest(ChainSkillActivationRequestEvent e)
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

        // 手札の作戦は、出せない、または作戦ゾーンが埋まっているなら聞かない。
        if (BattleManager.Instance.IsHandTacticsAwaitingAppearance(link.SourceCard)
            && !BattleManager.Instance.CanCommitHandTactics(link.SourceCard))
        {
            SkillChainManager.Instance?.NotifyActivationComplete(link.ChainId, false, link.IsMyCard);
            return;
        }

        // 強制発動と、オフラインCPUの任意効果は確認を出さずYes。
        // CPUの手札作戦も、プレイヤーがYesしたときと同じく、ここで場へ出す。
        bool offlineCpu = BattleManager.Instance != null
            && !BattleManager.Instance.IsOnlineMode
            && link.SourceCard != null
            && !link.SourceCard.isMyCard;
        if (link.Skill.強制発動 || offlineCpu)
        {
            bool commit = await BattleManager.Instance.TryCommitHandTacticsAppearance(link.SourceCard);
            SkillChainManager.Instance?.NotifyActivationComplete(link.ChainId, commit, link.IsMyCard);
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
        bool revealedHand = false;
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

            if (link.EffectNegated || NegateZoneEffectSkill.Blocks(link.SourceCard))
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

            // 相手手札の裏カードは、発動演出と続く選択のあいだ表にする。
            revealedHand = CardAnimationManager.RevealEnemyHandFace(link.SourceCard);

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
            CardAnimationManager.RestoreEnemyHandBack(link.SourceCard, revealedHand);
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

    /// <summary>
    /// 起動効果・登場時・永続・ルール以外の、トリガーとして集めるタイミング。
    /// </summary>
    public static bool IsTriggerTiming(ActiveTiming timing)
    {
        switch (timing)
        {
            case ActiveTiming.技使用時:
            case ActiveTiming.技1使用時:
            case ActiveTiming.技2使用時:
            case ActiveTiming.技使用後:
            case ActiveTiming.技1使用後:
            case ActiveTiming.技2使用後:
            case ActiveTiming.技撃破時:
            case ActiveTiming.技1撃破時:
            case ActiveTiming.技2撃破時:
            case ActiveTiming.技を受ける時:
            case ActiveTiming.手札に加わった時:
            case ActiveTiming.墓地に送られた時:
            case ActiveTiming.自分のフェイズ開始時:
            case ActiveTiming.自分のフェイズ終了時:
            case ActiveTiming.相手のフェイズ開始時:
            case ActiveTiming.相手のフェイズ終了時:
            case ActiveTiming.相手の技使用時:
            case ActiveTiming.他のカードが墓地に送られた時:
            case ActiveTiming.他のバトラーが撃破した時:
            case ActiveTiming.他効果誘発:
            case ActiveTiming.他のカードが手札に戻った時:
            case ActiveTiming.他のカードが登場した時:
                return true;
            default:
                return false;
        }
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