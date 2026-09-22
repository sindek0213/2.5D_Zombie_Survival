Shader "Zombie Survival/Generated Sprite White Key"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [MaterialToggle] PixelSnap ("Pixel snap", Float) = 0
        [HideInInspector] _RendererColor ("Renderer Color", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
        [PerRendererData] _AlphaTex ("External Alpha", 2D) = "white" {}
        [PerRendererData] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
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

        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex SpriteVert
            #pragma fragment WhiteKeySpriteFrag
            #pragma target 2.0
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ PIXELSNAP_ON
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            #include "UnitySprites.cginc"

            float _WhiteDistance;
            float _EdgeSoftness;
            float _CheckerBrightness;
            float _CheckerChroma;
            float _CheckerSoftness;

            fixed4 WhiteKeySpriteFrag(v2f input) : SV_Target
            {
                // 생성된 원본은 투명 PNG가 아니라 흰 배경이 포함된 RGB 이미지입니다.
                // 순수 흰색과 가까운 픽셀일수록 알파를 0으로 만들어 캐릭터 주변의 사각 배경만 화면에서 숨깁니다.
                fixed4 color = SampleSpriteTexture(input.texcoord) * input.color;
                float distanceFromWhite = distance(color.rgb, float3(1.0, 1.0, 1.0));
                float whiteKeyAlpha = smoothstep(
                    _WhiteDistance,
                    _WhiteDistance + _EdgeSoftness,
                    distanceFromWhite);
                color.a *= whiteKeyAlpha;

                // 회색과 흰색 체크무늬 사이가 보간되어도 남지 않도록 밝은 무채색 범위를 함께 투명 처리합니다.
                // 색 차이가 있는 캐릭터·무기 픽셀은 chroma 값이 크므로 이 계산의 영향을 받지 않습니다.
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

                // Sprite 기본 셰이더와 같은 Premultiplied Alpha 방식으로 RGB에도 최종 알파를 곱합니다.
                // 이 처리가 없으면 투명해진 가장자리에 흰 테두리가 남을 수 있습니다.
                color.rgb *= color.a;
                return color;
            }
            ENDCG
        }
    }
}
