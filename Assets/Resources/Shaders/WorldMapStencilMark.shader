// World map stencil mark only: draws nothing visible, but sets stencil bit 64 where the texture (times vertex alpha) is
// visible and nothing opaque is in front (ZTest LEqual against the opaque pass). WorldMapOverlayMasks draws every
// character and node with it from a command buffer before the transparent pass, so their own rendering stays untouched.
Shader "WorldMap/Stencil Mark"
{
	Properties
	{
		_MainTex ("Base (RGB) Trans (A)", 2D) = "white" {}
		_MaskCutoff ("Mask alpha cutoff", Float) = 0.3
	}

	SubShader
	{
		Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" }
		Pass
		{
			Cull Off
			Lighting Off
			ZWrite Off
			ZTest LEqual
			ColorMask 0
			Stencil
			{
				Ref 64
				ReadMask 64
				WriteMask 64
				Comp Always
				Pass Replace
			}
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"

			struct appdata_t
			{
				float4 vertex : POSITION;
				float2 texcoord : TEXCOORD0;
				fixed4 color : COLOR;
			};

			struct v2f
			{
				float4 vertex : SV_POSITION;
				float2 texcoord : TEXCOORD0;
				fixed alpha : TEXCOORD1;
			};

			sampler2D _MainTex;
			float4 _MainTex_ST;
			float _MaskCutoff;

			v2f vert (appdata_t v)
			{
				v2f o;
				o.vertex = UnityObjectToClipPos(v.vertex);
				o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
				o.alpha = v.color.a;
				return o;
			}

			fixed4 frag (v2f i) : SV_Target
			{
				clip(tex2D(_MainTex, i.texcoord).a * i.alpha - _MaskCutoff);
				return 0;
			}
			ENDCG
		}
	}
}
