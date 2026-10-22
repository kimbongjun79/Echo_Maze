Shader "Echo/Monster"
{
    Properties
    {
        [MainTexture] _BaseMap("Model Base Color", 2D) = "white" {}
        [MainColor] _BaseColor("Model Color", Color) = (1,1,1,1)
        [Normal] _BumpMap("Model Normal", 2D) = "bump" {}
        _BumpScale("Normal Strength", Float) = 1
        _Metallic("Model Metallic", Range(0,1)) = 0
        _Smoothness("Model Smoothness", Range(0,1)) = .5
        _Base("Echo Brightness", Float) = 1
        _EchoTint("Echo Tint", Color) = (1,.25,.2,1)
        _ColorSaturation("Original Color Saturation", Range(0,1)) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "EchoMonster"
            Tags { "LightMode"="UniversalForward" }
            Cull [_Cull]
            ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "EchoField.cginc"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST, _BumpMap_ST;
                half4 _BaseColor;
                half4 _EchoTint;
                half _BumpScale, _Metallic, _Smoothness, _Base, _ColorSaturation;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 tangentWS : TEXCOORD2;
                float4 uv : TEXCOORD3;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normal = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = normal.normalWS;
                output.tangentWS = half4(normal.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                output.uv = float4(TRANSFORM_TEX(input.uv, _BaseMap), TRANSFORM_TEX(input.uv, _BumpMap));
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv.zw), _BumpScale);
                half3 bitangent = input.tangentWS.w * cross(input.normalWS, input.tangentWS.xyz);
                InputData lighting = (InputData)0;
                lighting.positionWS = input.positionWS;
                lighting.normalWS = NormalizeNormalPerPixel(TransformTangentToWorld(normalTS,
                    half3x3(input.tangentWS.xyz, bitangent, input.normalWS)));
                lighting.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                lighting.bakedGI = SampleSH(lighting.normalWS);
                lighting.shadowMask = half4(1,1,1,1);
                lighting.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                SurfaceData surface = (SurfaceData)0;
                surface.albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv.xy).rgb * _BaseColor.rgb;
                // Furniture subdues the original texture; default 1 leaves monster shading unchanged.
                half luminance = dot(surface.albedo, half3(.2126, .7152, .0722));
                surface.albedo = lerp(luminance.xxx, surface.albedo, _ColorSaturation);
                surface.metallic = _Metallic;
                surface.smoothness = _Smoothness;
                surface.normalTS = normalTS;
                surface.occlusion = 1;
                surface.alpha = 1;
                // Neutral furniture tint / red monster tint; no echo means black, never permanent light.
                half3 modelColor = UniversalFragmentPBR(lighting, surface).rgb;
                return half4(modelColor * EchoColor(input.positionWS) * _EchoTint.rgb * _Base, 1);
            }
            ENDHLSL
        }
    }
}
