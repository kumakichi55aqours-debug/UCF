using UnityEngine;

public class FieldModel : CardDBBase
{
    public int maxHp;

    public void Init(int cardID)
    {
        FieldDB cardDB = Resources.Load<FieldDB>("CardDBList/フィールド/" + cardID);
        cardKind = cardDB.cardKind;
        cardId = cardDB.cardId;
        cardName = cardDB.cardName;
        rarity = cardDB.rarity;
        artwork = cardDB.artwork;
        maxHp = cardDB.hp;
        cost = cardDB.cost;
        category = cardDB.category;

        skills = cardDB.skills;
        skillDescription = cardDB.skillDescription;
    }
}


