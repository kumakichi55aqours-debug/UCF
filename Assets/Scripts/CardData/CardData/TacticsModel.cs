using UnityEngine;

public class TacticsModel : CardDBBase
{
    public CardTypeList type;

    public void Init(int cardID)
    {
        TacticsDB cardDB = Resources.Load<TacticsDB>("CardDBList/作戦/" + cardID);
        分類 = cardDB.分類;
        ID = cardDB.ID;
        type = cardDB.type;
        カード名 = cardDB.カード名;
        レア度 = cardDB.レア度;
        画像 = cardDB.画像;
        コスト = cardDB.コスト;
        カテゴリ = cardDB.カテゴリ;

        スキル = cardDB.スキル;
        テキスト = cardDB.テキスト;
    }
}
