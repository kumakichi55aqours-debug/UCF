using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SkillDisplayNameAttribute : Attribute
{
    public string Name { get; }
    public SkillDisplayNameAttribute(string name) => Name = name;
}

[Serializable]
public abstract class SkillDB
{
    [SerializeField] public string スキル名;
    [SerializeField] public ActiveTiming タイミング;
    // 入っている場所のどれかと一致すれば発動できる
    [SerializeField] public List<ActiveLocation> 場所リスト = new List<ActiveLocation>();
    [SerializeField] public int 上限回数 = 1;
    [SerializeField] public bool 強制発動;
    // 登場時だけ見る。true はその登場では発動しない。
    // 未保存の既存スキルは false のままなので、通常登場と特殊登場の両方で発動する。
    [HideInInspector] public bool 通常登場を除く;
    [HideInInspector] public bool 特殊登場を除く;

    [SerializeField] public int 使用回数;

    [SerializeReference] public List<ISkillCondition> 条件 = new List<ISkillCondition>();

    public SearchCriteria トリガー対象条件;
    // 「墓地に送られた時」「他のカードが墓地に送られた時」で、送られた元のゾーン。空ならどこからでも
    // 「プレイヤー」はそのスキルを持つカード側、「相手」は反対側。複数指定したときはどれかと一致すればよい
    public List<CardZoneType> どこから = new List<CardZoneType>();
    // 「他のバトラーが撃破した時」で、撃破したバトラーがいるゾーン。空ならどこにいても
    // 「プレイヤー」はそのスキルを持つカード側、「相手」は反対側。複数指定したときはどれかと一致すればよい
    public List<CardZoneType> いる場所 = new List<CardZoneType>();
    // 「自分のフェイズ開始時」「自分のフェイズ終了時」のとき、どのフェイズかを見る
    public TurnPhase 対象フェイズ = TurnPhase.ドローフェイズ;
    // トリガー対象のカードの側。プレイヤーは、このスキルを持つカード側。両方は側を見ない。
    public PlayerType 発動側 = PlayerType.両方;

    public bool HasRemainingUses => 使用回数 < 上限回数;

    /// <summary>
    /// トリガーの対象カードが、このスキルを持つカードから見て指定の側か。
    /// 両方は通す。プレイヤーか相手を指定していて対象がないときは通さない。
    /// </summary>
    public bool MatchesActivationOwner(CardController owner, CardController subject)
    {
        if (発動側 == PlayerType.両方)
            return true;
        if (owner == null || subject == null)
            return false;

        bool sameSide = subject.isMyCard == owner.isMyCard;
        return 発動側 == PlayerType.プレイヤー ? sameSide : !sameSide;
    }

    // カードの使用可能判定
    public bool TryConsumeUse(CardController card)
    {
        if (!HasRemainingUses) return false;

        if (card != null)
            SkillUseCountStore.Consume(card, this);
        else
            使用回数++;

        return true;
    }

    public bool CanUseAt(ActiveTiming timing, ActiveLocation currentLocation, SkillContext context)
    {
        return タイミング == timing
            && IncludesLocation(currentLocation)
            && CanActivate(context);
    }

    /// <summary>
    /// 登場時スキルが、今回の登場で発動するか。
    /// 通常登場は登場ボタンから場に出したとき。特殊登場はスキルで場に出したとき。
    /// 登場時以外は常に通す。
    /// </summary>
    public bool MatchesAppearKind(bool specialSummon)
    {
        if (タイミング != ActiveTiming.登場時)
            return true;
        return specialSummon ? !特殊登場を除く : !通常登場を除く;
    }

    public bool IncludesLocation(ActiveLocation location)
    {
        if (場所リスト == null)
            return false;

        for (int i = 0; i < 場所リスト.Count; i++)
        {
            if (場所リスト[i] == location)
                return true;
        }

        return false;
    }

