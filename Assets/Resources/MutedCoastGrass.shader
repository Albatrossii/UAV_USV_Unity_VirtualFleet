Shader "UavUsv/MutedCoastGrass"
{
    Properties
    {
        _Color ("Base", Color) = (0.30, 0.32, 0.24, 1)
        _DryColor ("Dry Patch", Color) = (0.38, 0.35, 0.26, 1)
        _DarkColor ("Shadow Patch", Color) = (0.20, 0.22, 0.17, 1)
        _NoiseScale ("Noise Scale", Float) = 0.038
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 200
        Cull Off

        CGPROGRAM
        #pragma surface surf Lambert fullforwardshadows
        #pragma target 3.0

        fixed4 _Color;
        fixed4 _DryColor;
        fixed4 _DarkColor;
        float _NoiseScale;

        struct Input
        {
            float3 worldPos;
        };

        float Hash21(float2 p)
        {
            p = frac(p * float2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return frac(p.x * p.y);
        }

        float Noise(float2 p)
        {
            float2 i = floor(p);
            float2 f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            return lerp(
                lerp(Hash21(i), Hash21(i + float2(1, 0)), f.x),
                lerp(Hash21(i + float2(0, 1)), Hash21(i + 1), f.x),
                f.y
            );
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            float2 p = IN.worldPos.xz * _NoiseScale;
            float n = Noise(p) * 0.55 + Noise(p * 2.3 + 7.1) * 0.30 + Noise(p * 5.1) * 0.15;
            fixed3 color = lerp(_DarkColor.rgb, _Color.rgb, saturate(n * 1.15));
            color = lerp(color, _DryColor.rgb, saturate((n - 0.62) * 2.4));
            o.Albedo = color;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
