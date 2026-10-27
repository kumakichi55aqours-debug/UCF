using System;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 同じチェーンで、直前に組まれたスキルの効果本体を無効にする。
/// このスキル自身の効果が飛ばされたときは、直前には印が付かない。
/// </summary>
[Serializable]
[SkillDisplayName("効果を無効")]
public class NegatePreviousChainSkill : SkillDB
{
    public override Task ActivateEffect(SkillContext context)
    {
        return Task.CompletedTask;
    }
}
