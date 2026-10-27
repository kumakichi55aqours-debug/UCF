using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 手札からコストを払ってバトラーを出す通常の登場を禁止する。
/// 効果による登場（SpecialSummonSkill）は止めない。
/// カードに付けるときはタイミングをルール効果にする。
/// </summary>
[Serializable]
[SkillDisplayName("通常登場不可")]
public class CannotNormalSummonSkill : SkillDB
{
    public CannotNormalSummonSkill()
    {
        タイミング = ActiveTiming.ルール効果;
    }

    public override Task ActivateEffect(SkillContext context)
    {
        return Task.CompletedTask;
    }

    public static bool Blocks(CardController card)
    {
        return PrintedSkills<CannotNormalSummonSkill>(card).Any();
    }

    // CPU用のブロック処理(CPUカードはDeckCard)
    public static bool Blocks(DeckCard deckCard)
    {
        if (deckCard == null || deckCard.カード種類 != CardKind.バトラー)
            return false;

        var db = Resources.Load<BattlerDB>($"CardDBList/バトラー/{deckCard.カードID}");
        return db?.skills != null && db.skills.Any(s => s is CannotNormalSummonSkill);
    }
}
