// A pig changing type under a swap crystal (PigSwapMorph). _OldTex/_NewTex are off-screen captures of the old and
// new rig (cleared to transparent black, so their color is premultiplied). The quad cross-fades the forms by _Morph
// and replaces the body (opaque coverage, so not the faint ground shadow) with a crystal-colored fill, graded over
// an ellipse fitted to it (radius 1 = the body's bounding box): dark and _InnerOpacity up to 70% of the radius,
// ramping to the light color and _OuterOpacity (20% transparency) by 90%, light beyond; plus a lighter, more
// saturated border just outside the body. _ColorT moves every color from the "From" to the "To" crystal color;
// _Strength fades the whole tint.
Shader "ABH/Swap Crystal Pig Morph"
{
	Properties
	{
		_OldTex ("Old form capture", 2D) = "black" {}
		_NewTex ("New form capture", 2D) = "black" {}
		_MorphSolidity ("Mid-morph solidity", Range(0, 1)) = 0.8
		_Morph ("Old -> new form", Range(0, 1)) = 0
		_Strength ("Tint strength", Range(0, 1)) = 0
		_ColorT ("From -> to color", Range(0, 1)) = 0
		_DarkFrom ("Dark (from)", Color) = (0.52, 0.08, 0.08, 1)
		_LightFrom ("Light (from)", Color) = (0.85, 0.47, 0.47, 1)
		_BorderFrom ("Border (from)", Color) = (1, 0.6, 0.6, 1)
		_DarkTo ("Dark (to)", Color) = (0.08, 0.25, 0.52, 1)
		_LightTo ("Light (to)", Color) = (0.47, 0.63, 0.85, 1)
		_BorderTo ("Border (to)", Color) = (0.6, 0.76, 1, 1)
		_EllipseOld ("Old silhouette ellipse (center xy, radii zw; uv)", Vector) = (0.5, 0.5, 0.5, 0.5)
		_EllipseNew ("New silhouette ellipse (center xy, radii zw; uv)", Vector) = (0.5, 0.5, 0.5, 0.5)
		_InnerOpacity ("Fill opacity inside 70%", Range(0, 1)) = 0.65
		_OuterOpacity ("Fill opacity from 90%", Range(0, 1)) = 0.8
		_OutlineWidth ("Border width (texels)", Float) = 4
	}

	SubShader
	{
		LOD 100

		Tags
		{
			"Queue" = "Transparent"
			"IgnoreProjector" = "True"
			"RenderType" = "Transparent"
		}

		Cull Off
		Lighting Off
		ZWrite Off
		Fog { Mode Off }
		Blend One OneMinusSrcAlpha

		Pass
		{
			CGPROGRAM
				#pragma vertex vert
				#pragma fragment frag
				#pragma target 3.0

				#include "UnityCG.cginc"

				struct appdata_t
				{
					float4 vertex : POSITION;
					float2 texcoord : TEXCOORD0;
				};

				struct v2f
				{
					float4 vertex : SV_POSITION;
					float2 uv : TEXCOORD0;
				};

				sampler2D _OldTex;
				float4 _OldTex_TexelSize;
				sampler2D _NewTex;
				float _MorphSolidity;
				float _Morph;
				float _Strength;
				float _ColorT;
				fixed4 _DarkFrom;
				fixed4 _LightFrom;
				fixed4 _BorderFrom;
				fixed4 _DarkTo;
				fixed4 _LightTo;
				fixed4 _BorderTo;
				float4 _EllipseOld;
				float4 _EllipseNew;
				float _InnerOpacity;
				float _OuterOpacity;
				float _OutlineWidth;

				v2f vert (appdata_t v)
				{
					v2f o;
					o.vertex = UnityObjectToClipPos(v.vertex);
					o.uv = v.texcoord;
					return o;
				}

				// How far mid-morph coverage is pushed from the plain cross-fade toward the union of both forms, so the
				// shape stays solid instead of both forms turning see-through.
				float Solidity ()
				{
					return sin(_Morph * 3.14159265) * _MorphSolidity;
				}

				// Cross-faded pig: premultiplied color and coverage. Alpha-blending onto a cleared target squares a
				// single layer's alpha (a * a + 0), so the square root gives the coverage back.
				float4 Pig (float2 uv)
				{
					float4 o = tex2D(_OldTex, uv);
					float4 n = tex2D(_NewTex, uv);
					o.a = sqrt(o.a);
					n.a = sqrt(n.a);
					float4 fade = lerp(o, n, _Morph);
					float a = lerp(fade.a, max(o.a, n.a), Solidity());
					return float4(fade.rgb / max(fade.a, 0.0001) * a, a);
				}

				// Body coverage: the rigs' bodies are opaque, their ground shadows about a third covered.
				float Body (float2 uv)
				{
					float o = smoothstep(0.45, 0.75, sqrt(tex2D(_OldTex, uv).a));
					float n = smoothstep(0.45, 0.75, sqrt(tex2D(_NewTex, uv).a));
					return lerp(lerp(o, n, _Morph), max(o, n), Solidity());
				}

				fixed4 frag (v2f i) : SV_Target
				{
					float4 pig = Pig(i.uv);
					float body = Body(i.uv);

					float4 ellipse = lerp(_EllipseOld, _EllipseNew, _Morph);
					float d = length((i.uv - ellipse.xy) / max(ellipse.zw, 0.001));
					float k = smoothstep(0.7, 0.9, d);
					float3 dark = lerp(_DarkFrom.rgb, _DarkTo.rgb, _ColorT);
					float3 light = lerp(_LightFrom.rgb, _LightTo.rgb, _ColorT);
					float3 border = lerp(_BorderFrom.rgb, _BorderTo.rgb, _ColorT);
					float3 fill = lerp(dark, light, k);
					// The tint replaces the pig's body (its ground shadow stays): at full strength only the fill shows.
					float fillAlpha = lerp(_InnerOpacity, _OuterOpacity, k) * body * _Strength;
					float bodyShare = saturate(body / max(pig.a, 0.0001));
					float keep = 1.0 - _Strength * bodyShare;
					float3 rgb = pig.rgb * keep + fill * fillAlpha;
					float a = pig.a * keep + fillAlpha;

					// Border: body coverage within _OutlineWidth texels that this pixel itself doesn't have.
					[branch]
					if (_Strength > 0.001)
					{
						float2 texel = _OldTex_TexelSize.xy * _OutlineWidth;
						float ring = 0.0;
						for (int s = 0; s < 8; s++)
						{
							float angle = s * 0.7853982;
							float2 dir = float2(cos(angle), sin(angle)) * texel;
							ring = max(ring, Body(i.uv + dir));
							ring = max(ring, Body(i.uv + dir * 0.5));
						}
						float edge = saturate(ring - body) * _Strength;
						rgb = rgb * (1.0 - edge) + border * edge;
						a = a * (1.0 - edge) + edge;
					}
					return float4(rgb, a);
				}
			ENDCG
		}
	}
}
