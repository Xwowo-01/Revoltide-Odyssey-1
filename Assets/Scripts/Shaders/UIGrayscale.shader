Shader "UI/Grayscale"
{
    Properties
    {
        [PerRendererData] _MainTex("Sprite Texture", 2D) = "white" {}
        _Color("Tint", Color) = (1,1,1,1)
        _Grayscale("Grayscale", Range(0, 1)) = 0 // 黑白效果开关
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
            ZTest[unity_GUIZTestMode]
            Blend SrcAlpha OneMinusSrcAlpha

            Pass
            {
                CGPROGRAM
                #pragma vertex vert
                #pragma fragment frag
                #include "UnityCG.cginc"
                #include "UnityUI.cginc"

                struct appdata_t
                {
                    float4 vertex   : POSITION;
                    float4 color    : COLOR;
                    float2 texcoord : TEXCOORD0;
                };

                struct v2f
                {
                    float4 vertex   : SV_POSITION;
                    fixed4 color : COLOR;
                    float2 texcoord  : TEXCOORD0;
                };

                sampler2D _MainTex;
                fixed4 _Color;
                float _Grayscale;

                v2f vert(appdata_t v)
                {
                    v2f OUT;
                    OUT.vertex = UnityObjectToClipPos(v.vertex);
                    OUT.texcoord = v.texcoord;
                    OUT.color = v.color * _Color;
                    return OUT;
                }

                fixed4 frag(v2f IN) : SV_Target
                {
                    // 采样纹理颜色
                    half4 color = tex2D(_MainTex, IN.texcoord) * IN.color;

                    // 转换灰度（亮度公式）
                    float luminance = dot(color.rgb, float3(0.299, 0.587, 0.114));
                    half4 grayscaleColor = half4(luminance, luminance, luminance, color.a);

                    // 根据开关混合颜色
                    return lerp(color, grayscaleColor, _Grayscale);
                }
                ENDCG
            }
        }
}
