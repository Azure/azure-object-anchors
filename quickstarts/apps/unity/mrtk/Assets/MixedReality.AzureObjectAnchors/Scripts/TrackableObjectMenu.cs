
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
#if UNITY_WSA
using UnityEngine;

using Microsoft.MixedReality.Toolkit;
using Microsoft.MixedReality.Toolkit.UI;

namespace Microsoft.Azure.ObjectAnchors.Unity.Sample
{
    public class TrackableObjectMenu : MonoBehaviour
    {
        [Header("Buttons")]

        [Tooltip("The start search button")]
        public Interactable StartSearchButton = null;

        [Tooltip("The stop search button")]
        public Interactable StopSearchButton = null;

        [Tooltip("The toggle search area button")]
        public Interactable ToggleSearchAreaButton = null;

        [Tooltip("The toggle spatial mapping button")]
        public Interactable ToggleSpatialMappingButton = null;

        [Tooltip("The toggle active observation button")]
        public Interactable ToggleActiveObservationButton = null;

        [Tooltip("The toggle high accuracy button")]
        public Interactable ToggleHighAccuracyButton = null;

        [Tooltip("The start debug tracing button")]
        public Interactable StartTracingButton = null;

        [Tooltip("The stop debug tracing button")]
        public Interactable StopTracingButton = null;

        [Tooltip("The upload debug tracing button")]
        public Interactable UploadTracingButton = null;

        [Tooltip("The search settings submenu component")]
        public GameObject SearchAreaSubmenu = null;

        [Tooltip("The tracking settings submenu component")]
        public GameObject TrackingOptionsSubMenu = null;

        [Tooltip("The scale toggle")]
        public Interactable ScaleToggle = null;

        [Tooltip("The angle tolerance toggle")]
        public Interactable AngleToleranceToggle = null;

        [Tooltip("The show environment observations toggle")]
        public Interactable ShowEnvironmentObservationsToggle = null;

        [Tooltip("The multi-anchor placement toggle")]
        public Interactable MultiAnchorPlacementToggle = null;

        [Tooltip("The single-anchor placement toggle")]
        public Interactable SingleAnchorPlacementToggle = null;

        [Tooltip("The scale single-anchor placement toggle")]
        public Interactable ScaleSingleAnchorPlacementToggle = null;

        [Tooltip("The coverage slider label")]
        public TextMesh CoverageRatioSliderLabel = null;

        [Header("Object Tracking Components")]

        [Tooltip("The search component")]
        public TrackableObjectSearch Search = null;

        private TextToSpeech _textToSpeech;
        private ObjectTracker _objectTracker;
        private TMPro.TextMeshPro _searchAreaButtonTMP;
        private bool _highAccuracyModeEnabled = false;

        private void Awake()
        {
            _textToSpeech = gameObject.EnsureComponent<TextToSpeech>();
        }

        private void OnEnable()
        {
            StartSearchButton?.OnClick.AddListener(StartSearch);
            StopSearchButton?.OnClick.AddListener(StopSearch);

            ToggleSpatialMappingButton?.OnClick.AddListener(ToggleSpatialMapping);

            StartTracingButton?.OnClick.AddListener(StartTracing);
            StopTracingButton?.OnClick.AddListener(StopTracing);
            UploadTracingButton?.OnClick.AddListener(UploadTracing);
            
            _objectTracker = ObjectTracker.Instance;
        }

        private void OnDisable()
        {
            StartSearchButton?.OnClick.RemoveListener(StartSearch);
            StopSearchButton?.OnClick.RemoveListener(StopSearch);

            ToggleSpatialMappingButton?.OnClick.RemoveListener(ToggleSpatialMapping);

            StartTracingButton.OnClick?.RemoveListener(StartTracing);
            StopTracingButton.OnClick?.RemoveListener(StopTracing);
            UploadTracingButton.OnClick?.RemoveListener(UploadTracing);
        }

        private void StartSearch()
        {
            ObjectAnchorsService.GetService().Resume();

            StopSearchButton.gameObject.SetActive(true);
            StartSearchButton.gameObject.SetActive(false);
            ToggleActiveObservationButton.IsEnabled = false;

            _textToSpeech.Speak("Object tracking started.");
        }

        private void StopSearch()
        {
            ObjectAnchorsService.GetService().Pause();
            StopSearchButton.gameObject.SetActive(false);
            StartSearchButton.gameObject.SetActive(true);
            ToggleActiveObservationButton.IsEnabled = true;

            _textToSpeech.Speak("Object tracking stopped.");
        }

        public void ToggleSearchSubmenu()
        {
            bool shouldActivate = !SearchAreaSubmenu.activeSelf;
            HideSubmenus();
            SearchAreaSubmenu.SetActive(shouldActivate);
        }

