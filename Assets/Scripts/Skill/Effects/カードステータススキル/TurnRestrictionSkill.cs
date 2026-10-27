using System;
using System.Threading.Tasks;
using UnityEngine;

[Serializable]
[SkillDisplayName("ターン制限")]
public class TurnRestrictionSkill : SkillDB
{
    public int 制限ターン数 = 1;
    public TurnRestrictionType 制限タイプ = TurnRestrictionType.特定の技;
    public int 制限対象技ID = -1;
    public ElementList 制限対象属性 = ElementList.None;

    public override Task ActivateEffect(SkillContext context)
    {
        context.発動カード.AddTurnRestriction(new TurnRestrictionEffect(
            制限ターン数,
            制限タイプ,
            制限対象技ID,
            制限対象属性));
        return Task.CompletedTask;
    }
}

// ターン制限効果のデータクラス
[System.Serializable]
public class TurnRestrictionEffect
{
    public int RemainingTurns { get; private set; }
    public TurnRestrictionType RestrictionType { get; private set; }
    public int RestrictedSkillID { get; private set; }
    public ElementList RestrictedElement { get; private set; }

    public TurnRestrictionEffect(int turns, TurnRestrictionType type, int skillID, ElementList element)
    {
        RemainingTurns = turns;
        RestrictionType = type;
        RestrictedSkillID = skillID;
        RestrictedElement = element;
    }

    // ターン経過時に呼ぶ
    public void Tick()
    {
        if (RemainingTurns > 0)
        {
            RemainingTurns--;
        }
    }

    public bool IsExpired() => RemainingTurns <= 0;

    // 指定された技が制限対象かどうかをチェック
    public bool IsSkillRestricted(int skillID, ElementList skillElement)
    {
        if (IsExpired()) return false;

        switch (RestrictionType)
        {
            case TurnRestrictionType.特定の技:
                return RestrictedSkillID == skillID;
                
            case TurnRestrictionType.属性制限:
                return RestrictedElement == skillElement;
                
            case TurnRestrictionType.全ての技:
                return true;
                
            default:
                return false;
        }
    }
}
