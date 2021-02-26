// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
#if UNITY_WSA
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Microsoft.Azure.ObjectAnchors.Unity.Sample
{
    /// <summary>
    /// Attempts to control the search area bounding box to 
    /// discover objects. This script is assumed to be attached
    /// to the bounding box with the SearchAreaController script.
    /// </summary>
    /// 
    [RequireComponent(typeof(SearchAreaController))]
    public class AutonomousSearchArea : MonoBehaviour
    {
        private const float RefineBoxStartingSizeMultiplier = 1.10f;
        private const float CoarseDetectionMinSurfaceCoverageMultiplier = 0.6f;

        private IObjectAnchorsService _objectAnchorsService;
        private SearchAreaController _searchAreaController;
        private ObjectTracker _objectTracker;
        private int _lastQueryFrame = 0;
        public bool AreaRefinementEnabled { get; set; } = false;
        
        void Start()
        {
            _objectAnchorsService = ObjectAnchorsService.GetService();
           
            _objectTracker = ObjectTracker.Instance;
        }

        private void OnEnable()
        {
            _searchAreaController = GetComponent<SearchAreaController>();
        }

        void Update()
        {
            if (_objectAnchorsService.Status == ObjectAnchorsServiceStatus.Paused || 
                _objectTracker.QueryActive || _objectTracker.QueryQueued)
            {
                return;
            }

            if (_objectTracker.TrackedObjectCount == 0 || Time.frameCount - _lastQueryFrame > 60)
            {
                _lastQueryFrame = Time.frameCount;
                if (_objectTracker.TrackedObjectCount == 0)
                {
                    // if we haven't found anything, we will do a query where the box is
                    UpdateBoxForGlobalQuery();
                }
                else if (AreaRefinementEnabled)
                {
                    // if we have founds something, we will try to improve it with a more accurately
                    // centered box.
                    RefineBox();
                }
            }
        }

        private void UpdateBoxForGlobalQuery()
        {
            Debug.Log($"global search");
            _objectTracker.QueueQueriesInBounds(_searchAreaController.SearchArea);
        }

        private void RefineBox()
        {
            // Scan the tracked objects looking for the one with the worst coverage that
            // exceeds the minimum threshold.
            foreach (TrackedObject to in _objectTracker.TrackedObjects)
            {
                // Above this, we won't retry to detect an object
                float desiredMinimumCoverage = to.TrackedObjectState.BaseModelData.UseCustomParameters ?
                    to.TrackedObjectState.BaseModelData.MinSurfaceCoverage :
                    to.TrackedObjectState.BaseModelData.MinSurfaceCoverageFromObjectModel;
                
                // Below this and we won't consider the tracked object to be valid
                float minimumCoarseCoverage = desiredMinimumCoverage * CoarseDetectionMinSurfaceCoverageMultiplier;

                if (to.TrackedObjectState.SurfaceCoverage >= desiredMinimumCoverage)
                {
                    // Coverage is good enough
                    WrapBoxAroundObject(to);
                }
                else if (to.TrackedObjectState.SurfaceCoverage >= minimumCoarseCoverage)
                {
                    // we will be refining again. Update the search area with the new query.
                    ObjectAnchorsBoundingBox? bb = to.TrackedObjectState.BaseLogicalBoundingBox;

                    if (bb.HasValue)
                    {
                        // We always want to put the box around where the object is.
                        // If the tracking is good enough or the object didn't get a good enough
                        // tracking after a few attempts, we will just draw the box, but not
                        // run another query.
                        _searchAreaController.UpdateBoxTransform(
                           to.TrackedObjectState.Location.Value.Orientation * bb.Value.Center + to.TrackedObjectState.Location.Value.Position,
                           to.TrackedObjectState.Location.Value.Orientation * bb.Value.Orientation,
                           bb.Value.Extents * RefineBoxStartingSizeMultiplier);
                     
                       _objectTracker.QueueQueriesInBounds(_searchAreaController.SearchArea);
                    }
                }
            }
        }

        /// <summary>
        /// Places the search box around the specified object.
        /// </summary>
        /// <param name="trackedObject">The object to place the search box around</param>
        private void WrapBoxAroundObject(TrackedObject trackedObject)
        {
            if (trackedObject != null)
            {
                ObjectAnchorsBoundingBox? bb = trackedObject.TrackedObjectState.BaseLogicalBoundingBox;
                if (bb.HasValue)
                {
                    _searchAreaController.UpdateBoxTransform(
                        trackedObject.TrackedObjectState.Location.Value.Orientation * bb.Value.Center + trackedObject.TrackedObjectState.Location.Value.Position,
                        trackedObject.TrackedObjectState.Location.Value.Orientation * bb.Value.Orientation,
                        bb.Value.Extents);
                }
            }
        }
    }
}
#endif // UNITY_WSA