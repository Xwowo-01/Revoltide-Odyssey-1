Shader "Custom/OldPhotoEffect"
{
    Properties
    {
        _MainTex("Base Texture", 2D) = "white" {}
        _NoiseTex("Noise Texture", 2D) = "white" {}
        _EdgeMask("Edge Mask", 2D) = "white" {}

        _YellowTint("泛黄颜色", Color) = (0.9, 0.8, 0.6, 1)
        _FadeAmount("褪色强度", Range(0, 1)) = 0.5
        _EdgeWear("边缘磨损", Range(0, 1)) = 0.3
        _HoleThreshold("镂空阈值", Range(0, 1)) = 0.8
    }

        SubShader
        {
            Tags { "Queue" = "Transparent" "RenderType" = "Transparent" }
            Blend SrcAlpha OneMinusSrcAlpha

            Pass
            {
                CGPROGRAM
                #pragma vertex vert
                #pragma fragment frag
                #include "UnityCG.cginc"

                struct appdata
                {
                    float4 vertex : POSITION;
                    float2 uv : TEXCOORD0;
                };

                struct v2f
                {
                    float2 uv : TEXCOORD0;
                    float4 vertex : SV_POSITION;
                    float2 noiseUV : TEXCOORD1;
                };

                sampler2D _MainTex, _NoiseTex, _EdgeMask;
                float4 _MainTex_ST;
                fixed4 _YellowTint;
                float _FadeAmount, _EdgeWear, _HoleThreshold;

                v2f vert(appdata v)
                {
                    v2f o;
                    o.vertex = UnityObjectToClipPos(v.vertex);
                    o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                    o.noiseUV = v.uv * float2(3,3); // 噪声平铺缩放
                    return o;
                }

                fixed4 frag(v2f i) : SV_Target
                {
                    // 基础颜色采样
                    fixed4 col = tex2D(_MainTex, i.uv);

                // 噪波采样（使用RG两个通道）
                fixed2 noise = tex2D(_NoiseTex, i.noiseUV).rg;

                // 边缘遮罩采样
                fixed edgeMask = tex2D(_EdgeMask, i.uv).r;

                // ===== 颜色处理阶段 =====
                // 1. 添加泛黄效果
                col.rgb = lerp(col.rgb, _YellowTint.rgb, 0.3);

                // 2. 不均匀褪色（用噪声控制）
                float fadeFactor = _FadeAmount * (0.8 + noise.x * 0.2);
                col.rgb = lerp(col.rgb, col.rgb * 0.7, fadeFactor);

                // 3. 添加陈年变色效果
                col.r -= noise.y * 0.1;
                col.g += noise.x * 0.05;
                col.b *= 0.9;

                // ===== 边缘磨损处理 =====
                // 边缘变暗+透明度变化
                float edgeWear = _EdgeWear * (1 - edgeMask);
                col.rgb *= (1 - edgeWear * 0.5);
                col.a *= (1 - edgeWear * 0.8);

                // ===== 镂空效果 =====
                // 使用噪声生成随机孔洞
                float holeMask = step(_HoleThreshold, noise.x * edgeWear);
                col.a *= holeMask;

                return col;
            }
            ENDCG
        }
        }
}
