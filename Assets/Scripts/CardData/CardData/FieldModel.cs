using UnityEngine;

public class FieldModel : CardDBBase
{
    public int maxHp;

    public void Init(int cardID)
    {
        FieldDB cardDB = Resources.Load<FieldDB>("CardDBList/フィールド/" + cardID);
        分類 = cardDB.分類;
        ID = cardDB.ID;
        カード名 = cardDB.カード名;
        レア度 = cardDB.レア度;
        画像 = cardDB.画像;
        maxHp = cardDB.hp;
        コスト = cardDB.コスト;
        カテゴリ = cardDB.カテゴリ;

        スキル = cardDB.スキル;
        テキスト = cardDB.テキスト;
    }
}


