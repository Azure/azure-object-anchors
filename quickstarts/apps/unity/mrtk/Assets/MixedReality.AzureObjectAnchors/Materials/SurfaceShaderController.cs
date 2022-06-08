// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
#if UNITY_WSA || UNITY_STANDALONE_WIN
using UnityEngine;

namespace Microsoft.Azure.ObjectAnchors.Unity.Sample
{
    /// <summary>
    /// Controls parameters for our spatial mapping shader
    /// </summary>
    public class SurfaceShaderController : MonoBehaviour
    {
        private static SurfaceShaderController _Instance;
        public static SurfaceShaderController Instance
        {
            get
            {
                if (_Instance == null)
                {
                    _Instance = FindObjectOfType<SurfaceShaderController>();
                }
                return _Instance;
            }
        }

        private struct ShaderProperty
        {
            public int PropertyId { get; set; }
            public ShaderProperty(string propertyName)
            {
                PropertyId = Shader.PropertyToID(propertyName);
                Debug.Log($"PID {PropertyId}");
            }
        }

        ShaderProperty ShaderParamsProperty;
        ShaderProperty SearchCenterProperty;
        ShaderProperty SearchExtentsProperty;

        // x = Frequency of solid lines in the shader
        // y = 0 no scan animation, > 0 scan animation.
        // z = thickness of solid line in the shader
        Vector4 shaderParams = new Vector4(0.125f, 1, 0.001f, 0);
        private ObjectTracker objectTracker;
        private SearchAreaController searchAreaController;

        void Start()
        {
            ShaderParamsProperty = new ShaderProperty("_ShaderParams");
            SearchCenterProperty = new ShaderProperty("_SearchCenter");
            SearchExtentsProperty = new ShaderProperty("_SearchExtents");
            objectTracker = FindObjectOfType<ObjectTracker>();
            searchAreaController = FindObjectOfType<SearchAreaController>();
        }

        void Update()
        {
            shaderParams.y = objectTracker.QueryActive ? 1 : 0;
#if UNITY_EDITOR
            shaderParams.y = 1;
            Shader.SetGlobalVector(SearchCenterProperty.PropertyId, new Vector4(0, 0, 1, 1));
            Shader.SetGlobalVector(SearchExtentsProperty.PropertyId, new Vector4(2, 2, 2, 1));
#endif
            Shader.SetGlobalVector(ShaderParamsProperty.PropertyId, shaderParams);

            
            if (objectTracker.QueryActive)
            {
                // If we are actively querying use the active query search bounds 
                Shader.SetGlobalVector(SearchCenterProperty.PropertyId, objectTracker.ActiveQueries.Item1.Center);
                Shader.SetGlobalVector(SearchExtentsProperty.PropertyId, objectTracker.ActiveQueries.Item1.Orientation * objectTracker.ActiveQueries.Item1.Extents * 0.5f);
            }
            else if (searchAreaController != null)
            {
                // If we are not actively searching, use the current search bounds.
                Shader.SetGlobalVector(SearchCenterProperty.PropertyId, searchAreaController.SearchArea.Center);
                Shader.SetGlobalVector(SearchExtentsProperty.PropertyId, searchAreaController.SearchArea.Orientation * searchAreaController.SearchArea.Extents * 0.5f);
            }
        }
    }
}
#endif // UNITY_WSA || UNITY_STANDALONE_WIN