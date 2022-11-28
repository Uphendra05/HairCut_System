#ifndef MarschnerLightingModel
#define MarschnerLightingModel

#include "HairStudio_Math.cginc"
#include "HairStudio_SelfShadow.cginc"

struct SurfaceOutputHair
{
	fixed4 Albedo;
	fixed3 Normal;
	fixed3 Emission;
	fixed Alpha;

	float3 iTangent;
	float random;
	float selfShadowStrength;
};

uniform float3 _ColorD, _ColorR, _ColorTT, _ColorTRT, _ColorG;
uniform float _ColorNuance;
uniform float _ScaleD, _ScaleR, _ScaleTT, _ScaleTRT, _ScaleG;
uniform float _AlphaR, _AlphaTT, _AlphaTRT;
uniform float _BetaR, _BetaTT, _BetaTRT;
uniform float _GammaTT, _GammaG, _PhiG;

/// Normalized gaussian with offset
//inline float g(float beta, float alpha, float theta_h)
//{
//    float n = theta_h - alpha;
//    return fast_exp(-(n * n) / (2.0f * beta)) / sqrtf(2.0f * AI_PI * beta);
//}

inline float3 SpecularFresnel(float3 F0, float vDotH)
{
	return F0 + (1.0f - F0) * pow(1 - vDotH, 5);
}

inline float gaussian(float expected, float deviation)
{
	return exp(-square(expected) / (2.0 * square(deviation)));
}

float3 GetMarschnerSpecular(SurfaceOutputHair surf, float3 toLight, float3 toEye, float3 lightColor, float3 indirectColor)
{
	float3 tangent = normalize(surf.iTangent);
	toLight = normalize(toLight);
	toEye = normalize(toEye);
    // theta
	float lightDotTangent = dot(toLight, tangent);
	float eyeDotTangent = dot(toEye, tangent);
	float thetaLight = (pi * 0.5f) - acos(lightDotTangent);
	float thetaEye = (pi * 0.5f) - acos(eyeDotTangent);
	float thetaH = (thetaLight + thetaEye) * 0.5f;
	float thetaD = (thetaEye - thetaLight) * 0.5f;
	float cos2thetaD = square(cos(thetaD));
    
    // phi
	const float3 toLightPerp = normalize(toLight - lightDotTangent * tangent);
	const float3 toEyePerp = normalize(toEye - eyeDotTangent * tangent);
	float CosPhi = dot(toEyePerp, toLightPerp);

	float phi = acos(CosPhi);
    
	float locAlphaR = radians(_AlphaR);
	float locBetaR = radians(_BetaR);
	
	// realistic TT
	float locScaleTT = _ScaleTT != 0 ? _ScaleTT : _ScaleR * 3;
	float locAlphaTT = _AlphaTT != -20 ? radians(_AlphaTT) : -locAlphaR * 0.5;
	float locBetaTT = _BetaTT != 0 ? radians(_BetaTT) : locBetaR * 0.5;

	// realistic TRT
	float locScaleTRT = _ScaleTRT != 0 ? _ScaleTRT : _ScaleR * 0.5;
	float locAlphaTRT = _AlphaTRT != -20 ? radians(_AlphaTRT) : -locAlphaR * 1.5;
	float locBetaTRT = _BetaTRT != 0 ? radians(_BetaTRT) : locBetaR * 2;

	// realistic G
	float locScaleG = _ScaleG != 0 ? _ScaleG : _ScaleR * 2;

	// realistic D
	float locScaleD = _ScaleD != 0 ? _ScaleD : 1 - locScaleG - _ScaleR - locScaleTRT - locScaleTT;
	locScaleD = saturate(locScaleD);
	
	// realistic color
	float3 localColorR = _ColorR;
	float3 localColorTRT = _ColorTRT;
	float3 localColorD = _ColorD != float3(0, 0, 0) ? _ColorD : hsv2rgb(rgb2hsv(localColorTRT) * float3(1, 0.6f, 1));
	float3 localColorTT = _ColorTT != float3(0, 0, 0) ? _ColorTT : hsv2rgb(rgb2hsv(localColorTRT) * float3(1, 0.8f, 1));
	float3 localColorG = _ColorG != float3(0, 0, 0) ? _ColorG : hsv2rgb(rgb2hsv(localColorTRT) * float3(1, 1.2f, 1));
	
	// color nuance
	localColorR = saturate(lerp(localColorR - _ColorNuance, localColorR + _ColorNuance, surf.random));
	localColorTRT = saturate(lerp(localColorTRT - _ColorNuance, localColorTRT + _ColorNuance, surf.random));
	localColorD = saturate(lerp(localColorD - _ColorNuance, localColorD + _ColorNuance, surf.random));
	localColorTT = saturate(lerp(localColorTT - _ColorNuance, localColorTT + _ColorNuance, surf.random));
	localColorG = saturate(lerp(localColorG - _ColorNuance, localColorG + _ColorNuance, surf.random)); 

	// D    
	float3 d = locScaleD * (localColorD + indirectColor);
	
    // R
	float mr = gaussian(thetaH - locAlphaR, locBetaR);
	float nr = cos(phi / 2);
	float3 r = _ScaleR * localColorR * mr * nr / cos2thetaD;
    
    // TT
	float mtt = gaussian(thetaH - locAlphaTT, locBetaTT);
	float ntt = gaussian(pi - phi, radians(_GammaTT));
	float3 tt = locScaleTT * localColorTT * mtt * ntt / cos2thetaD;

    // TRT-G
	float mtrt = gaussian(thetaH - locAlphaTRT, locBetaTRT);
	float ntrt = cos(phi / 2);
	float3 trt = locScaleTRT * localColorTRT * mtrt * ntrt / cos2thetaD;
    
    // G
	float ng = gaussian(abs(phi) - radians(_PhiG), radians(_GammaG));
	float3 g = mtrt * locScaleG * localColorG * ng / cos2thetaD;
	g *= surf.random;

	float3 S = d + r + tt + trt + g;
	S = saturate(S);
	S *= lightColor;
	return S;
}

half4 LightingMarschner(SurfaceOutputHair s, half3 viewDir, UnityGI gi)
{
    half4 c = half4(0, 0, 0, 1);
	c.rgb = GetMarschnerSpecular(s, gi.light.dir, viewDir, gi.light.color, gi.indirect.diffuse);

	return c;
}

void LightingMarschner_GI(SurfaceOutputHair s, UnityGIInput data, inout UnityGI gi)
{
	gi = UnityGlobalIllumination(data, 1, -data.light.dir); 
} 

#endif