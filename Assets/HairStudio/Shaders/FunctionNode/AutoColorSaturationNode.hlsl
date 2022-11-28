#ifndef HairStudio_AutoColorSaturation
#define HairStudio_AutoColorSaturation

#include "../HairStudio_Math.cginc"


void AutoColorSaturationNode_float(
	float3 color,
	float3 colorTRT,
	float colorNuance,
	float strandIndex01,
	float saturationMultiplier,
	out float3 resultColor)
{
	resultColor = color != float3(0, 0, 0) ? color : hsv2rgb(rgb2hsv(colorTRT) * float3(1, saturationMultiplier, 1));
	resultColor = saturate(lerp(resultColor - colorNuance, resultColor + colorNuance, strandIndex01));
}
#endif