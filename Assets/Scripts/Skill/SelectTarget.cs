using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TargetList から対象を選ぶときの数と選び方。
/// 対象を選ぶスキルには、この項目がインスペクターへ出る。
/// </summary>
[Serializable]
public class SelectTarget
{
    public int 選択数 = 1;
    public TargetCountMode 選択方式 = TargetCountMode.ちょうど;

    public int Count => Mathf.Max(1, 選択数);

    public int MinimumCount => 選択方式 == TargetCountMode.まで ? 1 : Count;

    public static SelectTarget Copy(SelectTarget source)
    {
        if (source == null)
            return new SelectTarget();

        return new SelectTarget
        {
            選択数 = source.選択数,
            選択方式 = source.選択方式
        };
    }

    public bool HasEnough(ICollection<CardController> targets)
    {
        if (Count <= 1)
            return true;

        return targets != null && targets.Count >= MinimumCount;
    }
}
