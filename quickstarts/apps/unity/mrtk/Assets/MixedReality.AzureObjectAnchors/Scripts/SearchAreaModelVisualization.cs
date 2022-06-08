// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
using UnityEngine;

namespace Microsoft.Azure.ObjectAnchors.Unity.Sample
{
    /// <summary>
    /// Renders the mesh in the trackable object data.  Used for rendering the model in the 
    /// search box.
    /// </summary>
    public class SearchAreaModelVisualization : MonoBehaviour
    {
        MeshRenderer meshRenderer;
        MeshFilter meshFilter;

        TrackableObjectData currentVisualizationData = null;
        
        private void Start()
        {
            meshRenderer = GetComponent<MeshRenderer>();
            meshFilter = GetComponent<MeshFilter>();
        }

        private void Update()
        {
            // force world scale to one, but otherwise obey the parent transform.
            transform.localScale = new Vector3(1f / transform.parent.lossyScale.x, 1f / transform.parent.lossyScale.y, 1f / transform.parent.lossyScale.z);
            if (currentVisualizationData != null)
            {
                ObjectAnchorsBoundingBox? bb = currentVisualizationData.logicalBoundingBox;
                if (bb.HasValue)
                {
                    transform.localPosition = bb.Value.Center; ;
                    transform.localRotation = bb.Value.Orientation;
                }
            }
        }

        public void SetTrackableObjectData(TrackableObjectData trackableObjectData)
        {
            currentVisualizationData = trackableObjectData;
            meshRenderer.enabled = currentVisualizationData?.ModelMesh != null;
            meshFilter.sharedMesh = currentVisualizationData?.ModelMesh;
        }
    }
}