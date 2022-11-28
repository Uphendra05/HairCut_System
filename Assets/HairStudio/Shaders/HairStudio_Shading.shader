Shader "HairStudio"
{
	Properties
	{
		// Diffuse
		[Header(Diffuse feature)]
		[Tooltip(The color of the diffuse feature. Set to black for realistic override (less saturated than both TRT and TT).)]
		_ColorD("Color D", Color) = (0,0,0,1)
		[Tooltip(The intensity of the diffuse feature. Set the minimum value for realistic override.)]
		_ScaleD("Intensity D", Range(0, 1)) = 0

		// R
		[Space]
		[Header(reflection feature (Marschner R))]
		[Tooltip(The color of the reflection feature. Realistic is white as the light bounces on the surface of the hair.)]
		_ColorR("Color R", Color) = (0, 0, 0, 1)

		[Tooltip(The intensity of the reflection feature. Represent the light absorption. The sum of the intensities of all features should not be greater than 1 (or you would create light out of nowhere).)]
		_ScaleR("Intensity R", Range(0, 1)) = 0.33

		[Tooltip(The angle of the reflection feature. Realistic is between minus 10 and minus 5 degrees.)]
		_AlphaR("Angle (alpha R)", Range(-20, 20)) = -7.5

		[Tooltip(The width of the reflection feature. Realistic is between 5 and 10 degrees.)] 
		_BetaR("Width (beta R)", Range(0, 20)) = 7.5

		// TT
		[Space]
		[Header(Transmittance feature (Marschner TT))]
		[Tooltip(The color of the transmittance feature. Set to black for realistic override(less saturated than TRT).)]
		_ColorTT("Color TT", Color) = (0, 0, 0, 1)
			
		[Tooltip(The intensity of the transmittance feature. Set the minimum value for realistic override (Intensity R x 3).)]
		_ScaleTT("Intensity TT", Range(0, 1)) = 0

		[Tooltip(The angle of the transmittance feature. Set the minimum value for realistic override (minus Angle R x 0.5).)]
		_AlphaTT("Angle (alpha TT)", Range(-20, 20)) = 0

		[Tooltip(The width of the transmittance feature. Set the minimum value for realistic override (Width R x 0.5).)]
		_BetaTT("Width (beta TT)", Range(0, 20)) = 0

		[Tooltip(The width of the transmittance feature perpendicularly to the direction of the hair.)]
		_GammaTT("Azimuthal width (gamma TT)", Range(0.001, 50)) = 2

		// TRT
		[Space]
		[Header(Second reflection feature (Marschner TRT))]
		[Tooltip(The color of the second reflection feature. This is the base color of the hair.)]
		_ColorTRT("Color TRT", Color) = (0, 0, 0, 1)

		[Tooltip(The intensity of the second reflection feature. Set the minimum value for realistic override (Intensity R x 0.5).)]
		_ScaleTRT("Intensity TRT", Range(0, 1)) = 0

		[Tooltip(The angle of the second reflection feature. Set the minimum value for realistic override (minus Angle R x 1.5).)]
		_AlphaTRT("Angle (alpha TRT)", Range(-20, 20)) = 0

		[Tooltip(The width of the second reflection feature. Set the minimum value for realistic override (Width R x 2).)]
		_BetaTRT("Width (beta TRT)", Range(0, 20)) = 0 

		// G
		[Space]
		[Header(Glints (Marschner G))]
		[Tooltip(The color of the glints. Set to black for realistic override (Realistic is more saturated than TRT).)]
		_ColorG("Color G", Color) = (0, 0, 0, 1)

		[Tooltip(The intensity of the glints. Set the minimum value for realistic override (Intensity R x 2).)]
		_ScaleG("Scale G", Range(0, 1)) = 0

		[Tooltip(The width of the glints perpendicularly to the direction of the hair. Realistic is between 10 and 25 degrees.)]
		_GammaG("Azimuthal width (gamma G)", Range(0, 30)) = 2

		[Tooltip(The half angle between the two glints. A higher value will separate the left and right glints.)]
		_PhiG("Separation angle (phi G)", Range(0, 40)) = 2

		// other
		[Space]
		[Header(Hair shape)]
		[Tooltip(A multiplier for all colors applied differently on each strand to add noise on the overall color.)]
		_ColorNuance("Hair color nuance", Range(0, 0.1)) = 0.01

		[Tooltip(The thickness of the hair strands at the root.)]
		_ThicknessRoot("Thickness at root", Range(0, 0.01)) = 0.002

		[Tooltip(The thickness of the hair strands at the tip.)]
		_ThicknessTip("Thickness at tip", Range(0, 0.01)) = 0

		[Tooltip(The distance from the root at which the thickness starts to decrease.)]
		_ThicknessDecreaseRate("Thickness decrease distance", Range(0, 1)) = 0.9
	}
	SubShader
	{
		CGPROGRAM
		#include "HairStudio_MarschnerLightingModel.cginc"
		#include "HairStudio_Data.cginc"
		#pragma surface surf Marschner vertex:VertBase addshadow fullforwardshadows
		
		#pragma target 4.5

	    struct SegmentDef
		{
			float3 initialLocalPos;
			float roughnessRate;
			float eccentricityRate;
		};

#if defined(SHADER_API_D3D11) || defined(SHADER_API_METAL) || defined(SHADER_API_MOBILE)
		StructuredBuffer<SegmentForShading> _SegmentsForShading;
		StructuredBuffer<SegmentDef> _SegmentDefinitions; 
#endif

		struct Input {
			float3 wPos;
			float3 iTangent;
			int index;
			float random;
			float2 uv_HairColorTex;
		};

		uniform float _ThicknessRoot;
		uniform float _ThicknessDecreaseRate;
		uniform float _ThicknessTip;
				
		void VertBase(inout appdata_base v, out Input o)
		{
			UNITY_INITIALIZE_OUTPUT(Input,o);
#if defined(SHADER_API_D3D11) || defined(SHADER_API_METAL) || defined(SHADER_API_MOBILE)
			float strandIndex01 = v.vertex.x;
			uint segmentIndex = v.normal.x;
			float segmentRate = v.normal.y;
			int segmentSide = v.normal.z;
			float curveTime = v.vertex.y;

			SegmentForShading seg = _SegmentsForShading[segmentIndex];
			SegmentDef def = _SegmentDefinitions[segmentIndex];

			// finding the vertex on the strand curve
			float3 posOnCurve = seg.pos;
			float3 tangentOnCurve = seg.tangent;
			if (curveTime != 0) {
				SegmentForShading next = _SegmentsForShading[segmentIndex + 1];
				float len = length(seg.pos - next.pos) * 0.3;
				posOnCurve = GetBezier(seg.pos, seg.tangent, next.pos, next.tangent, curveTime, len);
				tangentOnCurve = GetTangent(seg.pos, seg.tangent, next.pos, next.tangent, curveTime, len);
			}

			// hair width offset
			float localThickness = lerp(_ThicknessRoot, _ThicknessTip, saturate(invLerp(_ThicknessDecreaseRate, 1, segmentRate)));
            float offset = (localThickness * 0.5f) * segmentSide;

			float3 viewToSeg = normalize(posOnCurve - _WorldSpaceCameraPos);
			float3 right = normalize(cross(viewToSeg, normalize(tangentOnCurve)));

			float3 vertexPos = posOnCurve + right * offset;

			v.vertex.xyz = mul(unity_WorldToObject, float4(vertexPos.xyz, 1.0)).xyz;
			o.index = segmentIndex;
			o.iTangent = tangentOnCurve;
			o.random = strandIndex01;
#endif	
		}
		
		void surf (Input IN, inout SurfaceOutputHair o)
		{
			o.random = IN.random;
			o.iTangent = IN.iTangent;
		}
		ENDCG
	}

	SubShader
	{
		Pass
		{
			 Name "ShadowCaster"
			 Tags { "LightMode" = "ShadowCaster" }

			 Fog { Mode Off }
			 ZWrite On ZTest Less Cull Off
			 Offset 1, 1

			 CGPROGRAM

			 #pragma vertex vert
			 #pragma fragment frag
			 #pragma multi_compile_shadowcaster
			 #pragma fragmentoption ARB_precision_hint_fastest

			 #include "UnityCG.cginc"

			 sampler2D _MainTex;

			 struct v2f
			 {
				 V2F_SHADOW_CASTER;
				 half2 uv:TEXCOORD1;
			 };

			 v2f vert(appdata_base v)
			 {
				 v2f o;
				 o.uv = v.texcoord;
				 TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
				 return o;
			 }

			 float4 frag(v2f i) : COLOR
			 {
				 fixed alpha = tex2D(_MainTex, i.uv).a;
				 clip(alpha - 0.5f);
				 SHADOW_CASTER_FRAGMENT(i)
			 }

			 ENDCG
		}
	}
	Fallback "Diffuse"
	Fallback "VertexLit"
}