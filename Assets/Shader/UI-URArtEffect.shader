// URカードのArtImage用。
// 下地はカード絵そのもの。虹画像を BlendMode の合成で載せ、白い斜め光を左上から右下へ通す。
// UI-CompositShader の GrabPass は Screen Space - Overlay では背面を取れないため、こちらを使う。
Shader "StudioMaron/UI-URArtEffect"
{
    Properties
    {
        [PerRendererData] _MainTex("Sprite Texture", 2D) = "white" {}
        _Color("Tint", Color) = (1,1,1,1)

        _HoloTex("虹色画像", 2D) = "white" {}
        _HoloStrength("虹の強さ", Range(0, 1)) = 0.5
        _HoloRotateSpeed("虹の回転(回/秒)", Range(-1, 1)) = 0.08
        _HoloTiling("倍率(1で等倍)", Range(0.25, 4)) = 1
        [HideInInspector] _ArtSize("Art Size", Vector) = (0, 0, 0, 0)
        [Enum(StudioMaron.BlendMode)] _Composition("虹の合成", Float) = 5

        _ShineAlpha("斜め光の透明度", Range(0, 1)) = 0.25
        _ShineWidth("斜め光の太さ", Range(0.02, 0.8)) = 0.22
        _ShineSoftness("斜め光のにじみ", Range(0, 0.4)) = 0.08
        _ShineTravel("横切る時間(秒)", Range(0.2, 4)) = 1.1
        _ShineInterval("繰り返す間隔(秒)", Range(0.4, 10)) = 3.2

        _StencilComp("Stencil Comparison", Float) = 8
        _Stencil("Stencil ID", Float) = 0
        _StencilOp("Stencil Operation", Float) = 0
        _StencilWriteMask("Stencil Write Mask", Float) = 255
        _StencilReadMask("Stencil Read Mask", Float) = 255
        _ColorMask("Color Mask", Float) = 15

        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref[_Stencil]
            Comp[_StencilComp]
            Pass[_StencilOp]
            ReadMask[_StencilReadMask]
            WriteMask[_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest[unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask[_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #include "BlendComposite.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            sampler2D _HoloTex;
            float4 _HoloTex_ST;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float4 _MainTex_ST;
            float _Composition;
            float _HoloStrength;
            float _HoloRotateSpeed;
            float _HoloTiling;
            float4 _ArtSize;
            float4 _HoloTex_TexelSize;
            float _ShineAlpha;
            float _ShineWidth;
            float _ShineSoftness;
            float _ShineTravel;
            float _ShineInterval;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;
                return OUT;
            }

            float2 HoloUv(float2 texcoord)
            {
                float2 texSize = max(_HoloTex_TexelSize.zw, float2(1, 1));
                float2 artSize = _ArtSize.xy;
                if (artSize.x < 1 || artSize.y < 1)
                    artSize = texSize;

                // 0-1のUVを枠に引き伸ばさず、画像のピクセルサイズのまま中心に置く
                float2 fromCenter = (texcoord - 0.5) * artSize;
                float angle = _Time.y * _HoloRotateSpeed * 6.2831853;
                float s = sin(angle);
                float c = cos(angle);
                fromCenter = float2(c * fromCenter.x - s * fromCenter.y, s * fromCenter.x + c * fromCenter.y);
                fromCenter *= _HoloTiling;
                return fromCenter / texSize + 0.5;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                half4 art = (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd) * IN.color;

                half4 holo = tex2D(_HoloTex, HoloUv(IN.texcoord));

                half3 composed = ApplyComposition(art.rgb, holo.rgb, _Composition);
                float holoMix = saturate(holo.a * _HoloStrength);
                half3 rgb = lerp(art.rgb, composed, holoMix);

                // 斜め棒は右上-左下方向。位置だけを左上から右下へ動かす。
                float diag = IN.texcoord.x - IN.texcoord.y;
                float cycle = max(_ShineInterval, _ShineTravel + 0.05);
                float localT = frac(_Time.y / cycle) * cycle;
                float moving = step(localT, _ShineTravel);
                float travel = saturate(localT / max(_ShineTravel, 0.001));
                travel = travel * travel * (3.0 - 2.0 * travel);
                float pos = lerp(-1.35, 1.35, travel);

                float dist = abs(diag - pos);
                float edge = _ShineWidth + _ShineSoftness;
                float band = 1.0 - smoothstep(_ShineWidth, max(edge, _ShineWidth + 0.001), dist);
                float coreEnd = max(_ShineWidth * 0.45, 0.01);
                float core = 1.0 - smoothstep(_ShineWidth * 0.12, coreEnd, dist);
                float shine = saturate(band * 0.65 + core) * moving;
                rgb = lerp(rgb, half3(1, 1, 1), shine * saturate(_ShineAlpha));

                art.rgb = rgb;

                #ifdef UNITY_UI_CLIP_RECT
                art.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(art.a - 0.001);
                #endif

                return art;
            }
            ENDCG
        }
    }
}
