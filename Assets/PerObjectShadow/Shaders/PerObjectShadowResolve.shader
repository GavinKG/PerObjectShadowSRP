// Gavin_KG presents
// single shader file for per object shadow resolve
// should be used on shadow frustum mesh

// todo: optimize precision for mobile (use real)

Shader "Universal Render Pipeline/PerObjectShadowResolve"
{
	Properties
	{
		[MainColor] _ShadowColor("Shadow Color", Color) = (0, 0, 0, 1)
		[Toggle] _SoftShadow("Soft Shadow", Float) = 1.0
		_ShadowBias("Shadow Bias", Float) = 0.02
		[Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend("Src Blend", Float) = 10 // ScrAlpha
		[Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Dst Blend", Float) = 5 // OneMinusSrcAlpha
		[Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 0

	}

	SubShader
	{

		Tags {"IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" "ShaderModel" = "4.5"}
		LOD 100

		Blend[_SrcBlend][_DstBlend]
		ZWrite Off
		Cull[_Cull]

		/* Stencil way of preventing multiple blend artefact. should use discard (alpha clip) instead of alpha blend.
		 * Do not use this solution when applying PCF.
		Stencil
		{
			Ref 0
			Comp Equal
			Pass IncrSat
		}
		*/

		Pass
		{
			Name "Resolve"

			HLSLPROGRAM
			#pragma target 3.5 // for SampleCmp & dynamic branch

			#pragma vertex ResolvePassVertex
			#pragma fragment ResolvePassFragment
			#pragma shader_feature_local_fragment _SOFTSHADOW_ON
			#pragma multi_compile_instancing

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"

			struct Attributes
			{
				float3 positionOS : POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionHCS : SV_POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			UNITY_INSTANCING_BUFFER_START(UnityPerMaterial)
				UNITY_DEFINE_INSTANCED_PROP(float4x4, _WorldToUVMatrix)
				UNITY_DEFINE_INSTANCED_PROP(float4, _SliceUVOffsetExtend) // todo: use it!
			UNITY_INSTANCING_BUFFER_END(UnityPerMaterial)

			// todo: find a way to support both srp batcher and gpu instancing

			// set in material inspector.
			float4 _ShadowColor; 
			float _ShadowBias;

			float4 _PerObjectShadowAtlasTexelSize; // xy: texel size, zw: not used.

			TEXTURE2D_SHADOW(_PerObjectShadowAtlas);
			SAMPLER_CMP(sampler_PerObjectShadowAtlas);

			TEXTURE2D_X_FLOAT(_CameraDepthTexture);
			SAMPLER(sampler_CameraDepthTexture);

			float SampleShadow(float3 shadowCoords) {

				// uv offset (without texel size) for 4-tap pcf filtering
				const float3 shadowOffset0 = float3(-0.5, -0.5, 0);
				const float3 shadowOffset1 = float3(0.5, -0.5, 0);
				const float3 shadowOffset2 = float3(-0.5, 0.5, 0);
				const float3 shadowOffset3 = float3(0.5, 0.5, 0);

				float shadow = 0;
#if _SOFTSHADOW_ON
				// 4-tap hardware comparison
				float4 atten4;
				atten4.x = SAMPLE_TEXTURE2D_SHADOW(_PerObjectShadowAtlas, sampler_PerObjectShadowAtlas, shadowCoords + shadowOffset0 * _PerObjectShadowAtlasTexelSize.xyz);
				atten4.y = SAMPLE_TEXTURE2D_SHADOW(_PerObjectShadowAtlas, sampler_PerObjectShadowAtlas, shadowCoords + shadowOffset1 * _PerObjectShadowAtlasTexelSize.xyz);
				atten4.z = SAMPLE_TEXTURE2D_SHADOW(_PerObjectShadowAtlas, sampler_PerObjectShadowAtlas, shadowCoords + shadowOffset2 * _PerObjectShadowAtlasTexelSize.xyz);
				atten4.w = SAMPLE_TEXTURE2D_SHADOW(_PerObjectShadowAtlas, sampler_PerObjectShadowAtlas, shadowCoords + shadowOffset3 * _PerObjectShadowAtlasTexelSize.xyz);

				shadow = dot(atten4, 0.25);
#else
				// 1-tap hardware comparison
				shadow =  SAMPLE_TEXTURE2D_SHADOW(_PerObjectShadowAtlas, sampler_PerObjectShadowAtlas, shadowCoords);
#endif
				shadow = 1 - shadow;
				return shadow;
			}

			Varyings ResolvePassVertex (Attributes input) 
			{
				Varyings output = (Varyings)0; // todo: should we use ZERO_INITIALIZE?

				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);

				float3 positionWS = TransformObjectToWorld(input.positionOS);
				output.positionHCS = TransformWorldToHClip(positionWS);

				return output;
			}

			half4 ResolvePassFragment(Varyings input) : SV_TARGET
			{
				UNITY_SETUP_INSTANCE_ID(input);

				// screen-space uv
				float2 uv = input.positionHCS.xy / _ScaledScreenParams.xy;

				// Important: CopyDepthPass or pre-Z should be applied before resolve (this) pass to make _CameraDepthTexture ready
				// from DeclareDepthTexture.hlsl
				float z = SAMPLE_TEXTURE2D(_CameraDepthTexture, sampler_CameraDepthTexture, uv).r;

#if !UNITY_REVERSED_Z
				// Adjust Z to match NDC for OpenGL ([-1, 1]), since reversed-z optimization is off only on OpenGL (see HLSLSupport.cginc)...
				z = lerp(UNITY_NEAR_CLIP_VALUE, 1, z);
#endif

				// ComputeWorldSpacePosition from Common.hlsl
				float3 sceneWorldPos = ComputeWorldSpacePosition(uv, z, UNITY_MATRIX_I_VP);

				float3 shadowCoords = mul(UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _WorldToUVMatrix), float4(sceneWorldPos, 1.0)).xyz;

				float4 wrap = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _SliceUVOffsetExtend);

#if UNITY_UV_STARTS_AT_TOP
				// shadowCoords.y = 1 - shadowCoords.y; // error: slice not occupying whole v-axis
				shadowCoords.y = wrap.y + wrap.w - shadowCoords.y;
#endif

				// discard sample outside range
				// todo: performance? branch / pre-z
				
				if (shadowCoords.x < wrap.x || shadowCoords.x > wrap.x + wrap.z || shadowCoords.y < wrap.y || shadowCoords.y > wrap.y + wrap.w) {
					return half4(1, 1, 1, 0);
				}

				// cmp compensation
				shadowCoords.z = max(shadowCoords.z, 0.00001) + _ShadowBias; // for DX, todo: test opengl, remove hard-coded bias

				float shadow = SampleShadow(shadowCoords);

				// float4 _ShadowColor = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _ShadowColor);
				return half4(_ShadowColor.rgb, shadow * _ShadowColor.a);
			}

			ENDHLSL

		}
	}
}