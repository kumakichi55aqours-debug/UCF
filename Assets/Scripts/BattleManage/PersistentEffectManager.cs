using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class PersistentEffectManager : MonoBehaviour
{
    public static PersistentEffectManager Instance { get; private set; }

    private Coroutine recalcCoroutine;
    private bool pendingRequest;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void OnEnable()
    {
        QueueRecalculation();
    }

    private void OnDisable()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private bool isRecalculating;

    public void QueueRecalculation()
    {
        pendingRequest = true;

        if (!isActiveAndEnabled || isRecalculating)
        {
            return;
        }

        if (recalcCoroutine == null)
        {
            recalcCoroutine = StartCoroutine(RecalculationRoutine());
        }
    }

    /// <summary>
    /// 手札へ入った直後など、次のフレームを待たずにコスト補正を付け直す。
    /// </summary>
    public void RecalculateImmediately()
    {
        if (!isActiveAndEnabled)
            return;

        if (isRecalculating)
        {
            pendingRequest = true;
            return;
        }

        isRecalculating = true;
        try
        {
            ApplyPersistentBonuses();
            RefreshAffectedCards();
        }
        finally
        {
            isRecalculating = false;
        }

        if (pendingRequest && recalcCoroutine == null)
            recalcCoroutine = StartCoroutine(RecalculationRoutine());
    }

    private IEnumerator RecalculationRoutine()
    {
        yield return null; // allow transforms/parents to settle this frame

        while (pendingRequest)
        {
            pendingRequest = false;
            yield return RecalculateAllEffects();
        }

        recalcCoroutine = null;
    }

    private IEnumerator RecalculateAllEffects()
    {
        if (BattleManager.Instance == null)
            yield break;

        ApplyPersistentBonuses();

        // 少し待機して非同期スキル処理が落ち着くのを促す
        yield return null;

        if (!pendingRequest)
            RefreshAffectedCards();
    }

    private void ApplyPersistentBonuses()
    {
        var battleManager = BattleManager.Instance;
        if (battleManager == null)
            return;

        var cards = GetAffectedCards(battleManager);
        foreach (var card in cards)
        {
            if (card.GetCurrentActiveLocation() == ActiveLocation.手札)
            {
                card.bonusCost = 0;
                continue;
            }

            card.ResetBonuses();
            card.damageBonus = 0;
            card.RefreshHitRate();

            if (card.invalidElements.Count > 0)
                card.ClearInvalidElements();
        }

        foreach (var card in cards)
            ApplyCardPersistentEffects(card);
    }

    private void RefreshAffectedCards()
    {
        var battleManager = BattleManager.Instance;
        if (battleManager == null)
            return;

        foreach (var card in GetAffectedCards(battleManager))
        {
            card.RecalculateModifiedPowers();
            if (card.GetCurrentActiveLocation() == ActiveLocation.手札)
            {
                var handView = card.GetComponent<BattleCardView>();
                if (handView == null || !handView.IsBack)
                    card.UpdateDisplay();
                continue;
            }

            card.RefreshHitRate();
            card.UpdateDisplay();
        }
    }

    private static List<CardController> GetAffectedCards(BattleManager battleManager)
    {
        var cards = GetBoardCards(battleManager);
        AppendZoneCards(cards, battleManager.pHandZone);
        AppendZoneCards(cards, battleManager.eHandZone);
        return cards
            .Where(c => c != null)
            .Distinct()
            .ToList();
    }

    private static List<CardController> GetBoardCards(BattleManager battleManager)
    {
        var cards = new List<CardController>();

        AppendZoneCards(cards, battleManager.pBattlerZone);
        AppendZoneCards(cards, battleManager.pTacticsZone);
        AppendZoneCards(cards, battleManager.pFieldZone);
        AppendZoneCards(cards, battleManager.eBattlerZone);
        AppendZoneCards(cards, battleManager.eTacticsZone);
        AppendZoneCards(cards, battleManager.eFieldZone);

        return cards
            .Where(c => c != null && IsBoardLocation(c.GetCurrentActiveLocation()))
            .Distinct()
            .ToList();
    }

    private static void AppendZoneCards(List<CardController> cards, Transform zone)
    {
        if (zone == null)
        {
            return;
        }

        cards.AddRange(zone.GetComponentsInChildren<CardController>(includeInactive: false));
    }

    private static bool IsBoardLocation(ActiveLocation location)
    {
        return location == ActiveLocation.Bゾーン
            || location == ActiveLocation.Tゾーン
            || location == ActiveLocation.Fゾーン;
    }

    private void ApplyCardPersistentEffects(CardController card)
    {
        var location = card.GetCurrentActiveLocation();
        if (!IsBoardLocation(location))
        {
            return;
        }

        foreach (var skillData in card.skills)
        {
            if (skillData.タイミング != ActiveTiming.永続効果)
            {
                continue;
            }

            if (!skillData.IncludesLocation(location))
            {
                continue;
            }

            if (skillData.条件 != null && skillData.条件.Count > 0)
            {
                bool conditionsMet = skillData.条件.All(c => c.IsConditionMet(card));
                if (!conditionsMet)
                {
                    continue;
                }
            }

            var skillInstance = skillData;
            if (skillInstance != null)
            {
                var context = new SkillContext(card, card, card.transform);
                _ = skillInstance.ActivateEffect(context);
            }
        }

        foreach (var passiveEffect in card.passiveEffects)
        {
            if (passiveEffect?.PassiveEffect == null)
            {
                continue;
            }

            var context = new SkillContext(card, card, card.transform);
            _ = passiveEffect.PassiveEffect.ActivateEffect(context);
        }
    }
}

