
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

[Serializable]
[SkillDisplayName("ダメージ")]
public class DamageSkill : SkillDB
{
    public TargetList 効果対象;
    public PlayerType 対象側 = PlayerType.プレイヤー;
    public SearchCriteria 対象条件;

    [SerializeReference]
    public List<ISkillCondition> 対象状態 = new List<ISkillCondition>();

    public int 効果数 = 1;
    public SkillArg 効果数キー = SkillArg.なし;

    [Header("枚数カウント")]
    public List<CardZoneType> 枚数参照場所 = new List<CardZoneType>
    {
        CardZoneType.プレイヤーBゾーン
    };

    public SearchCriteria 枚数参照条件;

    [SerializeReference]
    public List<ISkillCondition> 枚数参照状態 = new List<ISkillCondition>();

    public bool 枚数参照で自分を除く;

    public override TargetList GetEffectTarget() => 効果対象;
    public override SearchCriteria GetTargetCriteria() => 対象条件;
    public override IReadOnlyList<ISkillCondition> GetTargetState() => 対象状態;

    public override async Task ActivateEffect(SkillContext context)
    {
        int damageAmount;

        if (効果数キー == SkillArg.枚数カウント)
        {
            damageAmount = CountMatchingCards(context) * 効果数;
        }
        else
        {
            damageAmount = ResolveKeyedAmount(context, 効果数, 効果数キー);
        }

        if (damageAmount <= 0)
            return;

        if (IsPlayerHpTarget(効果対象))
        {
            await DamagePlayers(context, damageAmount);
            return;
        }

        var toDamage = await CollectTargetsAsync(
            効果対象, 対象側, 対象条件, context, 対象状態);

        if (!GetSelectTarget().HasEnough(toDamage))
            return;

        var kinds = new List<string>();
        var ids = new List<int>();
        var zoneNames = new List<string>();
        var indices = new List<int>();
        var amounts = new List<int>();
        var deadKinds = new List<string>();
        var deadIds = new List<int>();
        var deadZones = new List<string>();
        var deadIndices = new List<int>();

        // 各バトラー・フィールドカードにダメージを与える。
        // 住所は墓地へ動かす前に取る
        foreach (var card in toDamage)
        {
            if (card == null) continue;
            if (card.cardKind != CardKind.バトラー && card.cardKind != CardKind.フィールド) continue;
            if (!ConditionsMet(対象状態, card)) continue;

            bool addressed = CardAddress.TryAdd(card, kinds, ids, zoneNames, indices);
            int damageToApply = IncomingDamageModifierSkill.ApplyToEffectDamage(card, damageAmount);
            await BattleManager.Instance.ShowEffect(card.transform.position, damageToApply, Color.yellow);
            card.curHp -= damageToApply;
            card.UpdateDisplay();
            if (damageToApply > 0)
                BattleManager.Instance.RequestStunResolve(card);

            if (!addressed) continue;
            amounts.Add(damageToApply);
            if (card.curHp <= 0 && !card.トークン)
                CardAddress.TryAdd(card, deadKinds, deadIds, deadZones, deadIndices);
        }

        if (BattleManager.Instance.IsOnlineMode && zoneNames.Count > 0)
        {
            OnlineSyncManager.Instance.SyncAreaDamage(
                kinds.ToArray(), ids.ToArray(), zoneNames.ToArray(),
                indices.ToArray(), amounts.ToArray());

            await OnlineSyncManager.Instance.WaitForOpponentAnimation();

            if (deadIndices.Count > 0)
            {
                OnlineSyncManager.Instance.SyncAreaGrave(
                    deadKinds.ToArray(), deadIds.ToArray(),
                    deadZones.ToArray(), deadIndices.ToArray());

                await OnlineSyncManager.Instance.WaitForOpponentAnimation();
            }
        }

        await Task.Delay(300);

        foreach (var card in toDamage)
        {
            if (card == null || !ConditionsMet(対象状態, card) || card.curHp > 0)
                continue;

            // 通常カードの墓地同期は直前で送ってある。
            // トークンだけ、ここで相手側の消滅も同期する。
            await BattleManager.Instance.HandleGraveyardTransition(
                card, syncOnline: card.トークン);
        }

        await Task.CompletedTask;
    }

    // ----------------------------
    // 枚数カウント
    // ----------------------------

