#ifndef ECHO_FIELD_INCLUDED
#define ECHO_FIELD_INCLUDED

// 맵과 몬스터가 동일한 BFS 음파 거리, 잔향, 대비/감쇠를 사용합니다.
float _EchoTime, _EchoG, _EchoCell, _EchoI, _EchoCt, _EchoCR;
float4 _EchoP[16], _EchoC[16], _EchoH[256];
float _EchoR[16], _EchoHY[256];
sampler2D _EchoD;

float EchoPathDistance(float k, float2 u, float2 c)
{
    return tex2Dlod(_EchoD, float4((c.x + .5) / _EchoG, (k * _EchoG + c.y + .5) / (_EchoG * 16), 0, 0)).r * 63.75
        + distance(u, c) * _EchoCell;
}

float3 EchoColor(float3 worldPosition)
{
    float3 hc = 0;
    float hn = 1e-4, b = 0;
    float2 u = worldPosition.xz / _EchoCell, c = floor(u);
    [loop] for (int k = 0; k < 16; k++)
    {
        float t = _EchoTime - _EchoP[k].w;
        if (t < 0 || t > 10) continue;
        float r = t * 20;
        float d = min(min(EchoPathDistance(k, u, c), EchoPathDistance(k, u, c + float2(1, 0))),
                      min(EchoPathDistance(k, u, c + float2(0, 1)), EchoPathDistance(k, u, c + 1)));
        d = max(distance(worldPosition, _EchoP[k].xyz), d - _EchoCell * .5);
        float ring = exp(-pow((d - r) * 3, 2)), trail = d < r ? exp(-(r - d) * .05) : 0;
        float fa = max(1 - t / 10, 0) * (1 - smoothstep(_EchoR[k] * .5, _EchoR[k], d));
        float wg = (ring * 4 + trail * .4) * fa;
        b += (ring + trail * .4) * fa;
        hc += _EchoC[k].rgb * wg;
        hn += wg;
    }
    // 음파 입자의 벽 충돌 잔향. 음수 에너지는 몬스터의 붉은 음파입니다.
    [loop] for (int j = 0; j < 256; j++)
    {
        float4 q = _EchoH[j];
        float a = _EchoTime - q.z, e = abs(q.w), T = floor(e) / 10;
        if (a < 0 || a > T) continue;
        float intensity = frac(e) / .9 * smoothstep(1.5, 0, distance(worldPosition, float3(q.x, _EchoHY[j], q.y)))
            * pow(1 - a / T, 2) * (1 + 2 * exp(-a * 8));
        b += intensity;
        hc += (q.w < 0 ? float3(1, .1, .1) : float3(1, 1, 1)) * intensity * 4;
        hn += intensity * 4;
    }
    float n = exp(-distance(worldPosition, _WorldSpaceCameraPos) / _EchoCR);
    return lerp(float3(.75, .88, 1), 1, n * _EchoCt) * hc / hn * min(b, 1.5) * lerp(1, n * 2, _EchoCt) * _EchoI;
}

#endif
