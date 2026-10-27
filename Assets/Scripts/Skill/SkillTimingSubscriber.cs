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
        EventBus.Publish(new SkillTriggerEvent(SkillTrigger.登場時, e.Card)
        {
            SpecialSummon = e.SpecialSummon
        });
    }

    private void OnUseFromHand(UseCardFromHandEvent e)
    {
        EventBus.Publish(new SkillTriggerEvent(SkillTrigger.カード使用時, e.Card));
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
    public SummonCompletedEvent(CardController card, bool specialSummon = false)
    {
        Card = card;
        SpecialSummon = specialSummon;
    }
}

public class UseCardFromHandEvent
{
    public CardController Card { get; }
    public UseCardFromHandEvent(CardController card) => Card = card;
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
    public TurnPhase Phase { get; set; } // 自分のフェイズ開始時・終了時で、入った／出たフェイズ
    public bool SpecialSummon { get; set; } // 登場時で、スキルによる登場か

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
