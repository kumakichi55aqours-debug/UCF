using System.Collections.Generic;
using UnityEngine;

public abstract class CardDBBase : ScriptableObject
{
    public string cardName;
    public CardKind cardKind;
    public int cardId;
    public CardRarity rarity;
    public Sprite artwork;
    public int cost;
    public List<CardCategoryList> category;

    [TextArea]
    public string skillDescription;

    [SerializeReference]
    public List<SkillDB> skills = new List<SkillDB>();
}
