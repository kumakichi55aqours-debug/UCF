using UnityEngine;

[CreateAssetMenu(fileName = "T", menuName = "新規作戦カード")]
public class TacticsDB : CardDBBase
{
    public CardTypeList type;

    private void OnEnable()
    {
        cardKind = CardKind.作戦;
    }
}
