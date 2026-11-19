using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public abstract class CardDBBase : ScriptableObject
{
    public string カード名;
    public CardKind 分類;
    public int ID;
    public CardRarity レア度;
    public Sprite 画像;
    public int コスト;
    public List<CardCategoryList> カテゴリ;

    [TextArea]
    public string テキスト;

    [SerializeReference]
    public List<SkillDB> スキル = new List<SkillDB>();
}
