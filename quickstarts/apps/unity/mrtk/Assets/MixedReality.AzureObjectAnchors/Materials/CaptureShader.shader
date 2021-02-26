// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
Shader "CaptureSample/CaptureShader"
{
    Properties
    {
       _Color("Main Color", Color) = (0,1,1,1)
    }
        SubShader
    {
        Cull Back ZWrite On ZTest LEqual

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 normal: NORMAL0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float3 normal: TEXCOORD0;
                float4 vertex : SV_POSITION;
                float3 worldPos: TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v); 
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld,v.vertex);
                return o;
            }

            fixed4 _Color;
            // x = Frequency of solid lines in the shader.
            // y = if <= 0, no query active. if > 0, query active
            // z = Line Width
            fixed4 _ShaderParams = fixed4(0.5f, 0, 0.005f, 0);
            fixed4 _SearchCenter = fixed4(0, 0, 0, 1);
            fixed4 _SearchExtents = fixed4(1, 1, 1, 1);

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                
                fixed4 col = _Color;
                float DistanceRepeat = _ShaderParams.x;
                float LineThickness = _ShaderParams.z;
                float scanWorldOffset = 0;

                // Todo: proper bounds check
                if (length(i.worldPos - _SearchCenter) > length(_SearchExtents))
                {
                    // if outside the search area, remove the ouput
                    return fixed4(0, 0, 0, 0);
                }

                // if scanning is active, we animate the position of the bright lines
                if (_ShaderParams.y > 0)
                {
                    scanWorldOffset = (_SinTime.x+1) * 0.5f;
                }
                
                // check to see if this is a point where we want a solid line
                float modx = (i.worldPos.x + scanWorldOffset) - DistanceRepeat * floor((i.worldPos.x + scanWorldOffset) / DistanceRepeat);
                float mody = i.worldPos.y - DistanceRepeat * floor(i.worldPos.y / DistanceRepeat);
                float modz = (i.worldPos.z + scanWorldOffset) - DistanceRepeat * floor((i.worldPos.z + scanWorldOffset) / DistanceRepeat);
                if (!(modx < LineThickness || mody < LineThickness || modz < LineThickness))
                {
                    float3 norm = normalize(i.normal);
                    float dotprodLightToNormal = abs(dot(_WorldSpaceLightPos0, norm));
                    col = col * dotprodLightToNormal * 0.375f;
                }

                col.a = 1;
                return col;
            }
            ENDCG
        }
    }
}
