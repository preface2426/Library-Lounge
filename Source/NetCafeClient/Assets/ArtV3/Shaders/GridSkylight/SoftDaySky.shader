Shader "LibraryLounge/SoftDaySky"
{
    Properties
    {
        _Zenith("Zenith",Color)=(0.34,0.52,0.76,1)
        _Horizon("Horizon",Color)=(0.73,0.82,0.88,1)
        _Ground("Below horizon",Color)=(0.63,0.69,0.72,1)
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Zenith,_Horizon,_Ground;
            CBUFFER_END
            struct Varyings{float4 positionCS:SV_POSITION;float3 ray:TEXCOORD0;};
            Varyings Vert(float4 positionOS:POSITION)
            {Varyings o;o.positionCS=TransformObjectToHClip(positionOS.xyz);o.ray=TransformObjectToWorldDir(positionOS.xyz);return o;}
            half4 Frag(Varyings input):SV_Target
            {
                float3 ray=normalize(input.ray);
                float height=saturate(ray.y);
                float3 sky=lerp(_Horizon.rgb,_Zenith.rgb,pow(height,0.55));
                if(ray.y<0)sky=lerp(_Horizon.rgb,_Ground.rgb,saturate(-ray.y*3));
                Light sun=GetMainLight();
                float disk=smoothstep(0.99965,0.99985,dot(ray,sun.direction));
                sky+=disk*float3(1.1,1.04,0.90);
                return half4(sky,1);
            }
            ENDHLSL
        }
    }
}
