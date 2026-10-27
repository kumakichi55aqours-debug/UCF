using UnityEngine;

public class TacticsModel : CardDBBase
{
    public void Init(int cardID)
    {
        TacticsDB cardDB = Resources.Load<TacticsDB>("CardDBList/作戦/" + cardID);
        cardKind = cardDB.cardKind;
        cardId = cardDB.cardId;
        cardName = cardDB.cardName;
        rarity = cardDB.rarity;
        artwork = cardDB.artwork;
        cost = cardDB.cost;
        category = cardDB.category;

        skills = cardDB.skills;
        skillDescription = cardDB.skillDescription;
    }
}
