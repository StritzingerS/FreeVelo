#ifndef FREEVELO_WIND_INCLUDED
#define FREEVELO_WIND_INCLUDED

// Wind for foliage (FreeVelo/LitWind): the whole crown leans and sways with the wind, more the higher a vertex is
// above the model's foot, and the leaves flutter a little. Phases come from the instance's world position, so
// neighbouring trees do not move in step, and slow gusts roll across the landscape.
// Global inputs (set by WorldAnimation.cs): _FvWindDir (xz, normalised), _FvWindStrength (0 = calm, 1 = breezy).

float4 _FvWindDir;
float _FvWindStrength;

float3 FreeVeloWind(float3 positionOS)
{
    float strength = _FvWindStrength;
    if (strength <= 0.0) return positionOS;

    float3 originWS = TransformObjectToWorld(float3(0, 0, 0));
    float3 positionWS = TransformObjectToWorld(positionOS);
    float h = max(0.0, positionWS.y - originWS.y);
    float bend = h * h / (h + 8.0); // about 0.8 at 3 m, 14 at 20 m

    float t = _Time.y;
    float2 dir = _FvWindDir.xz;
    float phase = dot(originWS.xz, float2(0.137, 0.171));
    // Gusts: a slow wave rolling along the wind direction (about 60 m long, 0.25 per second).
    float gust = 0.55 + 0.45 * sin(dot(originWS.xz, dir) * 0.1 - t * 1.6);
    float sway = sin(t * 1.3 + phase) * 0.6 + sin(t * 2.7 + phase * 1.7) * 0.25;
    float lean = 0.5 + sway; // leans downwind on average, swings back and forth around that
    float2 offset = dir * (lean * gust * bend * 0.018 * strength);

    // Leaf flutter: small, fast, different for every vertex.
    float flutter = sin(t * 7.0 + dot(positionWS, float3(2.1, 1.7, 2.9))) * 0.012 * strength * saturate(h / 3.0);

    float3 displacedWS = positionWS + float3(offset.x, 0, offset.y) + float3(flutter, flutter * 0.5, -flutter);
    return TransformWorldToObject(displacedWS);
}

#endif
