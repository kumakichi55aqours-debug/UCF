Shader "UI/SoftPortrait"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _Margin ("Outer Margin", Range(0, 0.45)) = 0
        _Blur ("Border Blur Pixels", Range(0, 64)) = 8
        _BlurStart ("Blur Start", Range(0, 1.5)) = 0.75
        _EdgeStart ("Border Start", Range(0.4, 1.8)) = 0.93
        _EdgeEnd ("Border End", Range(0.5, 2.5)) = 1

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15

        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

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
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float4 _MainTex_ST;
            float _Margin;
            float _Blur;
            float _BlurStart;
            float _EdgeStart;
            float _EdgeEnd;

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

            // 0-1 の外側を内側へ折り返す。端の1ピクセルを引き伸ばさない。
            float2 MirrorInside(float2 uv)
            {
                uv = abs(uv);
                uv = 1.0 - abs(uv - 1.0);
                return saturate(uv);
            }

            // 画面上のピクセル半径でぼかす。透明ピクセルの黒が色に混ざらないよう、不透明度で重みを付ける。
            float4 SampleBlur(float2 contentUv, float radiusPx, float2 pixelUv)
            {
                if (radiusPx < 0.75)
                    return tex2Dlod(_MainTex, float4(saturate(contentUv), 0, 0));

                const int COUNT = 20;
                float3 rgb = 0;
                float alpha = 0;
                float weightSum = 0;

                [unroll]
                for (int i = 0; i < COUNT; i++)
                {
                    float area = (i + 0.5) / COUNT;
                    float ring = sqrt(area);
                    float angle = i * 2.39996323;
                    float2 dir = ring * float2(cos(angle), sin(angle));
                    float weight = exp(-2.6 * ring * ring);
                    float2 tapUv = MirrorInside(contentUv + dir * pixelUv * radiusPx);
                    float4 tex = tex2Dlod(_MainTex, float4(tapUv, 0, 0));
                    rgb += tex.rgb * tex.a * weight;
                    alpha += tex.a * weight;
                    weightSum += weight;
                }

                float4 col = 0;
                col.rgb = rgb / max(alpha, 0.0001);
                col.a = alpha / max(weightSum, 0.0001);
                return col;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                // 余白があるとき、絵そのものは中央に元の大きさで置き、外側だけをにじみにする。
                float contentScale = max(0.05, 1.0 - _Margin * 2.0);
                float2 fromCenter = (IN.texcoord - 0.5) / contentScale;
                float2 contentUv = fromCenter + 0.5;
                // 中心が0、絵の辺が1。円にはせず、辺までの距離で外側だけを抜く。
                float squareEdge = max(abs(fromCenter.x), abs(fromCenter.y)) * 2.0;
                float2 pixelUv = max(abs(ddx(contentUv)) + abs(ddy(contentUv)), float2(1e-5, 1e-5));

                float blurT = smoothstep(_BlurStart, max(_BlurStart + 0.02, _EdgeEnd), squareEdge);
                fixed4 col = SampleBlur(contentUv, _Blur * blurT, pixelUv);
                col += _TextureSampleAdd;

                float fade = saturate((squareEdge - _EdgeStart) / max(0.001, _EdgeEnd - _EdgeStart));
                fade = fade * fade * fade * (fade * (fade * 6.0 - 15.0) + 10.0);
                col *= IN.color;
                col.a *= 1.0 - fade;

                #ifdef UNITY_UI_CLIP_RECT
                col.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(col.a - 0.001);
                #endif

                return col;
            }
            ENDCG
        }
    }
}
