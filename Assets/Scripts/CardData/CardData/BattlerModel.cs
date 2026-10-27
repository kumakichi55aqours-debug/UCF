using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class BattlerModel : CardDBBase
{
    public CardTypeList type;
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

    public void Init(int cardID)
    {
        BattlerDB cardDB = Resources.Load<BattlerDB>("CardDBList/バトラー/" + cardID);
        cardKind = cardDB.cardKind;
        cardId = cardDB.cardId;
        cardName = cardDB.cardName;
        rarity = cardDB.rarity;
        artwork = cardDB.artwork;
        cost = cardDB.cost;
        type = cardDB.type;
        maxHp = cardDB.hp;
        得意属性 = cardDB.得意属性;
        属性軽減値 = cardDB.属性軽減値;
        弱点属性 = cardDB.弱点属性;
        属性増加値 = cardDB.属性増加値;
        category = cardDB.category;

        skills = cardDB.skills;
        skillDescription = cardDB.skillDescription;

        moveName1 = cardDB.moveName1;
        energy1 = cardDB.energy1;
        power1 = cardDB.power1;
        moveElement1 = cardDB.moveElement1;
        target1 = cardDB.target1;
        対象側1 = cardDB.対象側1;
        対象選択1 = SelectTarget.Copy(cardDB.対象選択1);
        description1 = cardDB.description1;

        moveName2 = cardDB.moveName2;
        energy2 = cardDB.energy2;
        power2 = cardDB.power2;
        moveElement2 = cardDB.moveElement2;
        target2 = cardDB.target2;
        対象側2 = cardDB.対象側2;
        対象選択2 = SelectTarget.Copy(cardDB.対象選択2);
        description2 = cardDB.description2;

        summonVoice = cardDB.summonVoice;
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