    public string LocationKey()
    {
        if (場所リスト == null || 場所リスト.Count == 0)
            return "";

        var values = new int[場所リスト.Count];
        for (int i = 0; i < 場所リスト.Count; i++)
            values[i] = (int)場所リスト[i];
        Array.Sort(values);
        return string.Join(",", values);
    }

    public string GetDisplayTypeName()
    {
        var attr = (SkillDisplayNameAttribute)Attribute.GetCustomAttribute(GetType(), typeof(SkillDisplayNameAttribute));
        return attr != null ? attr.Name : GetType().Name;
    }

    // TargetList から対象を選ぶスキルだけ、インスペクターに出す。
    [HideInInspector]
    public SelectTarget 対象選択 = new SelectTarget();

    public SelectTarget GetSelectTarget()
    {
        if (対象選択 == null)
            対象選択 = new SelectTarget();
        return 対象選択;
    }

    protected Task<List<CardController>> CollectTargetsAsync(TargetList targetList, PlayerType side, SearchCriteria criteria, SkillContext context)
    {
        return TargetSelectionManager.CollectTargetsAsync(targetList, side, criteria, context, GetSelectTarget());
    }

    public virtual TargetList GetEffectTarget() => TargetList.None;
    public virtual SearchCriteria GetTargetCriteria() => null;

