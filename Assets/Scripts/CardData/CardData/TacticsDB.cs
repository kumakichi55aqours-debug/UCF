using UnityEngine;

[CreateAssetMenu(fileName = "T", menuName = "新規作戦カード")]
public class TacticsDB : CardDBBase
{
    private void OnEnable()
    {
        cardKind = CardKind.作戦;
    }
}
