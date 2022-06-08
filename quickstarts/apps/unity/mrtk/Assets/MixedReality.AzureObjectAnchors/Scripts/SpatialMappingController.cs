// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
using Microsoft.MixedReality.Toolkit;
using Microsoft.MixedReality.Toolkit.SpatialAwareness;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace Microsoft.Azure.ObjectAnchors.Unity.Sample
{
    public class SpatialMappingController : MonoBehaviour
    {
        public enum ObserverMode
        {
            Auto = 0,
            ForceOff,
            ForceOn,
            Max
        }

        static public Dictionary<ObserverMode, string> ObserverModeNames { get; private set; } = new Dictionary<ObserverMode, string>()
        {
            {ObserverMode.Auto, "Automatic" },
            {ObserverMode.ForceOff, "Off" },
            {ObserverMode.ForceOn, "On" }
        };

        public ObserverMode CurrentObserverMode { get; private set; }
        private ObjectTracker _objectTracker = null;
        private IMixedRealitySpatialAwarenessMeshObserver _surfaceObserver = null;
        private IObjectAnchorsService _objectAnchorsService;
        SearchAreaController _searchAreaController;
        public bool ObserverRunning { get; private set; } = false;

        private void Awake()
        {
            _objectAnchorsService = ObjectAnchorsService.GetService();

            _surfaceObserver = TryToGetObserver();
            if (_surfaceObserver != null)
            {
                _surfaceObserver.Disable();
                _surfaceObserver.DisplayOption = SpatialAwarenessMeshDisplayOptions.None;
            }

            _objectTracker = FindObjectOfType<ObjectTracker>();
        }

        private void Update()
        {
            _surfaceObserver = TryToGetObserver();
            if (_surfaceObserver == null)
            {
                return;
            }

            switch(CurrentObserverMode)
            {
                case ObserverMode.Auto:
                    if (_objectTracker.QueryActive || _objectAnchorsService.TrackingResults.Count == 0)
                    {
                        _surfaceObserver.DisplayOption = SpatialAwarenessMeshDisplayOptions.Visible;
                    }
                    else
                    {
                        _surfaceObserver.DisplayOption = SpatialAwarenessMeshDisplayOptions.None;
                    }
                    break;
                case ObserverMode.ForceOn:
                    _surfaceObserver.DisplayOption = SpatialAwarenessMeshDisplayOptions.Visible;
                    break;
                case ObserverMode.ForceOff:
                    _surfaceObserver.DisplayOption = SpatialAwarenessMeshDisplayOptions.None;
                    break;
            }
        }

        private void OnEnable()
        {
            _searchAreaController = FindObjectOfType<SearchAreaController>();
            if (_searchAreaController != null)
            {
                _searchAreaController.SearchAreaMoved += _searchAreaController_SearchAreaMoved;
            }
            StartObserver();
        }

        
        private void OnDisable()
        {
            StopObserver();
            if (_searchAreaController != null)
            {
                _searchAreaController.SearchAreaMoved -= _searchAreaController_SearchAreaMoved;
            }
        }

        public void StartObserver()
        {
            _surfaceObserver = TryToGetObserver();
            if (_surfaceObserver != null)
            {
                _surfaceObserver.Enable();
                _surfaceObserver.DisplayOption = SpatialAwarenessMeshDisplayOptions.Visible;
                ObserverRunning = true;
                Debug.Log("Observer started");
            }
            else
            {
                Debug.Log("No observer?");
            }
        }

        public void StopObserver()
        {
            _surfaceObserver = TryToGetObserver();
            if (_surfaceObserver != null)
            {
                _surfaceObserver.Disable();
                _surfaceObserver.DisplayOption = SpatialAwarenessMeshDisplayOptions.None;
                Debug.Log("Observer Stopped");
            }

            ObserverRunning = false;
        }

        public void CycleObserver()
        {
            int currentModeAsInt = (int)CurrentObserverMode;
            currentModeAsInt = (currentModeAsInt+1) % (int)ObserverMode.Max;
            CurrentObserverMode = (ObserverMode)currentModeAsInt;
        }

        private IMixedRealitySpatialAwarenessMeshObserver TryToGetObserver()
        {
            IMixedRealitySpatialAwarenessMeshObserver retval = null;
            if (MixedRealityServiceRegistry.TryGetService<IMixedRealitySpatialAwarenessSystem>(out var service))
            {
                IMixedRealityDataProviderAccess dataProviderAccess = service as IMixedRealityDataProviderAccess;

                retval =
                    dataProviderAccess.GetDataProvider<IMixedRealitySpatialAwarenessMeshObserver>();
            }

            return retval;
        }
        private void _searchAreaController_SearchAreaMoved(object sender, System.EventArgs e)
        {
            _surfaceObserver = TryToGetObserver();
            _surfaceObserver.ObserverVolumeType = MixedReality.Toolkit.Utilities.VolumeType.AxisAlignedCube;
            _surfaceObserver.ObserverOrigin = _searchAreaController.SearchArea.Center;
            _surfaceObserver.ObserverRotation = _searchAreaController.SearchArea.Orientation;
            _surfaceObserver.ObservationExtents = _searchAreaController.SearchArea.Extents;
        }
    }
}