Shader "Custom/SpriteShadow"
{
    Properties
    {
        [PerRendererData] _MainTex("Sprite Texture", 2D) = "white" {}
        _Color("Tint", Color) = (1,1,1,1)

            // 阴影参数
            _ShadowColor("Shadow Color", Color) = (0,0,0,0.8)
            _ShadowStrength("Shadow Strength", Range(0,5)) = 1.5
            _ShadowRadius("Shadow Radius", Range(1,150)) = 40

            // 边缘光参数
            _RimColor("Rim Color", Color) = (1,1,1,0.5)
            _RimStrength("Rim Strength", Range(0,5)) = 1.0
            _RimRadius("Rim Radius", Range(1,150)) = 20

            // ===== 新增：垂直渐变叠加 =====
            _GradientColor("Gradient Color", Color) = (0.2, 0.149, 0.1294, 1)  // (51,38,33)
            _GradientBottomAlpha("Gradient Bottom Alpha", Range(0,1)) = 1.0
            _GradientHeight("Gradient Height (0~1)", Range(0,1)) = 0.75
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

            Cull Off
            Lighting Off
            ZWrite Off
            Blend One OneMinusSrcAlpha

            Pass
            {
                CGPROGRAM
                #pragma vertex vert
                #pragma fragment frag
                #pragma target 3.0
                #include "UnityCG.cginc"

                struct appdata_custom
                {
                    float4 vertex   : POSITION;
                    float4 color    : COLOR;
                    float2 texcoord : TEXCOORD0;
                };

                struct v2f
                {
                    float4 vertex   : SV_POSITION;
                    fixed4 color : COLOR;
                    float2 texcoord : TEXCOORD0;
                };

                sampler2D _MainTex;
                float4 _MainTex_TexelSize;
                fixed4 _Color;
                fixed4 _ShadowColor;
                float _ShadowStrength;
                float _ShadowRadius;
                fixed4 _RimColor;
                float _RimStrength;
                float _RimRadius;

                // 新增渐变变量（必须与 Properties 中名称一致）
                fixed4 _GradientColor;
                float _GradientBottomAlpha;
                float _GradientHeight;

                v2f vert(appdata_custom IN)
                {
                    v2f OUT;
                    OUT.vertex = UnityObjectToClipPos(IN.vertex);
                    OUT.texcoord = IN.texcoord;
                    OUT.color = IN.color * _Color;
                    return OUT;
                }

                // 采样邻域 alpha（与您原版完全一致）
                float SampleNeighborhoodAlpha(float2 uv, float radius, int dirCount, int stepCount)
                {
                    float2 texelSize = _MainTex_TexelSize.xy;
                    float r = radius * texelSize;

                    float weightedAlpha = 0.0;
                    float totalWeight = 0.0;

                    for (int i = 0; i < dirCount; i++)
                    {
                        float angle = (float)i / dirCount * 6.2831853;
                        float2 dir = float2(cos(angle), sin(angle));

                        for (int j = 1; j <= stepCount; j++)
                        {
                            float t = (float)j / stepCount;
                            float dist = r * t;
                            float2 sampleUV = uv + dir * dist;
                            float alpha = tex2D(_MainTex, sampleUV).a;
                            float weight = exp(-t * t * 1.2);
                            weightedAlpha += alpha * weight;
                            totalWeight += weight;
                        }
                    }
                    return weightedAlpha / max(totalWeight, 0.001);
                }

                fixed4 frag(v2f IN) : SV_Target
                {
                    // 1. 基础采样
                    fixed4 main = tex2D(_MainTex, IN.texcoord) * IN.color;
                    main.rgb *= main.a;
                    float currentAlpha = main.a;

                    // 2. 垂直渐变叠加（只作用于不透明区域）
                    if (currentAlpha > 0.001)
                    {
                        float y = IN.texcoord.y;   // 0=底部，1=顶部
                        float factor = _GradientBottomAlpha * (1.0 - y / max(_GradientHeight, 0.001));
                        factor = saturate(factor);

                        if (factor > 0.001)
                        {
                            float3 gradColor = _GradientColor.rgb;
                            main.rgb = lerp(main.rgb, gradColor * main.a, factor);
                        }
                    }

                    // ---- 以下完全沿用您原版的阴影和边缘光逻辑 ----
                    float threshold = 0.02;

                    float shadow = 0.0;
                    if (currentAlpha < 0.99)
                    {
                        float avgAlphaShadow = SampleNeighborhoodAlpha(IN.texcoord, _ShadowRadius, 24, 16);
                        if (avgAlphaShadow > threshold)
                        {
                            float raw = avgAlphaShadow * 2.0 * _ShadowStrength * (1.0 - currentAlpha);
                            raw = saturate(raw);
                            shadow = smoothstep(0.0, 1.0, raw);
                        }
                    }

                    float rim = 0.0;
                    if (currentAlpha > 0.1)
                    {
                        float avgAlphaRim = SampleNeighborhoodAlpha(IN.texcoord, _RimRadius, 24, 16);
                        float diff = currentAlpha - avgAlphaRim;
                        if (diff > 0.05)
                        {
                            float raw = diff * _RimStrength;
                            raw = saturate(raw);
                            rim = smoothstep(0.0, 1.0, raw);
                        }
                    }

                    float3 finalRgb = main.rgb;
                    float finalAlpha = main.a;

                    if (shadow > 0.001)
                    {
                        float3 shadowRgb = _ShadowColor.rgb * _ShadowColor.a;
                        float shadowBlend = shadow * (1.0 - main.a);
                        finalRgb += shadowRgb * shadowBlend;
                        finalAlpha = max(main.a, shadow * _ShadowColor.a);
                    }

                    if (rim > 0.001)
                    {
                        float3 rimRgb = _RimColor.rgb * _RimColor.a;
                        float rimBlend = rim * main.a;
                        finalRgb += rimRgb * rimBlend;
                    }

                    return float4(finalRgb, finalAlpha);
                }
                ENDCG
            }
        }
}