Shader "ArcaneWaters/RiverFlow" {
Properties {
 _Normal ("Ocean normal",2D)="bump"{}
 _NormalFine ("Ocean fine normal",2D)="bump"{}
 _FoamTex ("Ocean foam",2D)="black"{}
 _ColorTop ("Ocean shallow color",Color)=(.315,.524,.361,1)
 _ColorBottom ("Ocean deep color",Color)=(.098,.196,.169,1)
 _RiverTime ("Synchronized time",Float)=0
 _SeaLevel ("Ocean level",Float)=30
 _NormalPower ("Ripple power",Float)=.65
 _UseGlobalWind ("Native compatibility",Float)=0
}
SubShader {
 Tags {"Queue"="Transparent+50" "RenderType"="Transparent"}
 GrabPass {"_ArcaneRiverScene"}
 Pass {
 Tags {"LightMode"="ForwardBase"}
 // Same hull exclusion as Valheim's Custom/Water. Custom/WaterMask writes 2.
 Stencil { Ref 2 ReadMask 255 WriteMask 0 Comp NotEqual Pass Keep }
 Blend SrcAlpha OneMinusSrcAlpha
 ZWrite Off
 // Opaque occlusion is handled explicitly below. Native ocean depth must not
 // cut this crossfade into intersecting triangles as its waves move.
 ZTest Always
 Cull Off
 CGPROGRAM
 #pragma target 3.0
 #pragma vertex vert
 #pragma fragment frag
 #pragma multi_compile_fog
 #include "UnityCG.cginc"
 #include "Lighting.cginc"
 #include "UnityStandardUtils.cginc"
 sampler2D _Normal,_NormalFine,_FoamTex,_ArcaneRiverScene;
 UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
 float4 _ArcaneRiverScene_TexelSize;
 float4 _ColorTop,_ColorBottom;
 float _RiverTime,_SeaLevel,_NormalPower;
 struct appdata {float4 vertex:POSITION;float3 normal:NORMAL;float2 flow:TEXCOORD1;float2 shore:TEXCOORD2;float2 contact:TEXCOORD3;float4 appearance:COLOR;};
 struct v2f {float4 pos:SV_POSITION;float3 world:TEXCOORD0;float3 normal:TEXCOORD1;float2 flow:TEXCOORD2;float4 grab:TEXCOORD3;float2 shore:TEXCOORD4;UNITY_FOG_COORDS(5) float2 contact:TEXCOORD6;float4 look:TEXCOORD7;float4 screen:TEXCOORD8;};
 float waveShape(float2 p){float a=p.x*1.7+p.y*.9,b=-p.x*1.3+p.y*2.4;return sin(a-.45*cos(a))*.68+sin(b-.3*cos(b))*.32;}
 float wave(float2 p,float2 flow){float a=frac(_RiverTime/6),b=frac(_RiverTime/6+.5),mix=abs(a*2-1);return lerp(waveShape(p-flow*a*6),waveShape(p-flow*b*6),mix);}
 v2f vert(appdata v){
  v2f o;float3 w=mul(unity_ObjectToWorld,v.vertex).xyz;
  float mean=w.y;float amplitude=v.appearance.b;
  float h=wave(w.xz,v.flow)*amplitude;
  float dx=(wave(w.xz+float2(.04,0),v.flow)-wave(w.xz-float2(.04,0),v.flow))*amplitude/ .08;
  float dz=(wave(w.xz+float2(0,.04),v.flow)-wave(w.xz-float2(0,.04),v.flow))*amplitude/ .08;
  w.y+=h;o.world=w;o.pos=mul(UNITY_MATRIX_VP,float4(w,1));
  o.normal=normalize(UnityObjectToWorldNormal(v.normal)+float3(-dx,0,-dz));
  o.flow=v.flow;o.shore=v.shore;o.contact=v.contact;o.look=float4(v.appearance.rg,mean,-mul(UNITY_MATRIX_V,float4(w,1)).z);o.grab=ComputeGrabScreenPos(o.pos);o.screen=ComputeScreenPos(o.pos);
  UNITY_TRANSFER_FOG(o,o.pos);return o;
 }
 float3 ripple(float2 p){float3 a=UnpackNormal(tex2D(_Normal,p*.16)),b=UnpackNormal(tex2D(_NormalFine,p*.55));return float3(a.xy+b.xy*.45,1);}
 fixed4 frag(v2f i):SV_Target {
  float p=frac(_RiverTime/6),q=frac(_RiverTime/6+.5),blend=abs(p*2-1);
  float2 a=i.world.xz-i.flow*p*6,b=i.world.xz-i.flow*q*6;
  float3 rip=lerp(ripple(a),ripple(b),blend);
  float3 baseN=normalize(i.normal),t=normalize(float3(baseN.y,-baseN.x,0)),side=normalize(cross(t,baseN));
  float3 n=normalize(baseN+(rip.x*t+rip.y*side)*_NormalPower);
  float3 view=normalize(_WorldSpaceCameraPos-i.world);if(dot(n,view)<0)n=-n;
  float depth=max(.02,i.contact.x),edge=smoothstep(0,.12,i.shore.x);
  float2 depthUV=i.screen.xy/i.screen.w;
  float sceneDepth=LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture,depthUV));
  float clearance=sceneDepth-i.look.w;
  clip(clearance-.005);
  edge*=smoothstep(.005,.22,clearance);
  float2 uv=i.grab.xy/i.grab.w;
  float2 distortion=n.xz*.012*saturate(depth)*edge;
  // Never refract foreground grass, banks or boats across the water boundary.
  float refractedDepth=LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture,depthUV+distortion));
  distortion*=step(i.look.w+.05,refractedDepth);
  float3 scene=tex2D(_ArcaneRiverScene,uv+distortion).rgb;
  float3 ambient=max(.08,ShadeSH9(float4(n,1)));
  float3 reflected=DecodeHDR(UNITY_SAMPLE_TEXCUBE_LOD(unity_SpecCube0,reflect(-view,n),1),unity_SpecCube0_HDR);
  reflected=max(reflected,ambient*float3(.45,.65,.8));
  float fresnel=.035+.965*pow(1-saturate(dot(n,view)),5);
  float3 tint=lerp(_ColorTop.rgb,_ColorBottom.rgb,saturate(depth/3));
  float transmission=exp(-depth*.55);
  float3 color=lerp(tint*ambient,scene*float3(.8,.96,1),transmission);
  color=lerp(color,reflected,fresnel);
  float3 light=normalize(_WorldSpaceLightPos0.xyz);
  float highlight=pow(saturate(dot(n,normalize(view+light))),120)*2;
  color+=highlight*_LightColor0.rgb;
  // Small broken crest highlights within the water, never separate foam sprites.
  float textureFoam=lerp(tex2D(_FoamTex,a*.45).r,tex2D(_FoamTex,b*.45).r,blend);
  float crest=smoothstep(.48,.9,wave(i.world.xz,i.flow));
  float foam=smoothstep(.55,.85,textureFoam)*crest*i.look.y*.16;
  color=lerp(color,ambient*.8+_LightColor0.rgb*.15,foam);
  fixed4 result=fixed4(color,edge);
  // The native receiving pond/ocean owns the final approach.
  result.a*=smoothstep(0,1.5,i.contact.y)*(1-i.shore.y);
  // Crossfade only where the riverbed is below sea level, so native water
  // actually exists beneath this fragment. Use mean height, not wave phase.
  float receiving=smoothstep(0,.3,_SeaLevel-(i.look.z-i.contact.x));
  result.a*=lerp(1,smoothstep(0,1.25,i.look.z-_SeaLevel),receiving);
  UNITY_APPLY_FOG(i.fogCoord,result);return result;
 }
 ENDCG
 }
}
}
