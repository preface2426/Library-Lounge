Shader "NetCafe/DesktopDisplay"
{
    Properties { _MainTex ("Desktop", 2D) = "black" {} _FlipY ("Flip Native Desktop Y", Float) = 1 }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex); float _FlipY;
            struct Input { float4 position : POSITION; float2 uv : TEXCOORD0; };
            struct Output { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
            Output Vert(Input v) { Output o; o.position = TransformObjectToHClip(v.position.xyz); o.uv = float2(v.uv.x, lerp(v.uv.y, 1-v.uv.y, saturate(_FlipY))); return o; }
            half4 Frag(Output v) : SV_Target { return half4(SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,v.uv).rgb,1); }
            ENDHLSL
        }
    }
}
