// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
using Microsoft.MixedReality.Toolkit;
using Microsoft.MixedReality.Toolkit.Input;
using Microsoft.MixedReality.Toolkit.UI;
using Microsoft.MixedReality.Toolkit.UI.BoundsControl;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Microsoft.Azure.ObjectAnchors.Unity.Sample
{
    /// <summary>
    /// Controls the bounding box used to specify the starting search area for objects
    /// </summary>
    public class SearchAreaController : MonoBehaviour
    {
        private const float BoundingBoxSizeFactor = 1.5f;
        private const float SearchAreaDistanceFromUser = 2.5f;
        private bool _isSearchAreaLocked = false;

        /// <summary>
        /// Controls if the user should be able to move the box.
        /// </summary>
        public bool SearchAreaLocked
        {
            get
            {
                return _isSearchAreaLocked;
            }
            set
            {
                _isSearchAreaLocked = value;

                // Disable drag and drop if locked.
                _searchAreaBboxManipulationHandler.enabled = !value;
            }
        }

        /// <summary>
        /// Returns current bounding box.
        /// </summary>
        public ObjectAnchorsBoundingBox SearchArea
        {
            get
            {
                return new ObjectAnchorsBoundingBox
                {
                    Center = transform.TransformPoint(_searchAreaBounds.TargetBounds.center),
                    Extents = transform.TransformSize(_searchAreaBounds.TargetBounds.size),
                    Orientation = transform.rotation
                };
            }
        }

        private IObjectAnchorsService _objectAnchorsService;
        private BoundsControl _searchAreaBounds;
        private ObjectManipulator _searchAreaBboxManipulationHandler;
        private TrackableObjectDataLoader _trackableObjectDataLoader;
        private AutonomousSearchArea _automaticSearchAreaMovementController;
        private SearchAreaModelVisualization _searchAreaModelVisualization;
        private Camera _mainCamera;
        private int _visualizedModelIndex = -1;

        public Vector3 LargestModelScale { get; private set; } = Vector3.one;

        /// <summary>
        /// Raised when the search area is moved by the user
        /// </summary>
        public event EventHandler<EventArgs> SearchAreaMoved;

        private void Start()
        {
            _mainCamera = Camera.main;

            //
            // Find bounding box components.
            //
            _searchAreaBboxManipulationHandler = GetComponent<ObjectManipulator>();
            _searchAreaBboxManipulationHandler.OnManipulationEnded.AddListener(BoundingBoxMoved);
            _searchAreaBounds = GetComponent<BoundsControl>();
            _objectAnchorsService = ObjectAnchorsService.GetService();
            _trackableObjectDataLoader = TrackableObjectDataLoader.Instance;
            _trackableObjectDataLoader.ModelsLoaded += _trackableObjectDataLoader_ModelsLoaded;
            _automaticSearchAreaMovementController = GetComponent<AutonomousSearchArea>();
            _searchAreaModelVisualization = GetComponentInChildren<SearchAreaModelVisualization>();
        }

        private void _trackableObjectDataLoader_ModelsLoaded(object sender, EventArgs e)
        {
            //
            // Load object model from local storage
            //
            var allModels = _trackableObjectDataLoader.TrackableObjects;

            //
            // Compute a bounding box to accommodate latest object.
            //

            if (allModels.Count > 0)
            {
                LargestModelScale = new Vector3();

                foreach (var model in allModels)
                {
                    var boundingbox = _objectAnchorsService.GetModelBoundingBox(model.ModelId);
                    Debug.Assert(boundingbox.HasValue);

                    var largerDimensionOfXY = Math.Max(boundingbox.Value.Extents.x, boundingbox.Value.Extents.y);
                    var smallerDimensionOfXY = Math.Min(boundingbox.Value.Extents.x, boundingbox.Value.Extents.y);
                    var size = new Vector3(largerDimensionOfXY, boundingbox.Value.Extents.z, smallerDimensionOfXY);

                    LargestModelScale = Vector3.Max(LargestModelScale, size);
                }

                LargestModelScale = new Vector3(LargestModelScale.x, LargestModelScale.z, LargestModelScale.y);
                PlaceSearchAreaBoundingBoxInFrontOfUser(BoundingBoxSizeFactor * LargestModelScale);
            }
        }

        private void OnDestroy()
        {
            _searchAreaBboxManipulationHandler.OnManipulationEnded.RemoveListener(BoundingBoxMoved);
        }

        public void PlaceSearchAreaBoundingBoxInFrontOfUser()
        {
            PlaceSearchAreaBoundingBoxInFrontOfUser(BoundingBoxSizeFactor * LargestModelScale, SearchAreaDistanceFromUser);
        }

        public void PlaceSearchAreaBoundingBoxInFrontOfUser(Vector3 size)
        {
            PlaceSearchAreaBoundingBoxInFrontOfUser(size, SearchAreaDistanceFromUser);
        }

        public void PlaceSearchAreaBoundingBoxInFrontOfUser(Vector3 size, float distance)
        {
            Vector3 position = _mainCamera.transform.position + _mainCamera.transform.forward * distance;
            Quaternion rotation = Quaternion.FromToRotation(Vector3.forward, Vector3.up);

            UpdateBoxTransform(position, rotation, size);
        }

        public void UpdateBoxTransform(Vector3 position, Quaternion rotation, Vector3 size)
        {
            transform.SetPositionAndRotation(position, rotation);
            transform.localScale = size;
            SearchAreaMoved?.Invoke(this, EventArgs.Empty);
        }

        private void BoundingBoxMoved(ManipulationEventData data)
        {
            SearchAreaMoved?.Invoke(this, EventArgs.Empty);
        }

        public void ToggleSearchAreaLocked()
        {
            SearchAreaLocked = !SearchAreaLocked;
        }

        public void ToggleAutomaticSearchArea()
        {
            _automaticSearchAreaMovementController.AreaRefinementEnabled = !_automaticSearchAreaMovementController.AreaRefinementEnabled;
        }

        public void CycleVisualizedModel()
        {
            int numModels = _trackableObjectDataLoader.TrackableObjects.Count;
            _visualizedModelIndex++;
            if (_visualizedModelIndex >= numModels)
            {
                _visualizedModelIndex = -1; // not visualizing
                _searchAreaModelVisualization.SetTrackableObjectData(null);
            }
            else
            {
                _searchAreaModelVisualization.SetTrackableObjectData(_trackableObjectDataLoader.TrackableObjects[_visualizedModelIndex]);
            }
        }
    }
}