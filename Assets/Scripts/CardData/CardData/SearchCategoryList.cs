using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class SearchCategoryList
{

}
public enum CardRarity { N, SR, UR }
public enum CardTypeList
{
    None,力,速,技,超,滅,無
}

/// <summary>
/// Inspectorのカテゴリ選択をグループに分ける。reading は五十音順の読み（ひらがな・カタカナ）。
/// カタカナだけの名前は省略できる。漢字や英字を含む名前は読みを書く。
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class CardCategoryAttribute : Attribute
{
    public string Group { get; }
    public string Reading { get; }

    public CardCategoryAttribute(string group, string reading = null)
    {
        Group = group;
        Reading = reading;
    }
}

// 数値はカードデータが参照している。既存の番号は変えず、追加は末尾の次の番号にする。
// グループ名を変えると Inspector の分類だけが変わる。
public enum CardCategoryList
{
    [CardCategory("種類", "かいじゅう")] 怪獣 = 0,
    [CardCategory("種類", "うちゅうじん")] 宇宙人 = 1,
    [CardCategory("種類")] ウルトラ = 2,

    [CardCategory("昭和シリーズ", "うるとらきゅー")] ウルトラQ = 31,
    [CardCategory("昭和シリーズ", "しょだいまん")] 初代マン = 3,
    [CardCategory("昭和シリーズ")] ゾフィー = 4,
    [CardCategory("昭和シリーズ")] セブン = 5,
    [CardCategory("昭和シリーズ")] ジャック = 6,
    [CardCategory("昭和シリーズ")] エース = 7,
    [CardCategory("昭和シリーズ")] タロウ = 8,
    [CardCategory("昭和シリーズ")] レオ = 9,
    [CardCategory("昭和シリーズ", "ちち")] 父 = 10,
    [CardCategory("昭和シリーズ", "はは")] 母 = 11,
    [CardCategory("昭和シリーズ")] キング = 12,
    [CardCategory("昭和シリーズ")] エイティ = 13,
    [CardCategory("昭和シリーズ", "ざゆーえすえー")] ザＵＳＡ = 14,
    [CardCategory("昭和シリーズ")] グレート = 15,
    [CardCategory("昭和シリーズ")] パワード = 16,
    [CardCategory("平成シリーズ")] ゼアス = 17,
    [CardCategory("平成シリーズ")] ティガ = 18,
    [CardCategory("平成シリーズ")] ダイナ = 19,
    [CardCategory("平成シリーズ")] ガイア = 20,
    [CardCategory("平成シリーズ")] コスモス = 21,
    [CardCategory("平成シリーズ")] ネオス = 22,
    [CardCategory("平成シリーズ")] ネクサス = 23,
    [CardCategory("平成シリーズ")] マックス = 24,
    [CardCategory("平成シリーズ")] メビウス = 25,
    [CardCategory("平成シリーズ", "だいかいじゅうばとる")] 大怪獣バトル = 26,
    [CardCategory("平成シリーズ")] ゼロ = 27,

    [CardCategory("種類", "ぼうえいたい")] 防衛隊 = 100,
    [CardCategory("その他", "へんしん")] 変身 = 101,
    [CardCategory("その他", "ばるたんせいじん")] バルタン星人 = 102,
    [CardCategory("その他", "たたらじま")] 多々良島 = 103,
    [CardCategory("その他", "こだいかいじゅう")] 古代怪獣 = 104,
    [CardCategory("その他", "かぷせるかいじゅう")] カプセル怪獣 = 105,
    [CardCategory("その他", "ゆうがた")] 夕方 = 106,
    [CardCategory("種類", "ちょうじゅう")] 超獣 = 107,
    [CardCategory("その他", "だいかいじゅう")] 大怪獣 = 108,
}
public enum ElementList
{
    None, 無, 炎, 水, 雷, 風, 地, 氷, 光, 闇
}

public enum ActiveTiming
{
    None,
    // ACT
    登場時,
    起動効果,

    // PAS
    ルール効果,
    永続効果,

