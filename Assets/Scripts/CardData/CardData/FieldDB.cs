using UnityEngine;

[CreateAssetMenu(fileName = "F", menuName = "新規フィールドカード")]
public class FieldDB : CardDBBase
{
    public int hp;

    private void OnEnable()
    {
        cardKind = CardKind.フィールド;
    }
}
