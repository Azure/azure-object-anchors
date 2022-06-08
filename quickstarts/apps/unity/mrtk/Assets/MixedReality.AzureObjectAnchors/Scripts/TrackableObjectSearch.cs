// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
using System;

using System.Threading.Tasks;
using UnityEngine;
using Microsoft.MixedReality.Toolkit;
using System.Threading;
using Microsoft.MixedReality.Toolkit.Input;

namespace Microsoft.Azure.ObjectAnchors.Unity.Sample
{
    public class TrackableObjectSearch : MonoBehaviour, IMixedRealityPointerHandler
    {
        [Tooltip("The area to perform search")]
        public GameObject SearchAreaBoundingBox;

        private IObjectAnchorsService _objectAnchorsService;

        private ObjectTracker _objectTracker;
        private SearchAreaController _searchAreaControl;

        private ManualResetEvent _initialized;
#region Public methods

        public void EnableAccuracyTrackingMode(bool enabled)
        {
            _objectTracker.TrackingStrategy = enabled ?
                ObjectTracker.TrackingModeStrategy.Accurate :
                ObjectTracker.TrackingModeStrategy.Coarse;
        }
#endregion

#region Unity script behavior

        private void Awake()
        {
            _initialized = new ManualResetEvent(false);
            _objectAnchorsService = ObjectAnchorsService.GetService();
            Debug.Assert(_objectAnchorsService != null);
        }

        private async void Start()
        {
            //
            // Wait for Mixed Reality system to be initialized.
            //
            await MixedRealityToolkitReady();

            //
            // Cache the object tracker
            //
            _objectTracker = ObjectTracker.Instance;
            _objectTracker.ActiveDetectionStrategy = ObjectTracker.DetectionStrategy.Manual;
            _objectTracker.TrackingStrategy = ObjectTracker.TrackingModeStrategy.Coarse;

            _searchAreaControl = SearchAreaBoundingBox.GetComponent<SearchAreaController>();

            SearchAreaBoundingBox.SetActive(true);

            //
            // Register input callback
            //
            CoreServices.InputSystem?.PushFallbackInputHandler(gameObject);

            _initialized.Set();
        }

        private void OnDestroy()
        {
            CoreServices.InputSystem?.PopFallbackInputHandler();
            SearchAreaBoundingBox.SetActive(false);
        }

        private void Update()
        {
            if (!_initialized.WaitOne(0))
            {
                return;
            }
           
            if (_objectAnchorsService.Status == ObjectAnchorsServiceStatus.Paused)
            {
                // Remove all existing objects if not searching.
                foreach (var instance in _objectAnchorsService.TrackingResults)
                {
                    _objectAnchorsService.RemoveObjectInstance(instance.InstanceId);
                }
            }
        }

#endregion

#region IMixedRealityPointerHandler interfaces

        void IMixedRealityPointerHandler.OnPointerUp(MixedRealityPointerEventData eventData)
        {
            if (!_searchAreaControl.SearchAreaLocked && !eventData.used)
            {
                _searchAreaControl.PlaceSearchAreaBoundingBoxInFrontOfUser();
                eventData.Use();
            }
        }

        void IMixedRealityPointerHandler.OnPointerDown(MixedRealityPointerEventData eventData)
        {
        }

        void IMixedRealityPointerHandler.OnPointerDragged(MixedRealityPointerEventData eventData)
        {
        }

        void IMixedRealityPointerHandler.OnPointerClicked(MixedRealityPointerEventData eventData)
        {
        }
#endregion

#region Private methods


        private Task MixedRealityToolkitReady()
        {
            return Task.Run(async () =>
            {
                while (!MixedRealityToolkit.IsInitialized)
                {
                    await Task.Delay(500);
                }
            });
        }
#endregion
    }
}