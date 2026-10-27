using UnityEngine;

public interface ISkillCondition
{
    bool IsConditionMet(CardController card);
    bool IsConditionMet(SkillContext context);
}
