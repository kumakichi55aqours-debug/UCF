using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CardIconManager : MonoBehaviour
{
    public static CardIconManager Instance { get; private set; }

    [SerializeField] private List<CardTypeSprite> cardTypeSprites;
    [SerializeField] private List<WeakElementSprite> elementSprites;
    [Header("作戦/フィールド")]
    [SerializeField] private Sprite tacticsSprite;
    [SerializeField] private Sprite fieldSprite;

    [Header("状態異常")]
    [SerializeField] private List<StatusAilmentSprite> statusAilmentSprites = new List<StatusAilmentSprite>();

    private Dictionary<CardTypeList, Sprite> cardTypeDictionary;
    private Dictionary<ElementList, Sprite> elementDictionary;
    private Dictionary<StatusAilmentType, Sprite> statusAilmentDictionary;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        BuildDictionaries();
    }

    private void BuildDictionaries()
    {
        cardTypeDictionary = new Dictionary<CardTypeList, Sprite>();
        foreach (var item in cardTypeSprites)
        {
            cardTypeDictionary[item.type] = item.sprite;
        }

        elementDictionary = new Dictionary<ElementList, Sprite>();
        foreach (var item in elementSprites)
        {
            elementDictionary[item.element] = item.sprite;
        }

        statusAilmentDictionary = new Dictionary<StatusAilmentType, Sprite>();
        if (statusAilmentSprites == null) return;
        foreach (var item in statusAilmentSprites)
        {
            if (item == null || item.sprite == null) continue;
            statusAilmentDictionary[item.type] = item.sprite;
        }
    }

    public Sprite GetCardTypeSprite(CardTypeList type)
    {
        cardTypeDictionary.TryGetValue(type, out Sprite sprite);
        return sprite;
    }

    public Sprite GetCardKindSprite(CardKind kind)
    {
        switch (kind)
        {
            case CardKind.作戦:
                return tacticsSprite;
            case CardKind.フィールド:
                return fieldSprite;
            default:
                return null;
        }
    }

    // 
    public Sprite GetElementSprite(ElementList element)
    {
        elementDictionary.TryGetValue(element, out Sprite sprite);
        return sprite;
    }

    public Sprite GetStatusAilmentSprite(StatusAilmentType type)
    {
        if (statusAilmentDictionary == null) return null;
        statusAilmentDictionary.TryGetValue(type, out Sprite sprite);
        return sprite;
    }
}

