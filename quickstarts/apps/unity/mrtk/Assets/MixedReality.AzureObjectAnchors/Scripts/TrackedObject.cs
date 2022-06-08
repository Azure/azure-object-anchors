// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Microsoft.Azure.ObjectAnchors.Unity.Sample
{
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class TrackedObject : MonoBehaviour
    {
        /// <summary>
        /// The gameobject that defines the object's multi-anchor placement
        /// </summary>
        public GameObject MultiAnchorPlacement;

        /// <summary>
        /// The gameobject that defines the object's single-anchor placment
        /// </summary>
        public GameObject SingleAnchorPlacement;

        // The object geometry isn't necessarily centered in the objects bounding box
        // As a consequence we must take care that other visualziations intended to be
        // placed relatively to a detetected object can be placed as expected
        public GameObject LogicalCenter;

        /// <summary>
        /// The component which renders the model mesh for the single-anchor placement
        /// </summary>
        public ModelMeshRenderer SingleAnchorRenderer;

        /// <summary>
        /// The component which renders the model mesh for the multi-anchor placement
        /// </summary>
        public ModelMeshRenderer MultiAnchorRenderer;

        public TrackedObjectData TrackedObjectState { get; private set; } = new TrackedObjectData();
        private IObjectAnchorsServiceEventArgs _pendingTrackedObjectData;
        private TextMesh _debugText;
        private MultiAnchorObjectPlacement _multiAnchorPlacement;
        private Vector3 _scaleChange = Vector3.one;
        private ObjectTracker _objectTracker;

        private static readonly Dictionary<ObjectInstanceTrackingMode, Color> trackingModeToSingleAnchorColor = new Dictionary<ObjectInstanceTrackingMode, Color>()
        {
            {ObjectInstanceTrackingMode.HighLatencyAccuratePosition, new Color(1,1,0,0.5f) },
            {ObjectInstanceTrackingMode.LowLatencyCoarsePosition, new Color(1,0,1,0.5f) },
            {ObjectInstanceTrackingMode.Paused, new Color(1,1,1,0.5f) }
        };

        private static readonly Dictionary<ObjectInstanceTrackingMode, Color> trackingModeToMultiAnchorColor = new Dictionary<ObjectInstanceTrackingMode, Color>()
        {
            {ObjectInstanceTrackingMode.HighLatencyAccuratePosition, new Color(0,1,0,0.5f) },
            {ObjectInstanceTrackingMode.LowLatencyCoarsePosition, new Color(0,1,1,0.5f) },
            {ObjectInstanceTrackingMode.Paused, new Color(1,1,1,0.5f) }
        };

        private void Start()
        {
            _objectTracker = ObjectTracker.Instance;
            _debugText = GetComponentInChildren<TextMesh>();
            MultiAnchorRenderer.MeshRendererComponent.sortingOrder = -100; // Render multi-anchor placement first so its depth values preclude rendering of other placements that are "under" it
        }

        private void Update()
        {
            IObjectAnchorsServiceEventArgs pendingTrackedObjectData = Interlocked.Exchange(ref _pendingTrackedObjectData, null);
            if (pendingTrackedObjectData != null)
            {
                UpdatePlacement(pendingTrackedObjectData);
                UpdateMeshes();
                UpdateDebugText();
            }

            MultiAnchorRenderer.gameObject.SetActive(_objectTracker.MultiAnchorPlacement);
            SingleAnchorRenderer.gameObject.SetActive(_objectTracker.SingleAnchorPlacement);
            if (_objectTracker.SingleAnchorPlacement)
            {
                SingleAnchorRenderer.transform.localScale = _objectTracker.ScaleSingleAnchorPlacement ? _scaleChange : Vector3.one;
            }
        }

        private void UpdatePlacement(IObjectAnchorsServiceEventArgs data)
        {
            if (_multiAnchorPlacement == null)
            {
                _multiAnchorPlacement = MultiAnchorPlacement.GetComponent<MultiAnchorObjectPlacement>();
            }

            _multiAnchorPlacement.UpdatePlacement(data);

            if (data.Location.HasValue)
            {
                SingleAnchorPlacement.transform.SetPositionAndRotation(data.Location.Value.Position, data.Location.Value.Orientation);
            }
            SingleAnchorPlacement.SetActive(data.Location.HasValue);
            _scaleChange = data.ScaleChange;

            ObjectAnchorsBoundingBox? bb = TrackedObjectState.BaseLogicalBoundingBox;
            if (bb.HasValue)
            {
                LogicalCenter.transform.localPosition = bb.Value.Center;
                LogicalCenter.transform.localRotation = bb.Value.Orientation;
            }
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

        private void UpdateMeshes()
        {
            SingleAnchorRenderer.UpdateMesh(TrackedObjectState.ModelMesh, trackingModeToSingleAnchorColor[TrackedObjectState.TrackingMode]);
            MultiAnchorRenderer.UpdateMesh(TrackedObjectState.ModelMesh, trackingModeToMultiAnchorColor[TrackedObjectState.TrackingMode]);
        }

        public void UpdateTrackedObjectData(IObjectAnchorsServiceEventArgs data)
        {
            Debug.Log($"Updating tracking data for {data.InstanceId} model {data.ModelId} mode {data.TrackingMode}");
            TrackedObjectState.UpdateTrackingData(data);
            Interlocked.Exchange(ref _pendingTrackedObjectData, data);
        }
    }
}