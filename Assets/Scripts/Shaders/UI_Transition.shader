Shader "Custom/FadeTransition"
{
    Properties
    {
        _MainTex("Sprite Texture", 2D) = "white" {}
        _Color("Tint", Color) = (1,1,1,1)
        _Fade("Fade", Range(-1,1)) = 0
        _EdgeWidth("Edge Width", Range(0, 0.5)) = 0.05
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
            Blend SrcAlpha OneMinusSrcAlpha

            Pass
            {
                CGPROGRAM
                #pragma vertex vert
                #pragma fragment frag
                #pragma target 2.0
                #include "UnityCG.cginc"

                struct appdata_t
                {
                    float4 vertex : POSITION;
                    float4 color : COLOR;
                    float2 texcoord : TEXCOORD0;
                };

                struct v2f
                {
                    float4 vertex : SV_POSITION;
                    float4 color : COLOR;
                    float2 texcoord : TEXCOORD0;
                };

                sampler2D _MainTex;
                float4 _MainTex_ST;
                float4 _Color;
                float _Fade;
                float _EdgeWidth;

                v2f vert(appdata_t v)
                {
                    v2f o;
                    o.vertex = UnityObjectToClipPos(v.vertex);
                    o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                    o.color = v.color * _Color;
                    return o;
                }

                fixed4 frag(v2f i) : SV_Target
                {
                    fixed4 col = tex2D(_MainTex, i.texcoord) * i.color;
                    float uvx = i.texcoord.x;
                    float edge = _EdgeWidth * 0.5;
                    float alpha = 1.0;

                    if (_Fade >= 0.0)
                    {
                        // 正向：从左到右淡出，极端值（1）时中心移至 1+edge，确保全透明
                        float center = _Fade * (1.0 + edge);
                        alpha = smoothstep(center - edge, center + edge, uvx);
                    }
                    else
                    {
                        // 反向：从右到左淡出，极端值（-1）时中心移至 -edge，确保全透明
                        float center = (1.0 + _Fade) * (1.0 + edge) - edge;
                        alpha = 1.0 - smoothstep(center - edge, center + edge, uvx);
                    }

                    col.a *= alpha;
                    return col;
                }
                ENDCG
            }
        }
}