// ScytheBells.cs. Bells: like Sprites/Default (vertex colour x texture, alpha blended), and marks the stencil where they draw.
Shader "Hidden/ScytheBells/Bells"
{
	Properties
	{
		_MainTex ("Texture", 2D) = "white" {}
	}
	SubShader
	{
		Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" }
		Cull Off
		ZWrite Off
		Lighting Off
		Blend SrcAlpha OneMinusSrcAlpha
		Stencil { Ref 91 Comp Always Pass Replace }
		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"

			sampler2D _MainTex;

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

			v2f vert (appdata v)
			{
				v2f o;
				o.pos = UnityObjectToClipPos(v.vertex);
				o.uv = v.uv;
				o.color = v.color;
				return o;
			}

			fixed4 frag (v2f i) : SV_Target
			{
				fixed4 c = tex2D(_MainTex, i.uv) * i.color;
				clip(c.a - 0.004); // only the bells' own pixels mark the stencil
				return c;
			}
			ENDCG
		}
	}
}
