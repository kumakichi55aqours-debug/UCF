using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 自分のBゾーンにいる宇宙人と同じバトラーを、デッキ外のトークンとして出す。
/// 技・タイプ・カテゴリはコピー元のカードデータと同じ。
/// HPを設定するときだけ、現在HPと最大HPをその値にする。オフのときはコピー元のHPのまま。
/// 場に乗っているパワー補正や状態異常はコピーしない。
/// 同一カード制限では止めない。Bゾーンが5体のときは出さない。
/// </summary>
[System.Serializable]
[SkillDisplayName("分身")]
public class CloneTokenSkill : SkillDB
{
    const int BattlerZoneLimit = 5;

    [SerializeField] bool HPを設定する = true;
    [SerializeField] int 設定HP = 1;

    public override bool CanActivate(SkillContext context)
    {
        if (!base.CanActivate(context))
            return false;

        BattleManager battle = BattleManager.Instance;
        if (battle == null)
            return true;

        Transform zone = OwnerBattlerZone(battle, context);
        return zone != null
            && zone.childCount < BattlerZoneLimit
            && ListAliens(zone).Count > 0;
    }

    public override async Task ActivateEffect(SkillContext context)
    {
        BattleManager battle = BattleManager.Instance;
        if (battle == null)
            return;

        bool ownerIsLocal = context?.発動カード == null || context.発動カード.isMyCard;
        Transform zone = OwnerBattlerZone(battle, context);
        if (zone == null || zone.childCount >= BattlerZoneLimit)
        {
            Debug.Log("CloneTokenSkill: Bゾーンに空きがないため分身を出しません。");
            return;
        }

        List<CardController> aliens = ListAliens(zone);
        if (aliens.Count == 0)
        {
            Debug.Log("CloneTokenSkill: Bゾーンに宇宙人がいないため分身を出しません。");
            return;
        }

        CardController source = await ChooseSource(aliens, context);
        if (source == null || BattlerAssets.Load(source.cardID, out _) == null)
        {
            Debug.LogWarning("CloneTokenSkill: コピー元のバトラーが見つかりません。");
            return;
        }

        if (zone.childCount >= BattlerZoneLimit)
            return;

        if (battle.IsOnlineMode && ownerIsLocal && OnlineSyncManager.Instance != null)
        {
            string zoneName = battle.GetZoneName(zone);
            OnlineSyncManager.Instance.SyncCloneTokenSummon(
                source.cardID,
                BattleManager.CovertOpponentZoneName(zoneName),
                HPを設定する,
                設定HP);
        }

        CardController token = await Summon(battle, source.cardID, zone, HPを設定する, 設定HP);
        if (token != null)
            context?.SetProcessedCards(new[] { token });
    }

    /// <summary>
    /// 指定IDのバトラーをトークンにして zone へ出す。出せなければ null。
    /// setHp が真のとき、現在HPと最大HPを hp にする。
    /// </summary>
    public static async Task<CardController> Summon(BattleManager battle, int cardId, Transform zone, bool setHp, int hp)
    {
        if (battle == null || zone == null || cardId <= 0)
            return null;
        if (zone.childCount >= BattlerZoneLimit)
        {
            Debug.Log("CloneTokenSkill: Bゾーンに空きがないため分身を出しません。");
            return null;
        }
        if (BattlerAssets.Load(cardId, out _) == null)
        {
            Debug.LogWarning($"CloneTokenSkill: バトラーが見つかりません。カードID={cardId}");
            return null;
        }

        Transform parent = battle.mainCanvas != null ? battle.mainCanvas.transform : zone;
        CardController card = battle.CreateCard(CardKind.バトラー, cardId, parent);
        if (card == null || card.cardKind != CardKind.バトラー || string.IsNullOrEmpty(card.cardName))
        {
            if (card != null)
                Object.Destroy(card.gameObject);
            Debug.LogWarning($"CloneTokenSkill: 分身を作れません。カードID={cardId}");
            return null;
        }

        card.BecomeToken(setHp, hp);
        await battle.SummonRoutine(card, zone, playVoice: true, summonSource: SummonSource.生成, specialSummon: true);
        return card;
    }

    static Transform OwnerBattlerZone(BattleManager battle, SkillContext context)
    {
        bool ownerIsLocal = context?.発動カード == null || context.発動カード.isMyCard;
        return ownerIsLocal ? battle.pBattlerZone : battle.eBattlerZone;
    }

    static List<CardController> ListAliens(Transform zone)
    {
        var aliens = new List<CardController>();
        if (zone == null)
            return aliens;

        CardController[] cards = zone.GetComponentsInChildren<CardController>();
        for (int i = 0; i < cards.Length; i++)
        {
            CardController card = cards[i];
            if (card == null || card.cardKind != CardKind.バトラー)
                continue;
            if (card.category == null || !card.category.Contains(CardCategoryList.宇宙人))
                continue;
            aliens.Add(card);
        }

        return aliens;
    }

    static async Task<CardController> ChooseSource(List<CardController> aliens, SkillContext context)
    {
        if (aliens == null || aliens.Count == 0)
            return null;

        IReadOnlyList<CardController> choices = aliens;
        if (aliens.Count > 1 && CpuChooses(context))
            choices = new List<CardController> { aliens[Random.Range(0, aliens.Count)] };

        if (TargetSelectionManager.Instance == null)
            return choices.Count == 1 ? choices[0] : null;

        return await TargetSelectionManager.Instance.SelectOneFromAsync(choices, "分身する宇宙人を選択してください");
    }

    static bool CpuChooses(SkillContext context)
    {
        if (SkillContext.IsOfflineCpuResolving)
            return true;

        CardController source = context?.発動カード;
        return source != null
            && !source.isMyCard
            && BattleManager.Instance != null
            && !BattleManager.Instance.IsOnlineMode;
    }
}
