Shader "Custom/GlowShader"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1) // オブジェクトの色
        _GlowColor ("Glow Color", Color) = (1, 1, 0, 1) // 光の色
        _GlowIntensity ("Glow Intensity", Range(0, 10)) = 1.0 // 光の強さ
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // 頂点データ
            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : POSITION;
                float4 color : COLOR;
            };

            // パラメータ
            float _GlowIntensity;   // 光の強さ
            float4 _GlowColor;      // 光の色
            float4 _Color;          // オブジェクトの基本色

            // 頂点シェーダー
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex); // 頂点位置をスクリーンスペースに変換
                o.color = _Color * (1 + _GlowIntensity); // 色に光の強さを掛ける
                return o;
            }

            // フラグメントシェーダー（ピクセルシェーダー）
            half4 frag(v2f i) : SV_Target
            {
                return i.color + _GlowColor; // 基本色と光の色を加える
            }
            ENDCG
        }
    }
}
