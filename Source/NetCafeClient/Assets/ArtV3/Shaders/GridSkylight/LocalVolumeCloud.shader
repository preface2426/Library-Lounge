Shader "LibraryLounge/LocalVolumeCloud"
{
    Properties
    {
        _Noise("Three dimensional density", 3D) = "white" {}
        _CloudColor("Cloud light", Color) = (1,0.98,0.94,1)
        _ShadeColor("Cloud shadow", Color) = (0.57,0.66,0.77,1)
        _Density("Density per metre", Range(0.05,2)) = 0.6
        _Steps("Ray samples", Range(16,48)) = 32
        _Seed("Shape offset", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "LocalVolumeCloud"
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Front
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE3D(_Noise); SAMPLER(sampler_Noise);
            CBUFFER_START(UnityPerMaterial)
                float4 _CloudColor, _ShadeColor, _Seed;
                float _Density, _Steps;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings Vert(Attributes input)
            {
                Varyings o; UNITY_SETUP_INSTANCE_ID(input); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS=TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS=TransformWorldToHClip(o.positionWS); return o;
            }
            float Density(float3 p)
            {
                // Ellipsoidal lobes occupy a bounded 3D region; never a billboard.
                float d=min(length((p-float3(-0.20,-0.05,0))*float3(3.6,3.7,3.2)),
                            length((p-float3(0.16,0.04,0.03))*float3(3.2,3.0,3.5)));
                d=min(d,length((p-float3(0.02,-0.11,-0.14))*float3(3.2,4.3,3.8)));
                if(d>=1)return 0;
                float n=SAMPLE_TEXTURE3D_LOD(_Noise,sampler_Noise,p*2.3+_Seed.xyz,0).r;
                float detail=SAMPLE_TEXTURE3D_LOD(_Noise,sampler_Noise,p*6.7+_Seed.yzx,1).r;
                return saturate((1-d)*3.8-(1-n)*1.25-(1-detail)*0.3)*_Density;
            }
            half4 Frag(Varyings input):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 rayWS=normalize(input.positionWS-GetCameraPositionWS());
                float3 origin=TransformWorldToObject(GetCameraPositionWS());
                float3 direction=mul((float3x3)unity_WorldToObject,rayWS);
                // Signed epsilon protects parallel rays without changing slab order.
                float3 safeDirection=lerp(-1.0,1.0,step(0.0,direction))*max(abs(direction),1e-6);
                float3 a=(-0.5-origin)/safeDirection,b=(0.5-origin)/safeDirection;
                float3 nearPlane=min(a,b),farPlane=max(a,b);
                float start=max(0,max(nearPlane.x,max(nearPlane.y,nearPlane.z)));
                float finish=min(farPlane.x,min(farPlane.y,farPlane.z));
                if(finish<=start)discard;
                int count=clamp((int)_Steps,16,48);
                float dt=(finish-start)/count;
                Light sun=GetMainLight();
                float3 towardSun=normalize(mul((float3x3)unity_WorldToObject,sun.direction));
                float transmission=1;float3 radiance=0;
                [loop] for(int i=0;i<48;i++)
                {
                    if(i>=count||transmission<0.015)break;
                    float3 p=origin+direction*(start+(i+0.5)*dt);
                    float density=Density(p);
                    if(density<0.001)continue;
                    float optical=Density(p+towardSun*0.065)*2.5+Density(p+towardSun*0.15)*1.3;
                    float lighting=saturate(0.35+0.65*exp(-optical)+saturate(p.y+0.3)*0.18);
                    float3 color=lerp(_ShadeColor.rgb,_CloudColor.rgb,lighting);
                    float opacity=1-exp(-density*dt);
                    radiance+=transmission*opacity*color;
                    transmission*=1-opacity;
                }
                return half4(radiance,1-transmission);
            }
            ENDHLSL
        }
    }
}
