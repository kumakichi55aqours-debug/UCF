using System;
using UnityEngine;

public enum StatusAilmentType
{
    None,
    気絶,
    毒,
    火傷,
    凍結,
    暗闇,
    鈍化,
    混乱
}

[Serializable]
public class StatusAilment
{
    public StatusAilmentType Type;
    public int RemainingTurns;
    public string SourceCardName;

    public StatusAilment(StatusAilmentType type, int remainingTurns, string sourceCardName)
    {
        Type = type;
        RemainingTurns = remainingTurns;
        SourceCardName = sourceCardName;
    }

    /// <summary>
    /// ダメージを受けたら解ける状態。ターン経過では消えない。
    /// </summary>
    public static bool ClearsWhenDamaged(StatusAilmentType type)
    {
        return type == StatusAilmentType.気絶 || type == StatusAilmentType.混乱;
    }
}

[Serializable]
public class StatusAilmentSprite
{
    public StatusAilmentType type;
    public Sprite sprite;
}
