// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

Shader "DepthOnly" {

    Subshader
    {
        Cull Back ZWrite On ZTest LEqual Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct v2f
            {
                fixed4 position : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.position = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag() : COLOR
            {
                return fixed4(0,0,0,0);
            }
        ENDCG
        }
    }
}