    int CountMatchingCards(SkillContext context)
    {
        if (枚数参照場所 == null ||
            枚数参照場所.Count == 0 ||
            BattleManager.Instance == null)
            return 0;

        var source = context?.発動カード;
        bool isMyCard = source == null || source.isMyCard;

        var seen = new HashSet<CardController>();
        int count = 0;

        foreach (var zoneType in 枚数参照場所)
        {
            if (zoneType == CardZoneType.None)
                continue;

            // デッキはカード状態を参照できないため、
            // 枚数参照状態が指定されている場合は数えない。
            if (zoneType == CardZoneType.プレイヤーデッキ ||
                zoneType == CardZoneType.相手デッキ)
            {
                if (!HasCountState())
                    count += CountDeck(zoneType, isMyCard, context);

                continue;
            }

            Transform zone = CardZones.Of(zoneType, isMyCard);
            if (zone == null)
                continue;

            foreach (var card in zone.GetComponentsInChildren<CardController>())
            {
                if (card == null || !seen.Add(card))
                    continue;

                if (枚数参照で自分を除く && source != null && card == source)
                    continue;

                if (枚数参照条件 != null &&
                    !card.MeetsCriteria(枚数参照条件, context))
                    continue;

                if (!ConditionsMet(枚数参照状態, card))
                    continue;

                count++;
            }
        }

        return count;
    }

    int CountDeck(
        CardZoneType zoneType,
        bool isMyCard,
        SkillContext context)
    {
        var deck = CardZones.Deck(zoneType, isMyCard);
        if (deck == null)
            return 0;

        int count = 0;

        foreach (var deckCard in deck)
        {
            if (DeckMatches(deckCard, context))
                count++;
        }

        return count;
    }

    bool DeckMatches(DeckCard deckCard, SkillContext context)
    {
        if (deckCard == null)
            return false;

        if (枚数参照条件 == null)
            return true;

        return 枚数参照条件.MatchesDeck(
            deckCard,
            context?.発動カード,
            checkSource: true,
            selfMatchesSameCard: false,
            nullSourcePassesExclude: true);
    }

    bool HasCountState()
    {
        if (枚数参照状態 == null)
            return false;

        for (int i = 0; i < 枚数参照状態.Count; i++)
        {
            if (枚数参照状態[i] != null)
                return true;
        }

        return false;
    }

    // ----------------------------
    // ダメージ処理
    // ----------------------------

    public static bool IsPlayerHpTarget(TargetList target)
    {
        return target == TargetList.プレイヤー;
    }

    async Task DamagePlayers(SkillContext context, int damageAmount)
    {
        var battle = BattleManager.Instance;
        if (battle == null || damageAmount <= 0)
            return;

        bool sourceIsPlayer = context?.発動カード == null || context.発動カード.isMyCard;
        bool hitOwnSide = TargetScope.IncludesOwner(対象側);
        bool hitOpponentSide = TargetScope.IncludesOpponent(対象側);

        int damageToMyHp = 0;
        int damageToEnemyHp = 0;

        if (hitOwnSide)
        {
            if (sourceIsPlayer) damageToMyHp = damageAmount;
            else damageToEnemyHp = damageAmount;
        }

        if (hitOpponentSide)
        {
            if (sourceIsPlayer) damageToEnemyHp += damageAmount;
            else damageToMyHp += damageAmount;
        }

        if (damageToMyHp > 0)
            await DamagePlayerHp(
                battle.pResources?.Hp, battle.pFieldZone, damageToMyHp);

        if (damageToEnemyHp > 0)
            await DamagePlayerHp(
                battle.eResources?.Hp, battle.eFieldZone, damageToEnemyHp);

        if (battle.IsOnlineMode &&
            OnlineSyncManager.Instance != null &&
            (damageToMyHp > 0 || damageToEnemyHp > 0))
        {
            OnlineSyncManager.Instance.SyncDirectHpDamage(
                damageToMyHp, damageToEnemyHp);

            await OnlineSyncManager.Instance.WaitForOpponentAnimation();
        }
    }

    static async Task DamagePlayerHp(
        ResourceSystem hp,
        Transform effectAnchor,
        int amount)
    {
        if (hp == null || amount <= 0)
            return;

        hp.TakeDamage(amount);

        if (effectAnchor != null)
            await BattleManager.Instance.ShowEffect(
                effectAnchor.position, amount, Color.yellow);
    }

    protected override void CopyPolymorphicFieldsTo(SkillDB clone)
    {
        if (clone is DamageSkill damage)
        {
            damage.対象状態 = 対象状態 != null
                ? new List<ISkillCondition>(対象状態)
                : new List<ISkillCondition>();

            damage.枚数参照状態 = 枚数参照状態 != null
                ? new List<ISkillCondition>(枚数参照状態)
                : new List<ISkillCondition>();
        }
    }
}