    public virtual bool CanActivate(SkillContext context)
    {
        if (!HasRemainingUses) return false;

        if (条件 == null || 条件.Count == 0)
        {
            return true;
        }

        foreach (var condition in 条件)
        {
            if (condition == null) continue;
            if (!condition.IsConditionMet(context))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// このスキルの発動条件を、カードに対して満たしている。条件が空なら満たしている。
    /// </summary>
    public bool ConditionsMet(CardController card)
    {
        return ConditionsMet(条件, card);
    }

    public static bool ConditionsMet(IReadOnlyList<ISkillCondition> conditions, CardController card)
    {
        if (conditions == null || conditions.Count == 0)
            return true;

        for (int i = 0; i < conditions.Count; i++)
        {
            if (conditions[i] != null && !conditions[i].IsConditionMet(card))
                return false;
        }

        return true;
    }

    /// <summary>
    /// 永続効果で、指定の場所にいて、カードに対する発動条件を満たしている。
    /// </summary>
    public bool IsPersistentAt(CardController card, ActiveLocation location)
    {
        return タイミング == ActiveTiming.永続効果
            && IncludesLocation(location)
            && ConditionsMet(card);
    }

    /// <summary>
    /// 技1は0、技2は1。両方でも、それ以外の番号には当てない。
    /// </summary>
    public static bool AppliesToMove(MoveTarget target, int moveIndex)
    {
        if (moveIndex != 0 && moveIndex != 1)
            return false;

        return target == MoveTarget.両方
            || (target == MoveTarget.技1 && moveIndex == 0)
            || (target == MoveTarget.技2 && moveIndex == 1);
    }

    /// <summary>
    /// 技番号に当て、属性リストが空ならすべての属性に当てる。
    /// </summary>
    protected static bool AppliesToMoveElement(MoveTarget target, IReadOnlyList<ElementList> elements, ElementList moveElement, int moveIndex)
    {
        if (!AppliesToMove(target, moveIndex))
            return false;
        return elements == null || elements.Count == 0 || elements.Contains(moveElement);
    }

    /// <summary>
    /// 技番号に当て、指定属性が None ならすべての属性に当てる。
    /// </summary>
    protected static bool AppliesToMoveElement(MoveTarget target, ElementList required, ElementList moveElement, int moveIndex)
    {
        if (!AppliesToMove(target, moveIndex))
            return false;
        return required == ElementList.None || moveElement == required;
    }

    /// <summary>
    /// 効果の対象を、今のコンテキストのカードだけに狭める。狭めないときはそのまま返す。
    /// </summary>
    protected static List<CardController> KeepContextCard(List<CardController> targets, SkillContext context)
    {
        if (targets == null)
            targets = new List<CardController>();
        if (context == null || !context.RestrictTargetsToContextCard || context.対象カード == null)
            return targets;

        if (!targets.Contains(context.対象カード))
            targets.Add(context.対象カード);

        var only = new List<CardController>(1);
        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i] == context.対象カード)
                only.Add(targets[i]);
        }
        return only;
    }

    public static IEnumerable<T> PrintedSkills<T>(CardController card) where T : SkillDB
    {
        if (card?.skills == null)
            yield break;

        foreach (var skill in card.skills)
        {
            if (skill is T typed)
                yield return typed;
        }
    }

    public static IEnumerable<T> GrantedSkills<T>(CardController card) where T : SkillDB
    {
        if (card?.passiveEffects == null)
            yield break;

        foreach (var effect in card.passiveEffects)
        {
            if (effect?.PassiveEffect is T typed)
                yield return typed;
        }
    }

    public abstract Task ActivateEffect(SkillContext context);

    /// <summary>
    /// 並んだサブスキルを順に実行する。キャンセルされたら、残りは実行しない。
    /// </summary>
    protected async Task ActivateSubSkills(List<SkillDB> skills, SkillContext context)
    {
        if (skills == null || skills.Count == 0)
            return;

        foreach (var skill in skills)
        {
            if (skill == null)
                continue;

            try
            {
                await skill.ActivateEffect(context);
            }
            catch (SkillExecutionCancelledException ex)
            {
                Debug.Log($"[{GetType().Name}] サブスキルがキャンセルされました: {skill.スキル名} - {ex.Message}");
                break;
            }
        }
    }

    public SkillDB Clone()
    {
        var clone = (SkillDB)Activator.CreateInstance(GetType());
        JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(this), clone);
        CopySearchCriteriaFields(this, clone);
        clone.条件 = 条件 != null ? new List<ISkillCondition>(条件) : new List<ISkillCondition>();
        clone.使用回数 = 0;
        CopyPolymorphicFieldsTo(clone);
        return clone;
    }

    // JsonUtility は対象条件の移行結果を落とすことがある。発動に使う複製は、今の条件をそのまま持つ。
    static void CopySearchCriteriaFields(SkillDB source, SkillDB destination)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
        foreach (var field in source.GetType().GetFields(flags))
        {
            if (field.FieldType != typeof(SearchCriteria))
                continue;

            var criteria = field.GetValue(source) as SearchCriteria;
            field.SetValue(destination, criteria != null ? criteria.Clone() : null);
        }
    }

    public static List<SkillDB> CloneList(IEnumerable<SkillDB> source)
    {
        if (source == null) return new List<SkillDB>();
        return source.Where(s => s != null).Select(s => s.Clone()).ToList();
    }

    protected virtual void CopyPolymorphicFieldsTo(SkillDB clone)
    {
    }

    protected static List<SkillDB> CloneSkillList(List<SkillDB> source)
    {
        return CloneList(source);
    }
}

/// <summary>
/// 1件分のカード条件。リストに並べたときは、いずれか1件に合致すれば対象になる。
/// カテゴリを複数入れたときは、そのすべてを持っているカードだけが対象。
/// </summary>
[Serializable]
public class CardSearchFilter
{
    public CardKind カードの種類;
    public int カードID;
    // -1 のときはコストで絞らない
    public int コスト = -1;
    public MatchType コスト比較 = MatchType.同じ;
    // インスペクターで足した直後の空行か。描画時にコスト -1 と「同じ」へ直す。
    [HideInInspector] public bool コスト比較設定済み;
    public List<CardTypeList> カードのタイプ = new List<CardTypeList>();
    public List<CardCategoryList> カードのカテゴリ = new List<CardCategoryList>();
    public string サーチワード;

