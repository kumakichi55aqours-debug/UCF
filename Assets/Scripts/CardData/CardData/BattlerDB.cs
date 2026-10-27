using UnityEngine;

[CreateAssetMenu(fileName = "B", menuName = "新規バトラーカード")]
public class BattlerDB : CardDBBase
{
    public CardTypeList type;
    public int hp;
    public ElementList 得意属性;
    public int 属性軽減値;
    public ElementList 弱点属性;
    public int 属性増加値;

    [Header("技1")]
    public string moveName1;
    public int energy1;
    public int power1;
    public ElementList moveElement1;
    public TargetList target1;
    public PlayerType 対象側1 = PlayerType.プレイヤー;
    [Tooltip("対象がバトラーを選ぶ技のときだけ使う。1なら1体。2以上で複数選択。ちょうどは人数ぴったりいないと使えない。までは1体以上、その人数まで。")]
    public SelectTarget 対象選択1 = new SelectTarget();
    [TextArea]
    public string description1;

    [Header("技2")]
    public string moveName2;
    public int energy2;
    public int power2;
    public ElementList moveElement2;
    public TargetList target2;
    public PlayerType 対象側2 = PlayerType.プレイヤー;
    [Tooltip("対象がバトラーを選ぶ技のときだけ使う。1なら1体。2以上で複数選択。ちょうどは人数ぴったりいないと使えない。までは1体以上、その人数まで。")]
    public SelectTarget 対象選択2 = new SelectTarget();
    [TextArea]
    public string description2;

    [Header("召喚ボイス")]
    public AudioClip summonVoice;
}
