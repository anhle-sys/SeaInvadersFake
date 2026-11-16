// Vệt cháy xém trên vành của lỗ 2 lớp (lõi bé đã thủng, vành còn).
// uv0 = toạ độ trong mask của lớp bé (tâm 0, 1 = Mask Size), uv1.x = hệ số lớp to.
// Lấy mẫu mask ở nhiều kích thước từ bé tới to: điểm nằm trong càng nhiều bản phóng to
// thì càng gần lõi -> cháy càng đậm. Vệt cháy vì thế ôm đúng hình mask, sát mép lõi
// có viền than hồng để thấy rõ lõi đã thủng.
Shader "MeshEditor/HoleScorch"
{
    Properties
    {
        _Color ("Char Color", Color) = (0.035, 0.025, 0.02, 0.95)
        _EmberColor ("Ember Color", Color) = (1.0, 0.42, 0.08, 1)
        _EmberWidth ("Ember Width", Range(0, 0.5)) = 0.14
        _EmberIntensity ("Ember Intensity", Range(0, 4)) = 1.6
        _Falloff ("Falloff", Range(0.2, 4)) = 0.8
        [NoScaleOffset] _MaskTex ("Mask (set by RuntimeMeshFillController)", 2D) = "white" {}
        _Threshold ("Alpha Threshold", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags { "Queue" = "Transparent-10" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            // Giữ nguyên alpha của framebuffer (ảnh chụp / RT có alpha không bị nhạt đi).
            Blend SrcAlpha OneMinusSrcAlpha, Zero One
            ZWrite Off
            Cull Off
            Offset -1, -1

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            #define SAMPLES 8

            sampler2D _MaskTex;
            fixed4 _Color;
            fixed4 _EmberColor;
            half _EmberWidth;
            half _EmberIntensity;
            half _Falloff;
            half _Threshold;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float2 scale : TEXCOORD1;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float scale : TEXCOORD1;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.scale = max(v.scale.x, 1.001);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float inside = 0;

                [unroll]
                for (int s = 0; s < SAMPLES; s++)
                {
                    float m = lerp(1.0, i.scale, (s + 0.5) / SAMPLES);
                    float2 uv = i.uv / m + 0.5;
                    float a = tex2Dlod(_MaskTex, float4(saturate(uv), 0, 0)).a;
                    a *= all(uv == saturate(uv)) ? 1.0 : 0.0;
                    inside += smoothstep(_Threshold - 0.08, _Threshold + 0.08, a);
                }

                // 1 sát mép lõi bé, ~0 ở mép ngoài vành.
                float t = inside / SAMPLES;
                float ember = smoothstep(1.0 - _EmberWidth, 1.0, t);

                fixed4 c;
                c.rgb = lerp(_Color.rgb, _EmberColor.rgb * _EmberIntensity, ember);
                c.a = saturate(_Color.a * pow(t, _Falloff) + ember * _EmberColor.a);
                return c;
            }
            ENDCG
        }
    }
}
