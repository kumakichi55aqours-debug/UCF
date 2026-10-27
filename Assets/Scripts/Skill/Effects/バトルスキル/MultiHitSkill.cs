using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 技が命中したあと、同じ対象へヒットを重ねる。
/// 永続効果としてバトラーに置く。エネルギーは技の解決側が1回だけ払う。
/// 1回目は通常の命中判定。2回目以降は継続確率で打ち切り、最大回数で止める。
/// 途中でHPが0以下でも対象は変えず、撃破判定は追加ヒットが終わってから1回。
/// </summary>
[System.Serializable]
[SkillDisplayName("(〇付与)複数ヒット")]
public class MultiHitSkill : SkillDB
{
    public const int MaxHitsCap = 16;

    public MoveTarget 適用先 = MoveTarget.両方;

    [Min(1)]
    [Tooltip("命中を含めた最大ヒット数。1なら追加ヒットなし。")]
    public int 最大回数 = 1;

    [Range(0, 100)]
    [Tooltip("2ヒット目以降を出す確率（百分率）。外れた時点で打ち切る。")]
    public int 継続確率;

    public override Task ActivateEffect(SkillContext context)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// 命中したあとに重ねるヒット数。宣言側だけが引き、オンラインではその数を相手へ送る。
    /// </summary>
    public static int RollExtraHits(CardController attacker, int moveIndex)
    {
        if (!TryGetRule(attacker, moveIndex, out int maxHits, out int chance))
            return 0;

        int extras = 0;
        int possible = maxHits - 1;
        for (int i = 0; i < possible; i++)
        {
            if (UnityEngine.Random.Range(0, 100) < chance)
                extras++;
            else
                break;
        }

        Debug.Log($"{attacker.cardName} の技{moveIndex + 1}: 追加ヒット {extras} 回（最大 {maxHits}、継続 {chance}%）");
        return extras;
    }

    static bool TryGetRule(CardController attacker, int moveIndex, out int maxHits, out int chance)
    {
        maxHits = 1;
        chance = 0;
        if (attacker == null || attacker.cardKind != CardKind.バトラー)
            return false;

        bool found = false;
        foreach (var skill in ActiveSkills(attacker))
        {
            if (!AppliesToMove(skill.適用先, moveIndex))
                continue;

            int hits = Mathf.Clamp(skill.最大回数, 1, MaxHitsCap);
            int rate = Mathf.Clamp(skill.継続確率, 0, 100);
            if (!found || hits > maxHits || (hits == maxHits && rate > chance))
            {
                maxHits = hits;
                chance = rate;
                found = true;
            }
        }

        return found && maxHits > 1;
    }

    static System.Collections.Generic.IEnumerable<MultiHitSkill> ActiveSkills(CardController card)
    {
        foreach (var skill in PrintedSkills<MultiHitSkill>(card))
        {
            if (skill.IsPersistentAt(card, ActiveLocation.Bゾーン))
                yield return skill;
        }

        foreach (var skill in GrantedSkills<MultiHitSkill>(card))
        {
            if (skill.ConditionsMet(card))
                yield return skill;
        }
    }
}
