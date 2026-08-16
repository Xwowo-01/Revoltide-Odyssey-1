Shader "Custom/CurveBrightness" {
    Properties{
        _MainTex("Main Tex", 2D) = "white" {}
        _CurveTex("Curve Texture", 2D) = "white" {}
    }

        SubShader{
            Pass {
                CGPROGRAM
                #pragma vertex vert
                #pragma fragment frag
                #include "UnityCG.cginc"

                struct v2f {
                    float4 pos : SV_POSITION;
                    float2 uv : TEXCOORD0;
                };

                sampler2D _MainTex;
                sampler2D _CurveTex;

                v2f vert(appdata_base v) {
                    v2f o;
                    o.pos = UnityObjectToClipPos(v.vertex);
                    o.uv = v.texcoord;
                    return o;
                }

                fixed4 frag(v2f i) : SV_Target {
                    fixed4 col = tex2D(_MainTex, i.uv);

                // 计算原始亮度
                float luminance = 0.299 * col.r + 0.587 * col.g + 0.114 * col.b;

                // 采样曲线纹理获取目标亮度
                float curveValue = tex2D(_CurveTex, float2(luminance, 0.5)).r;

                // 计算缩放比例并调整颜色
                float scale = (luminance > 0.0) ? (curveValue / max(luminance, 0.0001)) : 0.0;
                col.rgb *= scale;

                return col;
            }
            ENDCG
        }
    }
}
