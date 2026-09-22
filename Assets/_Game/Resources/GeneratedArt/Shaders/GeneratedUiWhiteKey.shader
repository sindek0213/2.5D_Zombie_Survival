Shader "Zombie Survival/Generated UI White Key"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
        _WhiteDistance ("White Background Distance", Range(0, 0.5)) = 0.06
        _EdgeSoftness ("Transparent Edge Softness", Range(0.001, 0.5)) = 0.08
        _CheckerBrightness ("Checker Background Brightness", Range(0, 1)) = 0.62
        _CheckerChroma ("Checker Background Chroma", Range(0, 0.5)) = 0.035
        _CheckerSoftness ("Checker Edge Softness", Range(0.001, 0.5)) = 0.06
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
            #pragma target 2.0
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float _WhiteDistance;
            float _EdgeSoftness;
            float _CheckerBrightness;
            float _CheckerChroma;
            float _CheckerSoftness;

            v2f vert(appdata_t input)
            {
                v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.worldPosition = input.vertex;
                output.vertex = UnityObjectToClipPos(output.worldPosition);
                output.texcoord = input.texcoord;
                output.color = input.color * _Color;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                fixed4 color = (tex2D(_MainTex, input.texcoord) + _TextureSampleAdd) * input.color;

                // UI에서도 필드 Sprite와 같은 기준으로 원본의 흰 배경을 투명하게 만듭니다.
                // 원본 PNG를 복사해 둔 채 표시 단계에서만 처리하므로 나중에 배경 제거된 정식 원화로 쉽게 교체할 수 있습니다.
                float distanceFromWhite = distance(color.rgb, float3(1.0, 1.0, 1.0));
                color.a *= smoothstep(
                    _WhiteDistance,
                    _WhiteDistance + _EdgeSoftness,
                    distanceFromWhite);

                // 일부 생성 원화의 투명 격자는 실제 알파가 아니라 밝은 무채색 RGB 픽셀로 저장되어 있습니다.
                // 밝기는 높지만 R·G·B 차이가 거의 없는 픽셀만 추가로 지워서 파랑 머리와 분홍 장비색은 보존합니다.
                float maximumChannel = max(color.r, max(color.g, color.b));
                float minimumChannel = min(color.r, min(color.g, color.b));
                float chroma = maximumChannel - minimumChannel;
                float brightness = dot(color.rgb, float3(0.2126, 0.7152, 0.0722));
                float neutralAmount = 1.0 - smoothstep(
                    _CheckerChroma,
                    _CheckerChroma + _CheckerSoftness,
                    chroma);
                float brightAmount = smoothstep(
                    _CheckerBrightness,
                    _CheckerBrightness + _CheckerSoftness,
                    brightness);
                color.a *= 1.0 - neutralAmount * brightAmount;

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(input.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
            ENDCG
        }
    }
}
