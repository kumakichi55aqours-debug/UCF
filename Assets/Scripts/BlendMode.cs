// 合成モードの定義をしている
// シェーダー側で[Enum(StudioMaron.BlendMode)]_Composition("Composition", float) = 0
//と書くことで、ここに書かれた定義と順番をInspectorでEnumとしてint値で受け取れる
namespace StudioMaron
{
    public enum BlendMode
    {
        通常, //0番
        乗算, //1番
        加算, //2番(以下略)
        除算,
        スクリーン,
        オーバーレイ,
        ハードライト,
        ソフトライト,
        覆い焼きカラー,
        焼き込みリニア,
        差の絶対値,
        比較暗,
        比較明,
        色相,
        彩度,
        カラー,
        輝度
    }
}