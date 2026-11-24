using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Serialization;

public class BattlerModel : CardDBBase
{
    public CardTypeList タイプ;
    public int maxHp;
    public ElementList 得意属性;
    public int 属性軽減値;
    public ElementList 弱点属性;
    public int 属性増加値;

    public string moveName1;
    public int energy1;
    public int power1;
    public ElementList moveElement1;
    public TargetList target1;
    public PlayerType 対象側1 = PlayerType.プレイヤー;
    public SelectTarget 対象選択1 = new SelectTarget();
    public string description1;

    public string moveName2;
    public int energy2;
    public int power2;
    public ElementList moveElement2;
    public TargetList target2;
    public PlayerType 対象側2 = PlayerType.プレイヤー;
    public SelectTarget 対象選択2 = new SelectTarget();
    public string description2;

    public AudioClip summonVoice;

    public List<SkillDB> passiveSkills;

    public bool トークン;

    public void Init(int cardID)
    {
        BattlerDB cardDB = BattlerAssets.Load(cardID, out bool token);
        if (cardDB == null) return;

        トークン = token;
        分類 = token ? CardKind.バトラー : cardDB.分類;
        ID = cardDB.ID;
        カード名 = cardDB.カード名;
        レア度 = cardDB.レア度;
        画像 = cardDB.画像;
        コスト = cardDB.コスト;
        タイプ = cardDB.タイプ;
        maxHp = cardDB.hp;
        得意属性 = cardDB.得意属性;
        属性軽減値 = cardDB.属性軽減値;
        弱点属性 = cardDB.弱点属性;
        属性増加値 = cardDB.属性増加値;
        カテゴリ = cardDB.カテゴリ;

        スキル = cardDB.スキル;
        テキスト = cardDB.テキスト;

        moveName1 = cardDB.技名1;
        energy1 = cardDB.EN1;
        power1 = cardDB.PW1;
        moveElement1 = cardDB.属性1;
        target1 = cardDB.対象1;
        対象側1 = cardDB.対象側1;
        対象選択1 = SelectTarget.Copy(cardDB.対象選択1);
        description1 = cardDB.効果1;

        moveName2 = cardDB.技名2;
        energy2 = cardDB.EN2;
        power2 = cardDB.PW2;
        moveElement2 = cardDB.属性2;
        target2 = cardDB.対象2;
        対象側2 = cardDB.対象側2;
        対象選択2 = SelectTarget.Copy(cardDB.対象選択2);
        description2 = cardDB.効果2;

        summonVoice = cardDB.ボイス;
    }

    public TargetList GetMoveTarget(int moveIndex)
    {
        return moveIndex == 0 ? target1 : target2;
    }

    public PlayerType GetMoveSide(int moveIndex)
    {
        return moveIndex == 0 ? 対象側1 : 対象側2;
    }

    public SelectTarget GetMoveSelect(int moveIndex)
    {
        var select = moveIndex == 0 ? 対象選択1 : 対象選択2;
        return select ?? new SelectTarget();
    }
}