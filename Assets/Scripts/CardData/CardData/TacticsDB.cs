using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "T", menuName = "新規作戦カード")]
public class TacticsDB : CardDBBase
{
    public CardTypeList タイプ;

    private void OnEnable()
    {
        分類 = CardKind.作戦;
    }
}
