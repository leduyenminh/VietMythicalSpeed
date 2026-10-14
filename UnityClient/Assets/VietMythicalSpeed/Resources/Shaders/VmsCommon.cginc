#ifndef VMS_COMMON_INCLUDED
#define VMS_COMMON_INCLUDED

// Global uniforms set from C# (VMS.Rendering.VmsGraphics).
float4 _VmsFogColor;
float4 _VmsFogRange;  // x = start, y = end, z = 1 / (end - start)
float _VmsPulse;      // 1 on the beat, decays to 0
float _VmsEnergy;     // song loudness 0..1

inline float VmsFog(float3 worldPos)
{
    float d = distance(worldPos, _WorldSpaceCameraPos);
    return saturate((d - _VmsFogRange.x) * _VmsFogRange.z);
}

#endif
