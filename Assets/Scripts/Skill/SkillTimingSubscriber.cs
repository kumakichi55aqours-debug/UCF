using Unity.VisualScripting;
using UnityEngine;

public class SkillTimingSubscriber : MonoBehaviour
{
    private void OnEnable()
    {
        EventBus.Subscribe<SummonCompletedEvent>(OnSummonCompleted);
        EventBus.Subscribe<UseCardFromHandEvent>(OnUseFromHand);
        EventBus.Subscribe<CardSentToGraveyardEvent>(OnCardSentToGraveyard);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<SummonCompletedEvent>(OnSummonCompleted);
        EventBus.Unsubscribe<UseCardFromHandEvent>(OnUseFromHand);
        EventBus.Unsubscribe<CardSentToGraveyardEvent>(OnCardSentToGraveyard);
    }

    private void OnSummonCompleted(SummonCompletedEvent e)
    {
        // バトラー／フィールドの登場。作戦は UseCardFromHand 側へ寄せる。
        EventBus.Publish(new SkillTriggerEvent(SkillTrigger.登場時, e.Card, null, null, e.FromLocation)
        {
            SpecialSummon = e.SpecialSummon
        });
    }

    private void OnUseFromHand(UseCardFromHandEvent e)
    {
        // 作戦登場の内部トリガー。スキルデータは登場時を参照し、チェーン終了後に強化以外を墓地へ送る。
        EventBus.Publish(new SkillTriggerEvent(SkillTrigger.カード使用時, e.Card, null, null, e.FromLocation)
        {
            SpecialSummon = e.SpecialSummon
        });
    }

    private void OnCardSentToGraveyard(CardSentToGraveyardEvent e)
    {
        if (e.SuppressSkillTrigger) return;
        EventBus.Publish(new SkillTriggerEvent(SkillTrigger.墓地に送られた時, e.Card, null, null, e.FromLocation));
    }
}
public class SummonCompletedEvent
{
    public CardController Card { get; }
    public bool SpecialSummon { get; }
    public CardZoneType FromLocation { get; }
    public SummonCompletedEvent(CardController card, bool specialSummon = false, CardZoneType fromLocation = CardZoneType.None)
    {
        Card = card;
        SpecialSummon = specialSummon;
        FromLocation = fromLocation;
    }
}

/// <summary>
/// 作戦登場のシグナル。Subscriber がカード使用時（内部トリガー）へ変換する。
/// スキルデータは登場時。チェーン終了後に強化以外は墓地へ送る。
/// </summary>
public class UseCardFromHandEvent
{
    public CardController Card { get; }
    public CardZoneType FromLocation { get; }
    public bool SpecialSummon { get; }
    public UseCardFromHandEvent(CardController card, CardZoneType fromLocation = CardZoneType.None, bool specialSummon = false)
    {
        Card = card;
        FromLocation = fromLocation;
        SpecialSummon = specialSummon;
    }
}

public class CardSentToGraveyardEvent
{
    public CardController Card { get; }
    public CardZoneType FromLocation { get; }
    public bool SuppressSkillTrigger { get; }
    public CardSentToGraveyardEvent(CardController card, CardZoneType fromLocation = CardZoneType.None, bool suppressSkillTrigger = false) 
    {
        Card = card;
        FromLocation = fromLocation;
        SuppressSkillTrigger = suppressSkillTrigger;
    }
}

public class SkillTriggerEvent
{
    public SkillTrigger Trigger { get; }
    public CardController SourceCard { get; }
    public CardController MoveTarget { get; }
    public SkillContext Context { get; set; } // 技を受ける時などで使用
    public CardZoneType FromLocation { get; set; } // 墓地に送られた時などで使用。ローカルプレイヤー基準の絶対ゾーン
    public TurnPhase Phase { get; set; } // フェイズ開始時・終了時で、入った／出たフェイズ
    public bool SpecialSummon { get; set; } // 登場時で、スキルによる登場か
    // 手札に戻ったカードが、ローカルプレイヤーのカードか。戻ったあとの実体は持たない
    public bool SubjectIsMyCard { get; set; }

    public SkillTriggerEvent(SkillTrigger trigger, CardController source, CardController moveTarget = null, SkillContext context = null, CardZoneType fromLocation = CardZoneType.None)
    {
        Trigger = trigger;
        SourceCard = source;
        MoveTarget = moveTarget;
        Context = context;
        FromLocation = fromLocation;
    }
}

public class SkillActivationCompletedEvent
{
    public CardController Card { get; }
    public SkillActivationCompletedEvent(CardController card) => Card = card;
}
