// Swap crystal prop (SwapCrystalView): blends the red-primary and blue-primary crystal textures, adds a white
// flash and a moving glint, and multiplies by _Color (fade and glow tint). Also used for its podium and glow.
Shader "ABH/Swap Crystal"
{
	Properties
	{
		_MainTex ("Red primary (RGBA)", 2D) = "black" {}
		_AltTex ("Blue primary (RGBA)", 2D) = "black" {}
		_Blend ("Red -> blue", Range(0, 1)) = 0
		_Flash ("Flash", Range(0, 1)) = 0
		_GlintStrength ("Glint strength", Range(0, 1)) = 0.35
		_GlintSpeed ("Glint speed", Float) = 0.35
		_Color ("Tint", Color) = (1, 1, 1, 1)
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
		Offset -1, -1
		Blend SrcAlpha OneMinusSrcAlpha

		Pass
		{
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
					half2 texcoord : TEXCOORD0;
					fixed4 color : COLOR;
				};

				sampler2D _MainTex;
				float4 _MainTex_ST;
				sampler2D _AltTex;
				float _Blend;
				float _Flash;
				float _GlintStrength;
				float _GlintSpeed;
				fixed4 _Color;

				v2f vert (appdata_t v)
				{
					v2f o;
					o.vertex = UnityObjectToClipPos(v.vertex);
					o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
					o.color = v.color * _Color;
					return o;
				}

				fixed4 frag (v2f i) : SV_Target
				{
					fixed4 col = lerp(tex2D(_MainTex, i.texcoord), tex2D(_AltTex, i.texcoord), _Blend);
					// A narrow diagonal band of light sweeping over the crystal now and then.
					float band = frac(i.texcoord.y - 0.5 * i.texcoord.x - _Time.y * _GlintSpeed);
					float glint = saturate(1.0 - abs(band - 0.5) * 14.0) * _GlintStrength;
					col.rgb = col.rgb + (glint + _Flash) * (1.0 - col.rgb * 0.5);
					return col * i.color;
				}
			ENDCG
		}
	}
}
