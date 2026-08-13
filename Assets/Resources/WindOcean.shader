Shader "UavUsv/WindOcean"
{
    Properties
    {
        _DeepColor ("Deep Water", Color) = (0.05, 0.12, 0.16, 1)
        _ShallowColor ("Shallow Water", Color) = (0.13, 0.21, 0.23, 1)
        _FoamColor ("Foam", Color) = (0.40, 0.44, 0.43, 1)
        _WindDirection ("Wind Direction XZ", Vector) = (0.88, 0, 0.48, 0)
        _WindSpeed ("Wind Speed", Range(0, 20)) = 6.5
        _WaveAmplitude ("Wave Amplitude", Range(0, 1.5)) = 0.42
        _Smoothness ("Smoothness", Range(0, 1)) = 0.08
        [HideInInspector] _PlanarReflectionTex ("Planar Reflection", 2D) = "black" {}
        [HideInInspector] _ReflectionAvailable ("Reflection Available", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 250
        Cull Off

        CGPROGRAM
        // Lambert keeps the mottled trough look without sun sparkle tiles.
        #pragma surface surf Lambert fullforwardshadows vertex:vert addshadow
        #pragma target 3.0
        #include "UnityCG.cginc"

        fixed4 _DeepColor;
        fixed4 _ShallowColor;
        float4 _WindDirection;
        float _WindSpeed;
        float _WaveAmplitude;

        struct Input
        {
            float3 viewDir;
            float waveHeight;
        };

        float2 Rotate2(float2 v, float angle)
        {
            float s = sin(angle), c = cos(angle);
            return float2(c * v.x - s * v.y, s * v.x + c * v.y);
        }

        float Wave(float2 p, float2 direction, float length, float speed, float phase)
        {
            float k = 6.2831853 / max(length, 1.0);
            return sin(dot(p, normalize(direction)) * k + _Time.y * speed + phase);
        }

        float Height(float2 p)
        {
            float2 wind = normalize(_WindDirection.xz + float2(0.0001, 0));
            float speedScale = lerp(0.45, 1.05, saturate(_WindSpeed / 15.0));
            float h = 0;
            h += Wave(p, wind,                  95.0, 0.28 * speedScale, 0.0) * 0.42;
            h += Wave(p, Rotate2(wind, 0.62),   58.0, 0.40 * speedScale, 1.3) * 0.30;
            h += Wave(p, Rotate2(wind, -0.95),  34.0, 0.55 * speedScale, 2.5) * 0.18;
            h += Wave(p, Rotate2(wind, 1.35),   21.0, 0.72 * speedScale, 0.8) * 0.10;
            return h * _WaveAmplitude;
        }

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            float3 world = mul(unity_ObjectToWorld, v.vertex).xyz;
            float e = 3.5;
            float h = Height(world.xz);
            float hx = Height(world.xz + float2(e, 0));
            float hz = Height(world.xz + float2(0, e));
            world.y += h;
            v.vertex = mul(unity_WorldToObject, float4(world, 1));
            float3 worldNormal = normalize(float3(
                -(hx - h) / e * 0.55,
                1.0,
                -(hz - h) / e * 0.55
            ));
            v.normal = normalize(mul((float3x3)unity_WorldToObject, worldNormal));
            o.waveHeight = h;
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            float facing = saturate(dot(normalize(IN.viewDir), float3(0, 1, 0)));
            float fresnel = pow(1.0 - facing, 3.5);
            // Crest/trough tint — the mottled dark patches from the reference shot.
            float lift = saturate(IN.waveHeight * 1.8 + 0.5);
            fixed3 water = lerp(_DeepColor.rgb, _ShallowColor.rgb, lift * 0.35 + fresnel * 0.12);
            o.Albedo = water;
            o.Emission = water * 0.02;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
