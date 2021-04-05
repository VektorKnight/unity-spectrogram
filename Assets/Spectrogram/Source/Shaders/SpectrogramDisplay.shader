Shader "Unlit/SpectrogramDisplay"
{
    Properties
    {
        _MainTex ("Spectrogram Texture", 2D) = "white" {}
        _GradientTex ("Gradient Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // make fog work
            #pragma multi_compile_fog

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            sampler2D _GradientTex;
            float4 _MainTex_ST;
            float4 _GradientTex_ST;

            float average_rgb(float3 rgb) {
                return (rgb.r + rgb.g + rgb.b) / 3.0;
            }

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                UNITY_TRANSFER_FOG(o,o.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Flip the texture on the X-axis so it appears to scroll from the right.
                i.uv.x = 1.0 - i.uv.x;

                // Sample the spectrogram texture and average the RGB values.
                const float4 spec_sample = tex2D(_MainTex, i.uv);
                const float grad_lookup = average_rgb(spec_sample.rgb);

                // Sample the 1D gradient texture to get the proper color.
                float4 grad_sample = tex2D(_GradientTex, float2(grad_lookup, 0.0));
                
                // apply fog
                UNITY_APPLY_FOG(i.fogCoord, col);
                return grad_sample;
            }
            ENDCG
        }
    }
}