    public bool IsEmpty()
    {
        return カードの種類 == CardKind.None &&
               カードID == 0 &&
               コスト < 0 &&
               (カードのタイプ == null || カードのタイプ.Count == 0) &&
               (カードのカテゴリ == null || カードのカテゴリ.Count == 0) &&
               string.IsNullOrEmpty(サーチワード);
    }
}

/// <summary>
/// ソースカード条件は全体で1つ。それ以外はフィルタのリストで、いずれか1件に合致すれば対象。
/// </summary>
[Serializable]
public class SearchCriteria
{
    public SourceCardType ソースカード条件 = SourceCardType.None;
    public List<CardSearchFilter> フィルタ = new List<CardSearchFilter>();

    public bool IsEmpty()
    {
        return ソースカード条件 == SourceCardType.None && !HasActiveFilter();
    }

    public bool HasCondition()
    {
        return !IsEmpty();
    }

    public SearchCriteria Clone()
    {
        var copy = new SearchCriteria
        {
            ソースカード条件 = ソースカード条件,
            フィルタ = new List<CardSearchFilter>()
        };

        if (フィルタ == null)
            return copy;

        for (int i = 0; i < フィルタ.Count; i++)
        {
            var filter = フィルタ[i];
            if (filter == null)
                continue;

            copy.フィルタ.Add(new CardSearchFilter
            {
                カードの種類 = filter.カードの種類,
                カードID = filter.カードID,
                コスト = filter.コスト,
                コスト比較 = filter.コスト比較,
                コスト比較設定済み = true,
                カードのタイプ = filter.カードのタイプ != null ? new List<CardTypeList>(filter.カードのタイプ) : new List<CardTypeList>(),
                カードのカテゴリ = filter.カードのカテゴリ != null ? new List<CardCategoryList>(filter.カードのカテゴリ) : new List<CardCategoryList>(),
                サーチワード = filter.サーチワード
            });
        }

        return copy;
    }

    public bool MatchesCard(
        CardController card,
        CardController source,
        bool checkSource,
        bool partialName = false,
        IReadOnlyList<CardController> processedCards = null)
    {
        if (card == null) return false;
        if (checkSource && !PassesCardSource(card, source, processedCards))
            return false;
        return PassesFilters(filter => FilterMatchesCard(card, filter, partialName));
    }

    public static bool AllowsDeck(SearchCriteria criteria, DeckCard deck, SkillContext context)
    {
        if (criteria == null)
            return true;
        return criteria.MatchesDeck(deck, context != null ? context.発動カード : null, checkSource: context != null);
    }

    public bool MatchesDeck(
        DeckCard deck,
        CardController source,
        bool checkSource,
        bool partialName = false,
        bool selfMatchesSameCard = true,
        bool nullSourcePassesExclude = false)
    {
        if (deck == null) return false;
        if (checkSource && !PassesDeckSource(deck, source, selfMatchesSameCard, nullSourcePassesExclude))
            return false;
        return PassesFilters(filter => FilterMatchesDeck(deck, filter, partialName));
    }

    bool HasActiveFilter()
    {
        if (フィルタ == null) return false;
        for (int i = 0; i < フィルタ.Count; i++)
        {
            if (フィルタ[i] != null && !フィルタ[i].IsEmpty())
                return true;
        }
        return false;
    }

    bool PassesFilters(Func<CardSearchFilter, bool> match)
    {
        if (フィルタ == null || フィルタ.Count == 0)
            return true;

        bool sawFilter = false;
        for (int i = 0; i < フィルタ.Count; i++)
        {
            var filter = フィルタ[i];
            if (filter == null || filter.IsEmpty())
                continue;
            sawFilter = true;
            if (match(filter))
                return true;
        }

        return !sawFilter;
    }

    bool PassesCardSource(CardController card, CardController source, IReadOnlyList<CardController> processedCards)
    {
        if (ソースカード条件 == SourceCardType.None)
            return true;

        if (ソースカード条件 == SourceCardType.処理したカード)
            return processedCards != null && processedCards.Contains(card);

        if (source == null)
            return false;

        switch (ソースカード条件)
        {
            case SourceCardType.Self:
                return card == source;
            case SourceCardType.ExcludeSelf:
                return !(card.cardID == source.cardID && card.cardKind == source.cardKind);
            default:
                return true;
        }
    }

