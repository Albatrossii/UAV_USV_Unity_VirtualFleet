Shader "UavUsv/ShoreFade"
{
    Properties
    {
        _Color ("Color", Color) = (0.12, 0.36, 0.32, 0.35)
        _FadePower ("Fade Power", Range(0.4, 4)) = 1.6
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }
        LOD 100
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _FadePower;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
            };

            v2f vert(appdata input)
            {
                v2f output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                UNITY_TRANSFER_FOG(output, output.position);
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                // uv.y: 1 = land side, 0 = open water side.
                float fade = pow(saturate(input.uv.y), _FadePower);
                float endFade = saturate(min(input.uv.x, 1.0 - input.uv.x) * 7.0);
                fixed4 color = _Color;
                color.a *= fade * endFade;
                UNITY_APPLY_FOG(input.fogCoord, color);
                return color;
            }
            ENDCG
        }
    }
}
