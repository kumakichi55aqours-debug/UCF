using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "B", menuName = "新規バトラーカード")]
public class BattlerDB : CardDBBase
{
    public CardTypeList タイプ;
    public int hp;
    public ElementList 得意属性;
    public int 属性軽減値;
    public ElementList 弱点属性;
    public int 属性増加値;

    public string 技名1;
    public ElementList 属性1;
    public int EN1;
    public int PW1;
    public TargetList 対象1;
    public PlayerType 対象側1 = PlayerType.プレイヤー;
    public SelectTarget 対象選択1 = new SelectTarget();
    [TextArea]
    public string 効果1;

    public string 技名2;
    public ElementList 属性2;
    public int EN2;
    public int PW2;
    public TargetList 対象2;
    public PlayerType 対象側2 = PlayerType.プレイヤー;
    public SelectTarget 対象選択2 = new SelectTarget();
    [TextArea]
    public string 効果2;

    public AudioClip ボイス;
}