    bool PassesDeckSource(DeckCard deck, CardController source, bool selfMatchesSameCard, bool nullSourcePassesExclude)
    {
        if (ソースカード条件 == SourceCardType.None)
            return true;

        // デッキ上の同名カードは、直前に処理した実体とは別物
        if (ソースカード条件 == SourceCardType.処理したカード)
            return false;

        if (ソースカード条件 == SourceCardType.Self)
        {
            if (!selfMatchesSameCard || source == null)
                return false;
            return deck.カード種類 == source.cardKind && deck.カードID == source.cardID;
        }

        if (ソースカード条件 == SourceCardType.ExcludeSelf)
        {
            if (source == null)
                return nullSourcePassesExclude;
            return !(deck.カード種類 == source.cardKind && deck.カードID == source.cardID);
        }

        return true;
    }

    static bool MatchesCost(int cardCost, CardSearchFilter filter)
    {
        if (filter.コスト < 0)
            return true;

        return filter.コスト比較.Matches(cardCost, filter.コスト);
    }

    static bool FilterMatchesCard(CardController card, CardSearchFilter filter, bool partialName)
    {
        if (filter.カードの種類 != CardKind.None && card.cardKind != filter.カードの種類)
            return false;
        if (filter.カードID != 0 && card.cardID != filter.カードID)
            return false;
        if (!MatchesCost(card.cost, filter))
            return false;

        if (filter.カードのカテゴリ != null && filter.カードのカテゴリ.Count > 0)
        {
            if (card.category == null)
                return false;
            if (!filter.カードのカテゴリ.All(category => card.category.Contains(category)))
                return false;
        }

        if (filter.カードのタイプ != null && filter.カードのタイプ.Count > 0)
        {
            CardTypeList cardType = card.type;
            if (cardType == CardTypeList.None && card.model is BattlerModel battlerModel)
                cardType = battlerModel.type;
            if (!filter.カードのタイプ.Contains(cardType))
                return false;
        }

        if (string.IsNullOrEmpty(filter.サーチワード))
            return true;
        if (string.IsNullOrEmpty(card.cardName))
            return false;
        return partialName ? card.cardName.Contains(filter.サーチワード) : card.cardName == filter.サーチワード;
    }

    static bool FilterMatchesDeck(DeckCard deck, CardSearchFilter filter, bool partialName)
    {
        if (filter.カードの種類 != CardKind.None && deck.カード種類 != filter.カードの種類)
            return false;
        if (filter.カードID != 0 && deck.カードID != filter.カードID)
            return false;
        if (!MatchesCost(deck.コスト, filter))
            return false;

        if (filter.カードのカテゴリ != null && filter.カードのカテゴリ.Count > 0)
        {
            if (deck.カテゴリ == null || deck.カテゴリ.Count == 0)
                deck.PopulateDerivedData();
            if (deck.カテゴリ == null)
                return false;
            if (!filter.カードのカテゴリ.All(category => deck.カテゴリ.Contains(category)))
                return false;
        }

        if (filter.カードのタイプ != null && filter.カードのタイプ.Count > 0)
        {
            if (deck.カード種類 != CardKind.バトラー)
                return false;
            if (deck.カードタイプ == CardTypeList.None)
                deck.PopulateDerivedData();
            if (!filter.カードのタイプ.Contains(deck.カードタイプ))
                return false;
        }

        return deck.MatchesSearchWord(filter.サーチワード, exactMatch: !partialName);
    }
}

public enum DamageModifierType
{
    固定値,
    パーセント
}

public enum DamageNullificationType
{
    完全無効化,
    固定値減算
}

public enum TurnRestrictionType
{
    特定の技,
    属性制限,
    全ての技
}
