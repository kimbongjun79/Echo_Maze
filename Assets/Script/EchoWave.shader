Shader "Echo/Wave"{
Properties{_Base("Base",Float)=1 _Off("Depth Offset",Float)=0 _Color("Color",Color)=(1,1,1,1)}
SubShader{Tags{"RenderType"="Opaque"} Offset [_Off],[_Off]
Pass{CGPROGRAM
#pragma vertex v
#pragma fragment f
#pragma target 3.0
#include "UnityCG.cginc"
float _Base,_EchoTime,_EchoG,_EchoCell,_EchoI,_EchoCt,_EchoCR;float4 _Color;float4 _EchoP[16];float4 _EchoC[16];float4 _EchoH[256];float _EchoR[16];float _EchoHY[256];sampler2D _EchoD;
struct o{float4 p:SV_POSITION;float3 w:TEXCOORD0;};
o v(float4 p:POSITION){o r;r.p=UnityObjectToClipPos(p);r.w=mul(unity_ObjectToWorld,p).xyz;return r;}
// 경로 거리: 주변 2x2 칸 중 min(칸 BFS 거리 + 칸 중심 직선거리)
float pd(float k,float2 u,float2 c){return tex2Dlod(_EchoD,float4((c.x+.5)/_EchoG,(k*_EchoG+c.y+.5)/(_EchoG*16),0,0)).r*63.75+distance(u,c)*_EchoCell;}
fixed4 f(o i):SV_Target{float3 hc=0;float hn=1e-4,b=0;float2 u=i.w.xz/_EchoCell,c=floor(u);
[loop]for(int k=0;k<16;k++){float t=_EchoTime-_EchoP[k].w;if(t<0||t>10)continue;
float r=t*20,d=min(min(pd(k,u,c),pd(k,u,c+float2(1,0))),min(pd(k,u,c+float2(0,1)),pd(k,u,c+1)));
d=max(distance(i.w,_EchoP[k].xyz),d-_EchoCell*0.5);
float ring=exp(-pow((d-r)*3,2)),trail=d<r?exp(-(r-d)*.05):0;
float fa=max(1-t/10,0)*(1-smoothstep(_EchoR[k]*.5,_EchoR[k],d)),wg=(ring*4+trail*.4)*fa;b+=(ring+trail*.4)*fa;hc+=_EchoC[k].rgb*wg;hn+=wg;}
// 음파 입자 충돌 발광 + 잔향: q=(x,z,시작시각,±(T*10 정수부+세기*.9 소수부)), 음수=몬스터
[loop]for(int j=0;j<256;j++){float4 q=_EchoH[j];float a=_EchoTime-q.z,e=abs(q.w),T=floor(e)/10;if(a<0||a>T)continue;
float I=frac(e)/.9*smoothstep(1.5,0,distance(i.w,float3(q.x,_EchoHY[j],q.y)))*pow(1-a/T,2)*(1+2*exp(-a*8));b+=I;hc+=(q.w<0?float3(1,.1,.1):float3(1,1,1))*I*4;hn+=I*4;}
// 채널별 min 클램프 시 흰 파동과 겹치면 붉은색이 포화되어 사라지던 문제 수정
float n=exp(-distance(i.w,_WorldSpaceCameraPos)/_EchoCR);
return fixed4(lerp(float3(.75,.88,1),1,n*_EchoCt)*_Color.rgb*hc/hn*min(b,1.5)*lerp(1,n*2,_EchoCt)*_Base*_EchoI,1);}
ENDCG}}}
