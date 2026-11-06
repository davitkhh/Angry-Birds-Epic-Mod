// World map overlay: Unlit/Transparent Colored (texture * vertex colour) drawn after the scenery and over it (ZTest
// Always), except where a character or node marked the stencil (bit 64, WorldMap/Masking Transparent). Used for the
// next-level marker's stars, so they show over the zone clouding and foreground props but under characters and nodes.
Shader "WorldMap/Masked Overlay Colored"
{
	Properties
	{
		_MainTex ("Base (RGB), Alpha (A)", 2D) = "black" {}
	}

	SubShader
	{
		Tags { "Queue" = "Transparent+100" "IgnoreProjector" = "True" "RenderType" = "Transparent" }
		LOD 100
		Cull Off
		Lighting Off
		ZWrite Off
		ZTest Always
		Blend SrcAlpha OneMinusSrcAlpha

		Pass
		{
			Stencil
			{
				Ref 64
				ReadMask 64
				Comp NotEqual
				Pass Keep
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
				fixed4 color : COLOR;
			};

			sampler2D _MainTex;
			float4 _MainTex_ST;

			v2f vert (appdata_t v)
			{
				v2f o;
				o.vertex = UnityObjectToClipPos(v.vertex);
				o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
				o.color = v.color;
				return o;
			}

			fixed4 frag (v2f i) : SV_Target
			{
				return tex2D(_MainTex, i.texcoord) * i.color;
			}
			ENDCG
		}
	}
}