    // TRG
    技使用時,
    技1使用時,
    技2使用時,
    技使用後,
    技1使用後,
    技2使用後,
    技撃破時,
    技1撃破時,
    技2撃破時,
    技を受ける時,
    手札に加わった時,
    墓地に送られた時,
    自分のフェイズ開始時,
    自分のフェイズ終了時,
    相手のフェイズ開始時,
    相手のフェイズ終了時,
    相手の技使用時,
    
    // 他のカードTRG
    他のカードが墓地に送られた時,
    他のバトラーが撃破した時,
    他効果誘発,
}

/// <summary>
/// 効果や技が取るものの種類。側は PlayerType の対象側で決める。
/// プレイヤーかつバトラー1体は自分側の1体。両方かつバトラー1体は両側から1体。
/// None は対象なし。
/// </summary>
public enum TargetList
{
    None,
    バトラー1体,
    自分,
    技対象,
    バトラー全員,
    フィールド1枚,
    手札,
    プレイヤー,
}

public static class TargetScope
{
    public static bool IncludesOwner(PlayerType side)
    {
        return side == PlayerType.プレイヤー || side == PlayerType.両方;
    }

    public static bool IncludesOpponent(PlayerType side)
    {
        return side == PlayerType.相手 || side == PlayerType.両方;
    }
}

public enum TargetCountMode
{
    ちょうど,
    まで,
}

public enum ActiveLocation
{
    None,
    Bゾーン,
    Tゾーン,
    Fゾーン,
    手札,
    墓地,
    デッキ,
}
public enum CardZoneType
{
    None,
    プレイヤー手札,
    プレイヤーBゾーン,
    プレイヤーTゾーン,
    プレイヤーFゾーン,
    プレイヤー墓地,
    プレイヤーデッキ,
    相手手札,
    相手Bゾーン,
    相手Tゾーン,
    相手Fゾーン,
    相手墓地,
    相手デッキ
}
public enum PlayerType
{
    プレイヤー,
    相手,
    両方
}
public enum CardKind
{
    None,
    バトラー,
    作戦,
    フィールド   
}

public enum ResourceType
{
    エネルギー,
    コスト,
    HP
}

public enum SummonSource
{
    手札,
    墓地,
    デッキ,
    場
}
public enum SkillTrigger
{
    カード使用時,
    登場時,
    技使用時,
    技1使用時,
    技2使用時,
    技使用後,
    技1使用後,
    技2使用後,
    技撃破時,
    技1撃破時,
    技2撃破時,
    相手の技使用時,
    墓地に送られた時,
    技を受ける時,
    // プレイヤーが選んで発動する。トリガー収集では拾わず、宣言した1件だけをチェーンに載せる
    起動効果,
    自分のフェイズ開始時,
    自分のフェイズ終了時,
    // 他カードTRG
    他カードが墓地に送られた時,
    他バトラー撃破時,
    他効果誘発,
}
public enum MoveTarget
{
    技1,
    技2,
    両方,
}

public enum DeckEditCustomZone
{
    カードリスト, // カードリストのゾーン
    デッキリスト      // デッキのゾーン
}

public enum SourceCardType
{
    None,                    // 条件なし
    Self,                    // スキルを発動したカード自身（全く同じカードインスタンス）
    ExcludeSelf,             // SourceCardと同名のカード以外すべて
    処理したカード           // 直前の効果で処理したカード（手札に加えたカードなど）
}

// 数値は保存済みの条件が参照している。並びは変えない。
public enum MatchType
{
    以上,
    以下,
    同じ
}

public static class MatchTypeExtensions
{
    public static bool Matches(this MatchType type, int value, int threshold)
    {
        switch (type)
        {
            case MatchType.以上:
                return value >= threshold;
            case MatchType.以下:
                return value <= threshold;
            default:
                return value == threshold;
        }
    }
}

[Serializable]
public class CardTypeSprite
{
    public CardTypeList type;
    public Sprite sprite;
}

[Serializable]
public class StrongElementSprite
{
    public ElementList element;
    public Sprite sprite;
}

[Serializable]
public class WeakElementSprite
{
    public ElementList element;
    public Sprite sprite;
}

[Serializable]
public class MoveElementSprite
{
    public ElementList element;
    public Sprite sprite;
}