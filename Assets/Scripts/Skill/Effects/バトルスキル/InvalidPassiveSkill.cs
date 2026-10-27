using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

[System.Serializable]
[SkillDisplayName("属性無効")]
public class InvalidPassiveSkill : SkillDB
{
    [Header("特定属性攻撃の無効化")]
    [SerializeField] public List<ElementList> 無効化属性リスト = new List<ElementList>();

    public override Task ActivateEffect(SkillContext context)
    {
        if (!TargetSelectionManager.IsAttackTargetOnField(context?.対象カード))
            return Task.CompletedTask;

        context.対象カード.AddInvalidPassiveEffect(無効化属性リスト);

        Debug.Log($"{context.発動カード.cardName} が {context.対象カード.cardName} に属性無効化効果を付与（無効化属性: {string.Join(", ", 無効化属性リスト)}）");
        return Task.CompletedTask;
    }
}
