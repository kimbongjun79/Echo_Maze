Shader "Echo/Wave"{
Properties{_Base("Base",Float)=1 _Off("Depth Offset",Float)=0 _Color("Color",Color)=(1,1,1,1)}
SubShader{Tags{"RenderType"="Opaque"} Offset [_Off],[_Off]
Pass{CGPROGRAM
#pragma vertex v
#pragma fragment f
#pragma target 3.0
#include "UnityCG.cginc"
#include "EchoField.cginc"
float _Base;float4 _Color;
struct o{float4 p:SV_POSITION;float3 w:TEXCOORD0;};
o v(float4 p:POSITION){o r;r.p=UnityObjectToClipPos(p);r.w=mul(unity_ObjectToWorld,p).xyz;return r;}
fixed4 f(o i):SV_Target{return fixed4(_Color.rgb*EchoColor(i.w)*_Base,1);}
ENDCG}}}
