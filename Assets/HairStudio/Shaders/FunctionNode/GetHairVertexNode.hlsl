#ifndef HairStudio_HairStrandBillboarding
#define HairStudio_HairStrandBillboarding

#include "../HairStudio_Math.cginc"
#include "../HairStudio_Data.cginc"

uniform float3 _ScalpWorldPosition;
uniform float4x4 _InverseRotation;

uniform StructuredBuffer<SegmentForShading> _SegmentsForShading;

void GetHairVertexNode_float(
	float index,
	float isRootSide,
	float rate,
	float side,
	float tesselationTime,
	float3 worldCameraPos,
	float rootThickness,
	float tipThickness,
	float thicknessDecreaseRate,
	bool drawTangents,
	bool drawNormals,
	out float3 worldPosition,
	out float3 normal,
	out float3 tangent)
{
	SegmentForShading seg = _SegmentsForShading[(uint) index];

	float3 posOnCurve = seg.pos;
	float3 tangentOnCurve = seg.tangent;
	if (tesselationTime != 0)
	{
		SegmentForShading next = { { 0, 0, 0 }, { 0, 0, 0 }, { 0, 0, 0 } };
		next = _SegmentsForShading[index + 1];
		float len = length(seg.pos - next.pos) * 0.3f;
		posOnCurve = GetBezier(seg.pos, seg.tangent, next.pos, next.tangent, tesselationTime, len);
		tangentOnCurve = GetTangent(seg.pos, seg.tangent, next.pos, next.tangent, tesselationTime, len);
	}

	// tangent
	tangent = tangentOnCurve;

	// normal
	float3 scalpToSeg = normalize(posOnCurve - _ScalpWorldPosition);
	normal = cross(cross(tangentOnCurve, scalpToSeg), tangentOnCurve);
	
	// position
	// offset due to thickness
	float localThickness = lerp(rootThickness, tipThickness, saturate(invLerp(thicknessDecreaseRate, 1, rate)));
	float offset = (localThickness * 0.5) * side;

	float3 viewToSeg = normalize(posOnCurve - worldCameraPos);
	float3 right = cross(viewToSeg, drawNormals ? normal : tangent);
	right *= (drawTangents || drawNormals) && isRootSide == 1 ? 8 : 1;

	worldPosition = posOnCurve;
	if (isRootSide == 0)
	{
		if (drawTangents)
		{
			worldPosition = _SegmentsForShading[index + 1].pos + tangent * 0.01f;
		}
		else if (drawNormals)
		{
			worldPosition = _SegmentsForShading[index + 1].pos + normal * 0.01f;
		}
	}
	// moving point to the side of the billboard
	worldPosition += right * offset;
	
	// changing transform to math further object transform
	tangent = mul(_InverseRotation, float4(tangent, 1)).xyz;
	normal = mul(_InverseRotation, float4(normal, 1)).xyz;
}
#endif