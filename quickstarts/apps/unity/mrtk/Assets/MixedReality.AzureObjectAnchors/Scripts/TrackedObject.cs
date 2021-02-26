// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
#if UNITY_WSA
using System.Collections.Generic;
using UnityEngine;

namespace Microsoft.Azure.ObjectAnchors.Unity.Sample
{
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class TrackedObject : MonoBehaviour
    {
        // The object geometry isn't necessarily centered in the objects bounding box
        // As a consequence we must take care that other visualziations intended to be
        // placed relatively to a detetected object can be placed as expected
        public GameObject LogicalCenter;

        public TrackedObjectData TrackedObjectState { get; private set; } = new TrackedObjectData();
        private bool _trackedObjectDataChanged = false;
        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private TextMesh _debugText;
        private Material _renderMaterial;
        private int _wireFrameColorParameter;

        public Material PointCloudMaterial;
        public Material MeshMaterial;

        private static readonly Dictionary<ObjectInstanceTrackingMode, Color> trackingModeToColor = new Dictionary<ObjectInstanceTrackingMode, Color>()
        {
            {ObjectInstanceTrackingMode.HighLatencyAccuratePosition, new Color(0,1,0,0.5f) },
            {ObjectInstanceTrackingMode.LowLatencyCoarsePosition, new Color(0,1,1,0.5f) },
            {ObjectInstanceTrackingMode.Paused, new Color(1,1,1,0.5f) }
        };

        private void Start()
        {
            Debug.Log("Tracked object started");
            _meshFilter = GetComponent<MeshFilter>();
            _meshRenderer = GetComponent<MeshRenderer>();
            _debugText = GetComponentInChildren<TextMesh>();
            _renderMaterial = _meshRenderer.material;
            _wireFrameColorParameter = Shader.PropertyToID("_WireColor");
        }

        private void Update()
        {
            if (_trackedObjectDataChanged)
            {
                UpdateMesh();
                UpdateDebugText();
                _trackedObjectDataChanged = false;
            }
        }

        private void OnDestroy()
        {
            Destroy(_renderMaterial);
        }

        private void UpdateDebugText()
        {
            if (_debugText != null)
            {

                _debugText.text =
                    $"id: {TrackedObjectState.InstanceId}\n" +
                    $"Model: {TrackedObjectState.ModelId}\n" +
                    $"Name: {TrackedObjectState.ModelFileName}\n" +
                    $"Located: {TrackedObjectState.Location.HasValue}\n" +
                    $"Updated: {TrackedObjectState.LastUpdatedTime.ToString("hh:mm:ss.fff")}\n" +
                    $"Cov: {TrackedObjectState.SurfaceCoverage}\n" +
                    $"Scale: {TrackedObjectState.Scale.x} {TrackedObjectState.Scale.y} {TrackedObjectState.Scale.z}\n" +
                    $"Tracking Mode: {TrackedObjectState.TrackingMode}";

                ObjectAnchorsBoundingBox? bb = TrackedObjectState.BaseLogicalBoundingBox;
                if (bb.HasValue)
                {
                    _debugText.gameObject.transform.position = LogicalCenter.transform.position + new Vector3(0, bb.Value.Extents.z * 0.5f + 0.2f, 0);
                }
            }
        }

        private void UpdateMesh()
        {
            _meshFilter.sharedMesh = TrackedObjectState.ModelMesh;
            if (TrackedObjectState.Location.HasValue)
            {
                transform.localScale = TrackedObjectState.Scale;
                transform.position = TrackedObjectState.Location.Value.Position;
                transform.rotation = TrackedObjectState.Location.Value.Orientation;
            }

            switch (_meshFilter.sharedMesh.GetTopology(0))
            {
                case MeshTopology.Triangles:
                    _meshRenderer.material = MeshMaterial;
                    _renderMaterial = _meshRenderer.material;
                    _renderMaterial.SetColor(_wireFrameColorParameter, trackingModeToColor[TrackedObjectState.TrackingMode]);
                    break;
                default: // point cloud material is the default shader so it should generally work.
                    _meshRenderer.material = PointCloudMaterial;
                    _renderMaterial = _meshRenderer.material;
                    _renderMaterial.color = trackingModeToColor[TrackedObjectState.TrackingMode];
                    break;
            }

            ObjectAnchorsBoundingBox? bb = TrackedObjectState.BaseLogicalBoundingBox;
            if (bb.HasValue)
            {
                LogicalCenter.transform.localPosition = bb.Value.Center;
                LogicalCenter.transform.localRotation = bb.Value.Orientation;
            }
        }

        public void UpdateTrackedObjectData(IObjectAnchorsServiceEventArgs data)
        {
            Debug.Log($"Updating tracking data for {data.InstanceId} model {data.ModelId} mode {data.TrackingMode}");
            TrackedObjectState.UpdateTrackingData(data);
            _trackedObjectDataChanged = true;
        }
    }
}
#endif // UNITY_WSA