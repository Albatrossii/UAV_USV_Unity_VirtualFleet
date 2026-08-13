Shader "UavUsv/MaritimeSky"
{
    Properties
    {
        _CloudSpeed ("Cloud Speed", Range(0, 0.1)) = 0.012
        _CloudAmount ("Cloud Amount", Range(0, 1)) = 0.48
        _Exposure ("Exposure", Range(0.2, 2)) = 1.15
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            float _CloudSpeed;
            float _CloudAmount;
            float _Exposure;

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 position : SV_POSITION; float3 direction : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.position = UnityObjectToClipPos(v.vertex);
                o.direction = mul((float3x3)unity_ObjectToWorld, v.vertex.xyz);
                return o;
            }

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x),
                            lerp(Hash(i + float2(0, 1)), Hash(i + 1), f.x), f.y);
            }

            float Fbm(float2 p)
            {
                float value = 0, weight = 0.55;
                value += Noise(p) * weight; p = p * 2.03 + 17.1; weight *= 0.5;
                value += Noise(p) * weight; p = p * 2.01 + 9.7;  weight *= 0.5;
                value += Noise(p) * weight; p = p * 2.07 + 5.3;  weight *= 0.5;
                value += Noise(p) * weight;
                return value;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.direction);
                float height = saturate(d.y);
                float3 horizon = float3(0.68, 0.82, 0.96);
                float3 zenith = float3(0.10, 0.34, 0.74);
                float3 sky = lerp(horizon, zenith, pow(height, 0.42));

                float3 sunDirection = normalize(float3(-0.72, 0.22, 0.46));
                float sunDot = saturate(dot(d, sunDirection));
                float sun = pow(sunDot, 720.0);
                float glow = pow(sunDot, 12.0) * (1.0 - height * 0.45);
                sky += float3(1.0, 0.66, 0.3) * glow * 0.42;
                sky += float3(1.0, 0.9, 0.68) * sun * 4.0;

                float cloudMask = smoothstep(0.02, 0.18, height) * (1.0 - smoothstep(0.72, 0.95, height));
                float2 cloudUv = d.xz / max(0.12, d.y + 0.22) * 1.15;
                cloudUv += float2(_Time.y * _CloudSpeed, _Time.y * _CloudSpeed * 0.34);
                float cloudNoise = Fbm(cloudUv) * 0.72 + Fbm(cloudUv * 2.8 + 12.4) * 0.28;
                float clouds = smoothstep(0.72 - _CloudAmount * 0.42, 0.82 - _CloudAmount * 0.28, cloudNoise) * cloudMask;
                float edge = saturate(Fbm(cloudUv + sunDirection.xz * 0.08) - cloudNoise + 0.12) * clouds;
                float3 cloudShade = lerp(float3(0.34, 0.42, 0.55), float3(0.98, 0.98, 1.0), height + 0.2);
                cloudShade += float3(1.0, 0.61, 0.28) * edge * glow * 2.2;
                sky = lerp(sky, cloudShade, clouds * 0.88);

                if (d.y < 0) sky = lerp(float3(0.14, 0.25, 0.34), horizon, saturate(d.y + 1.0));
                return fixed4(sky * _Exposure, 1);
            }
            ENDCG
        }
    }
}