        public void ToggleTrackingOptionsMenu()
        {
            bool shouldActivate = !TrackingOptionsSubMenu.activeSelf;
            HideSubmenus();
            TrackingOptionsSubMenu.SetActive(shouldActivate);
        }

        private void HideSubmenus()
        {
            SearchAreaSubmenu.SetActive(false);
            TrackingOptionsSubMenu.SetActive(false);
        }

        private void ToggleSpatialMapping()
        {
            SpatialMappingController smc = FindObjectOfType<SpatialMappingController>();
            if (smc != null)
            {
                smc.CycleObserver();
                _textToSpeech.Speak($"spatial mapping set to {SpatialMappingController.ObserverModeNames[smc.CurrentObserverMode]}");
            }
        }

        public void ToggleActiveObservationMode()
        {
            _objectTracker.ObservationMode = ToggleActiveObservationButton.IsToggled ? ObjectObservationMode.Active : ObjectObservationMode.Ambient;
            _textToSpeech.Speak("Active observation mode " + (ToggleActiveObservationButton.IsToggled ? "on." : "off."));
        }

        public void ToggleHighAccuracyTrackingMode()
        {
            _highAccuracyModeEnabled = !_highAccuracyModeEnabled;

            Search.EnableAccuracyTrackingMode(_highAccuracyModeEnabled);

            _textToSpeech.Speak("High accuracy tracking mode " + (_highAccuracyModeEnabled ? "on." : "off."));
        }

        private void StartTracing()
        {
            ObjectTrackerDiagnostics.Instance.StartDiagnosticsSession();

            StopTracingButton.gameObject.SetActive(true);
            StartTracingButton.gameObject.SetActive(false);

            _textToSpeech.Speak("Debug tracing started.");
        }

        private async void StopTracing()
        {
            await ObjectTrackerDiagnostics.Instance.StopDiagnosticsSessionAsync();

            StopTracingButton.gameObject.SetActive(false);
            StartTracingButton.gameObject.SetActive(true);

            _textToSpeech.Speak("Debug tracing stopped.");
        }

        private async void UploadTracing()
        {
            _textToSpeech.Speak("Start uploading diagnostics.");
            bool uploaded = await ObjectTrackerDiagnostics.Instance.UploadDiagnosticsAsync();
            _textToSpeech.Speak("Diagnostics uploading " + (uploaded ? " succeeded." : " failed."));
        }

        public void CoverageRatioSliderChanged(SliderEventData sliderEventData)
        {
            _objectTracker.CoverageThresholdFactor = sliderEventData.NewValue;
            CoverageRatioSliderLabel.text = $"Coverage Ratio {_objectTracker.CoverageThresholdFactor.ToString("F2")}";
        }
        
        public void ToggleScale()
        {
            _objectTracker.MaxScaleChange = ScaleToggle.IsToggled ? 0.1f : 0;
            Debug.Log($"ToggleScale {_objectTracker.MaxScaleChange} {ScaleToggle.IsToggled}");
        }

        public void ToggleAngleTolerance()
        {
            _objectTracker.AllowedVerticalOrientationInDegrees = AngleToleranceToggle.IsToggled ? 10 : 0;
            Debug.Log($"Toggle Angle Tolerance {_objectTracker.AllowedVerticalOrientationInDegrees} {AngleToleranceToggle.IsToggled}");
        }

        public void ToggleShowEnvironmentObservations()
        {
            _objectTracker.ShowEnvironmentObservations = ShowEnvironmentObservationsToggle.IsToggled;
            Debug.Log($"Toggle Show Environment Observations {_objectTracker.ShowEnvironmentObservations}");
        }

        public void ToggleMultiAnchorPlacement()
        {
            _objectTracker.MultiAnchorPlacement = MultiAnchorPlacementToggle.IsToggled;
            Debug.Log($"Toggle Multi-Anchor Placement {_objectTracker.MultiAnchorPlacement}");
        }

        public void ToggleSingleAnchorPlacement()
        {
            _objectTracker.SingleAnchorPlacement = SingleAnchorPlacementToggle.IsToggled;
            Debug.Log($"Toggle Single-Anchor Placement {_objectTracker.MultiAnchorPlacement}");
        }

        public void ToggleScaleSingleAnchorPlacement()
        {
            _objectTracker.ScaleSingleAnchorPlacement = ScaleSingleAnchorPlacementToggle.IsToggled;
            Debug.Log($"Toggle Scale Single-Anchor Placement {_objectTracker.ScaleSingleAnchorPlacement}");
        }
    }
}
#endif // UNITY_WSA