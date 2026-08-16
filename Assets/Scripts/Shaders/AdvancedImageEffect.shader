Shader "Custom/AdvancedImageEffect" {
    Properties{
        _MainTex("MainTex", 2D) = "white" {}
        _NoiseTex("NoiseTex", 2D) = "gray" {}
        // _CurveTex 已移除

        _Color("Color", Color) = (1,1,1,1)
        _Alpha("Alpha", Range(0,1)) = 1
        _Saturation("Saturation", Range(0,2)) = 1
        _Vignette("Vignette", Range(0,1)) = 0.5
        _WearThreshold("WearThreshold", Range(0,1)) = 0.5

        _NoiseScale("NoiseScale", Float) = 1.0
        _NoiseScroll("_NoiseScroll", Vector) = (0,0,0,0)
    }

        SubShader{
            Tags {
                "Queue" = "Transparent"
                "RenderType" = "Transparent"
                "PreviewType" = "Plane"
            }
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            Lighting Off
            ZWrite Off

            Pass {
                CGPROGRAM
                #pragma vertex vert
                #pragma fragment frag
                #include "UnityCG.cginc"

                struct appdata {
                    float4 vertex : POSITION;
                    float2 uv : TEXCOORD0;
                };

                struct v2f {
                    float4 vertex : SV_POSITION;
                    float2 mainUV : TEXCOORD0;
                    float2 noiseUV : TEXCOORD1;
                    float2 screenUV : TEXCOORD2;
                };

                // 纹理属性
                sampler2D _MainTex;
                sampler2D _NoiseTex;
                // sampler2D _CurveTex;  已删除
                float4 _MainTex_ST;
                float4 _NoiseTex_ST;

                // 控制参数
                float4 _Color;
                float _Alpha;
                float _Saturation;
                float _Vignette;
                float _WearThreshold;
                float _NoiseScale;
                float2 _NoiseScroll;

                v2f vert(appdata v) {
                    v2f o;
                    o.vertex = UnityObjectToClipPos(v.vertex);
                    o.mainUV = TRANSFORM_TEX(v.uv, _MainTex);
                    o.noiseUV = TRANSFORM_TEX(v.uv, _NoiseTex) * _NoiseScale;
                    o.noiseUV += _Time.yy * _NoiseScroll;

                    float4 screenPos = ComputeScreenPos(o.vertex);
                    o.screenUV = (screenPos.xy / screenPos.w) * 2.0 - 1.0;
                    return o;
                }

                fixed4 frag(v2f i) : SV_Target {
                    fixed4 col = tex2D(_MainTex, i.mainUV);

                // 颜色调整流程（饱和度 + 颜色叠加）
                float luminance = Luminance(col.rgb);
                col.rgb = lerp(luminance, col.rgb, _Saturation);
                col.rgb *= _Color.rgb;

                // 曲线部分已完全移除，亮度不再被映射

                // 光晕效果（暗角）
                float vignette = 1.0 - saturate(dot(i.screenUV, i.screenUV) * _Vignette);
                col.rgb *= vignette * vignette;

                // 噪声镂空系统
                float noise = tex2D(_NoiseTex, i.noiseUV).r;
                float wearMask = smoothstep(_WearThreshold - 0.1, _WearThreshold + 0.1, noise);
                col.a *= wearMask;

                // 最终透明度合成
                col.a *= _Alpha * saturate(col.a);
                return saturate(col);
            }
            ENDCG
        }
        }
            FallBack "UI/Default"
}