Shader "Echo/Gem"
{
    Properties
    {
        _Color("Gem Color", Color) = (1,1,1,1)
        _Depth("Extraction Depth", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "EchoGem"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "../Script/EchoField.cginc"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Depth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                // A literal plane at zero, without a singular zero-scale Transform.
                // Retained cut normals depict the facets even before extrusion.
                input.positionOS.z *= _Depth;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 normal = normalize(input.normalWS);
                half3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half3 key = normalize(half3(-.45, .75, .65));
                half facet = .3 + .7 * saturate(dot(normal, key));
                half sparkle = pow(saturate(dot(reflect(-key, normal), view)), 28) * .7;
                half3 cutColor = _Color.rgb * facet + sparkle * lerp(_Color.rgb, 1, .65);
                // Match the original item's echo field, including its colored pulses.
                // Facet highlights are also echo-gated: no permanent illumination.
                return half4(cutColor * EchoColor(input.positionWS), 1);
            }
            ENDHLSL
        }
    }
}
