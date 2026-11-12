// Fog Machine look for the targeting HUD of a combatant cursed by King DJ (CharacterControlHUD): the texture's shape is
// filled with _CoreColor, and a thin outline (pixels within _OutlineTexels of the shape's edge) takes the vertex colour,
// which the HUD animates as a rainbow (SoundWaveVisual.ForcedMoveRainbow), like the sound wave's rainbow edges.
// The HUD builds its ring and line from repeated pieces of CharacterControlHUD.png (four quarter disks / arcs make a
// circle, ten bars end to end make the line), so the cut edges of those pieces are seams, not silhouette: samples
// across a seam count as inside the shape, and only the real outline gets the rainbow. The seams, in texels from the
// texture's top-left: the quarter shapes (top-left 128 x 96 area) end at x = 94 and y = 94; the white bar (x < 48 below
// y = 96) ends at x = 0 and 32; the gray wall bar (x >= 48 below y = 96) ends at x = 64 and 96.
// Kept in Resources so builds include it (loaded with Shader.Find).
Shader "Hidden/ABH/FogMachineOutline"
{
	Properties
	{
		_MainTex ("Texture", 2D) = "white" {}
		_CoreColor ("Core Color", Color) = (0.1059, 0.1333, 0.2275, 1)
		_Color ("Tint", Color) = (1, 1, 1, 1)
		_OutlineTexels ("Outline Width (texels)", Float) = 2.5
	}
	SubShader
	{
		Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" }
		Cull Off
		Lighting Off
		ZWrite Off
		Blend SrcAlpha OneMinusSrcAlpha
		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"

			sampler2D _MainTex;
			float4 _MainTex_ST;
			float4 _MainTex_TexelSize;
			fixed4 _CoreColor;
			fixed4 _Color;
			float _OutlineTexels;

			struct appdata
			{
				float4 vertex : POSITION;
				float2 uv : TEXCOORD0;
				fixed4 color : COLOR;
			};

			struct v2f
			{
				float4 pos : SV_POSITION;
				float2 uv : TEXCOORD0;
				fixed4 color : COLOR;
			};

			// Texel position from the top-left of the texture.
			float2 Texel(float2 uv)
			{
				return float2(uv.x, 1.0 - uv.y) * _MainTex_TexelSize.zw;
			}

			// Alpha at uv for a pixel at base (texels); 1 across a seam of the piece base lies in.
			fixed SeamAlpha(float2 base, float2 uv)
			{
				float2 t = Texel(uv);
				if (base.y < 96.0)
				{
					if (t.x >= 94.0 || t.y >= 94.0)
					{
						return 1;
					}
				}
				else if (base.x < 48.0)
				{
					if (t.x < 0.0 || t.x >= 32.0)
					{
						return 1;
					}
				}
				else if (t.x < 64.0 || t.x >= 96.0)
				{
					return 1;
				}
				return tex2D(_MainTex, uv).a;
			}

			v2f vert(appdata v)
			{
				v2f o;
				o.pos = UnityObjectToClipPos(v.vertex);
				o.uv = TRANSFORM_TEX(v.uv, _MainTex);
				o.color = v.color;
				return o;
			}

			fixed4 frag(v2f i) : SV_Target
			{
				fixed alpha = tex2D(_MainTex, i.uv).a;
				float2 base = Texel(i.uv);
				// Smallest alpha in a ring of samples around the pixel: low means the shape's edge is near.
				float2 d = _MainTex_TexelSize.xy * _OutlineTexels;
				float2 e = d * 0.7071;
				fixed nearest = alpha;
				nearest = min(nearest, SeamAlpha(base, i.uv + float2(d.x, 0)));
				nearest = min(nearest, SeamAlpha(base, i.uv - float2(d.x, 0)));
				nearest = min(nearest, SeamAlpha(base, i.uv + float2(0, d.y)));
				nearest = min(nearest, SeamAlpha(base, i.uv - float2(0, d.y)));
				nearest = min(nearest, SeamAlpha(base, i.uv + e));
				nearest = min(nearest, SeamAlpha(base, i.uv - e));
				nearest = min(nearest, SeamAlpha(base, i.uv + float2(e.x, -e.y)));
				nearest = min(nearest, SeamAlpha(base, i.uv + float2(-e.x, e.y)));
				fixed outline = step(nearest, 0.5);
				fixed4 color = lerp(_CoreColor, fixed4(i.color.rgb, 1), outline);
				color.a = alpha * _Color.a;
				return color;
			}
			ENDCG
		}
	}
}
