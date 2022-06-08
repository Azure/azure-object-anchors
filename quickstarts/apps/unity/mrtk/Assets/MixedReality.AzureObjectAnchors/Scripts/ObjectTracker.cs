// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
#if WINDOWS_UWP || DOTNETWINRT_PRESENT
#define SPATIALCOORDINATESYSTEM_API_PRESENT
#endif

using Microsoft.MixedReality.Toolkit;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Microsoft.Azure.ObjectAnchors.Unity.Sample
{
    /// <summary>
    /// Interfaces with Azure Object Anchors to locate and track physical objects
    /// </summary>
    public class ObjectTracker : MonoBehaviour
    {
        private static ObjectTracker _instance;

        public static ObjectTracker Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<ObjectTracker>();
                }

                return _instance;
            }
        }


        /// <summary>
        /// DetectionStrategy determines how the object tracker should try to detect objects
        /// currently this is limited to Auto and Manual, but it is easy to imagine more granular
        /// auto detection strategies might be desired in the future.
        /// </summary>
        public enum DetectionStrategy
        {
            Auto = 0,
            Manual
        }

        /// <summary>
        /// The detection strategy to use.
        /// Auto
        ///     - Objects will attempt to be be detected in the area around the user when no objects are detected
        ///       Object detection will stop once an object has been found.
        /// Manual
        ///     - Objects will attempt to be detected when requested through the StartQuery or QueueQueriesInBounds methods
        /// </summary>
        public DetectionStrategy ActiveDetectionStrategy = DetectionStrategy.Auto;

        private ObjectObservationMode _observationMode = ObjectObservationMode.Ambient;

        /// <summary>
        /// The observation mode to use.
        /// 
        /// Ambient
        ///     - Ambient observation relies on information about the environment that has been gathered by the system automatically
        ///       as the user uses the device. This can result in quicker detection initially since the environment may already
        ///       be scanned. However it may also contain stale data which can lead to poorer quality results. It is also limited
        ///       to a standard resolution and area determined by the system, rather than tailored to the model.
        /// Active
        ///     - Active observation creates a fresh scan of the environment that is optimized for the model being detected.
        ///       This avoids any problems with stale data, but requires the user to take some time to scan the environment
        ///       before the object can be detected. Observations will continue to be accumulated until the query object
        ///       is disposed, so re-using a query for multiple detections may produce quicker results.
        /// </summary>
        [HideInInspector]
        public ObjectObservationMode ObservationMode
        {
            get => _observationMode;
            set { _observationMode = value; UpdateQueue.Enqueue(InitializeQueries); }
        }

        /// <summary>
        /// TrackingStrategy defines how a previously detected object will be updated
        /// </summary>
        public enum TrackingModeStrategy
        {
            Auto = 0,
            Accurate,
            Coarse,
            Pause
        }

        private TrackingModeStrategy _trackingStrategy = TrackingModeStrategy.Auto;

        /// <summary>
        /// The Tracking Strategy to use.
        /// Auto
        ///     - When an object is in the camera FOV, the object will be set to accurate tracking
        ///       Otherwise the object will be set to Paused
        /// Accurate
        /// Coarse
        /// Pause
        ///     - All objects will have their tracking set in the specified mode.
        /// </summary>
        public TrackingModeStrategy TrackingStrategy
        {
            get
            {
                return _trackingStrategy;
            }
            set
            {
                if (_trackingStrategy != value)
                {
                    _trackingStrategy = value;
                    TrackingStrategyUpdated();
                }
            }
        }

        // Maps our internal tracking mode to the APIs tracking mode.
        private static readonly Dictionary<TrackingModeStrategy, ObjectInstanceTrackingMode> strategyToMode = new Dictionary<TrackingModeStrategy, ObjectInstanceTrackingMode>()
        {
            {TrackingModeStrategy.Accurate, ObjectInstanceTrackingMode.HighLatencyAccuratePosition},
            {TrackingModeStrategy.Coarse, ObjectInstanceTrackingMode.LowLatencyCoarsePosition},
            {TrackingModeStrategy.Pause, ObjectInstanceTrackingMode.Paused},
        };

        /// <summary>
        /// The gameobject to represent a detected object
        /// </summary>
        public GameObject TrackedObjectPrefab;

        /// <summary>
        /// The gameobject to represent an environment obseration
        /// </summary>
        public GameObject EnvironmentObservationPrefab;

        private bool _showEnvironmentObservations = false;

        /// <summary>
        /// Whether to show environment observations during object search
        /// </summary>
        [HideInInspector]
        public bool ShowEnvironmentObservations
        {
            get => _showEnvironmentObservations;
            set { _showEnvironmentObservations = value; UpdateEnvironmentObservationVisuals(); }
        }

        private bool _multiAnchorPlacement = true;

        /// <summary>
        /// Whether to use multi-anchor placement for detected object instances
        /// </summary>
        [HideInInspector]
        public bool MultiAnchorPlacement
        {
            get => _multiAnchorPlacement;
            set { _multiAnchorPlacement = value; }
        }

        private bool _singleAnchorPlacement = false;

        /// <summary>
        /// Whether to use single-anchor placement for detected object instances
        /// </summary>
        [HideInInspector]
        public bool SingleAnchorPlacement
        {
            get => _singleAnchorPlacement;
            set { _singleAnchorPlacement = value; }
        }

        private bool _scaleSingleAnchorPlacement = false;

        /// <summary>
        /// Whether to scale rendering of the single-anchor placement for detected object instances
        /// </summary>
        [HideInInspector]
        public bool ScaleSingleAnchorPlacement
        {
            get => _scaleSingleAnchorPlacement;
            set { _scaleSingleAnchorPlacement = value; }
        }

        private GameObject _environmentObservationVisuals;
        private void UpdateEnvironmentObservationVisuals()
        {
            _environmentObservationVisuals.SetActive(ShowEnvironmentObservations && _objectAnchorsService.Status == ObjectAnchorsServiceStatus.Running);
        }

        /// <summary>
        /// True while we are awaiting a query to complete
        /// </summary>
        public bool QueryActive { get; set; }

        public Tuple<ObjectAnchorsBoundingBox, IEnumerable<ObjectQuery>> ActiveQueries { get; set; }

        public bool QueryQueued
        {
            get
            {
                return _queryQueue.Count > 0;
            }
        }

        public int TrackedObjectCount
        {
            get
            {
                if (_objectAnchorsService != null && _objectAnchorsService.TrackingResults != null)
                {
                    return _objectAnchorsService.TrackingResults.Count;
                }
                return 0;
            }
        }

        private float _maxScaleChange = 0.1f;
        public float MaxScaleChange
        {
            get
            {
                return _maxScaleChange;
            }
            set
            {
                _maxScaleChange = Mathf.Clamp(value, 0, 1);
            }
        }

        private float _allowedVerticalOrientationInDegrees = 0f;
        public float AllowedVerticalOrientationInDegrees
        {
            get
            {
                return _allowedVerticalOrientationInDegrees;
            }
            set
            {
                _allowedVerticalOrientationInDegrees = Mathf.Clamp(value, 0, 180f);
            }
        }

        private float _coverageThresholdFactor = 1.0f;
        public float CoverageThresholdFactor
        {
            get
            {
                return _coverageThresholdFactor;
            }
            set
            {
                _coverageThresholdFactor = Mathf.Clamp(value, 0.01f, 1.0f);
            }
        }
            
        // cached handle to object tracking service
        private IObjectAnchorsService _objectAnchorsService;

        // Map of instance id to tracked object data. TrackedObject is attached to the trackedObjectPrefab
        private Dictionary<Guid, TrackedObject> _instanceToTrackedObject = new Dictionary<Guid, TrackedObject>();

        // process things that come in on background threads on the unity thread
        private ConcurrentQueue<Action> UpdateQueue = new ConcurrentQueue<Action>();

        private TrackableObjectDataLoader _trackableObjectDataLoader;

        class TrackableObjectQuery : IDisposable
        {
            public TrackableObjectData TrackableObjectData;
            public ObjectQuery Query;
            public EnvironmentObservationRenderer EnvironmentObservation;

            public void Dispose()
            {
                if (EnvironmentObservation != null)
                {
                    Destroy(EnvironmentObservation.gameObject);
                }

                Query.Dispose();
            }
        }

        void DisposeQueries()
        {
            if (_trackableObjectQueries != null)
            {
                foreach (var toq in _trackableObjectQueries)
                {
                    toq.Dispose();
                }
                _trackableObjectQueries = null;
            }
        }

        void InitializeQueries()
        {
            DisposeQueries();
            _trackableObjectQueries = _trackableObjectDataLoader.TrackableObjects.Select(InitializeQuery).ToList();

            // In principle, each ObjectQuery may have distinct observations of the environment, depending on differences in 
            // search area, model resolution, and observation mode. In practice, in this sample all queries are created
            // with the same parameters (except for the model). Therefore environment observations are only visualized for
            // the first once since it would be redundant to do this for all of them. However, this could be repeated
            // for additional queries with differing parameters as needed.
            UpdateEnvironmentObservationVisuals();
            if (_trackableObjectQueries.Count != 0)
            {
                var toq = _trackableObjectQueries.First();
                toq.EnvironmentObservation = Instantiate(EnvironmentObservationPrefab).GetComponent<EnvironmentObservationRenderer>();
                toq.EnvironmentObservation.transform.parent = _environmentObservationVisuals.transform;
                toq.EnvironmentObservation.Query = toq.Query;
                toq.EnvironmentObservation.EnvironmentTopology =
                    ObservationMode == ObjectObservationMode.Ambient ? // Ambient observation mode only supports providing a point cloud.
                        EnvironmentObservationTopology.PointCloud :
                        EnvironmentObservationTopology.TriangleList;
            }
        }

        TrackableObjectQuery InitializeQuery(TrackableObjectData tod)
        {
            TrackableObjectQuery toq = new TrackableObjectQuery();
            toq.TrackableObjectData = tod;
            toq.Query = ObjectAnchorsService.GetService().CreateObjectQuery(tod.ModelId, ObservationMode);
            tod.MinSurfaceCoverageFromObjectModel = toq.Query.MinSurfaceCoverage;
            return toq;
        }

        private List<TrackableObjectQuery> _trackableObjectQueries;

        // Queue of queries to run
        private ConcurrentQueue<Tuple<ObjectAnchorsBoundingBox, IEnumerable<ObjectQuery>>> _queryQueue = new ConcurrentQueue<Tuple<ObjectAnchorsBoundingBox, IEnumerable<ObjectQuery>>>();

        private Camera _mainCamera;

        private bool _awaiting = false;

        private void Awake()
        {
            _objectAnchorsService = ObjectAnchorsService.GetService();
            if (_objectAnchorsService == null)
            {
                Debug.LogError("Could not get AOA service");
                Destroy(this);
            }
        }

        private async void Start()
        {
            _mainCamera = Camera.main;
            _trackableObjectDataLoader = TrackableObjectDataLoader.Instance;

            _awaiting = true;
            try
            {
                await _objectAnchorsService.InitializeAsync();
            }
            catch (System.ArgumentException ex)
            {
#if WINDOWS_UWP
                string message = ex.Message;
                global::Windows.Foundation.IAsyncOperation<global::Windows.UI.Popups.IUICommand> dialog = null;
                UnityEngine.WSA.Application.InvokeOnUIThread(() => dialog = new global::Windows.UI.Popups.MessageDialog(message, "Invalid account information").ShowAsync(), true);
                await dialog;
#elif UNITY_EDITOR
                UnityEditor.EditorUtility.DisplayDialog("Invalid account information", ex.Message, "OK");
#endif // WINDOWS_UWP
                throw ex;
            }
            _objectAnchorsService.Pause();

            bool foundModelsInAppPath = false;
            bool foundModelsInObjects3D = false;
            
            // Read models from LocalState folder
            foundModelsInAppPath = await _trackableObjectDataLoader.LoadObjectModelsAsync(Application.persistentDataPath);
            // Read models from 3D Objects folder
#if WINDOWS_UWP
            foundModelsInObjects3D = await _trackableObjectDataLoader.LoadObjectModelsFromWellKnownFolderAsync(global::Windows.Storage.KnownFolders.Objects3D.Path);
#endif

            _environmentObservationVisuals = new GameObject("Environment Observation Visuals");
            InitializeQueries();

            _awaiting = false;

            // if the trackable object loader doesn't find anything, may as well give up.
            // future: add a refresh button...
            if (foundModelsInAppPath || foundModelsInObjects3D)
            {
                _objectAnchorsService.ObjectAdded += _objectAnchorsService_ObjectAdded;
                _objectAnchorsService.ObjectUpdated += _objectAnchorsService_ObjectUpdated;
                _objectAnchorsService.ObjectRemoved += _objectAnchorsService_ObjectRemoved;
                _objectAnchorsService.RunningChanged += _objectAnchorsService_RunningChanged;
                Debug.Log("Object detector ready");
            }
            else
            {
                Debug.LogWarning("no objects to detect.");
                enabled = false;
            }
        }

        private async void Update()
        {
            // even though this is an 'async' function, unity will call Update each frame
            // even if a previous update is awaiting.
            if (_awaiting)
            {
                return;
            }

            Action nextAction;
            Tuple<ObjectAnchorsBoundingBox, IEnumerable<ObjectQuery>> nextQuery;
            if (UpdateQueue.TryDequeue(out nextAction))
            {
                nextAction();
            }
            else if (_queryQueue.TryDequeue(out nextQuery))
            {
                _awaiting = true;
                QueryActive = true;
                ActiveQueries = nextQuery;
                await _objectAnchorsService.DetectObjectAsync(nextQuery.Item2.ToArray());
                ActiveQueries = null;
                QueryActive = false;
                _awaiting = false;
            }
            else if (_objectAnchorsService.Status == ObjectAnchorsServiceStatus.Running)
            {
                if (TrackingStrategy == TrackingModeStrategy.Auto)
                {
                    ManageLocatedObjectTrackingStates();
                }

                // If nothing has been found, try another query.
                if (_instanceToTrackedObject.Count == 0 && ActiveDetectionStrategy == DetectionStrategy.Auto)
                {
                    RefillGlobalQueryQueue();
                }
            }
        }

        private void OnDestroy()
        {
            DisposeQueries();

            _trackableObjectDataLoader.Dispose();
            _trackableObjectDataLoader = null;

            _objectAnchorsService.Dispose();
            _objectAnchorsService = null;
        }

#region Callbacks
        /// The Azure Obeject Anchors callbacks arrive off of the Unity thread, so we queue them to run on the unity thread.
        private void _objectAnchorsService_RunningChanged(object sender, ObjectAnchorsServiceStatus e)
        {
            UpdateQueue.Enqueue(() => { RunningChanged(e); });
        }

        private void RunningChanged(ObjectAnchorsServiceStatus e)
        {
            switch (e)
            {
                case ObjectAnchorsServiceStatus.Paused:
                    _queryQueue = new ConcurrentQueue<Tuple<ObjectAnchorsBoundingBox, IEnumerable<ObjectQuery>>>();
                    break;
                case ObjectAnchorsServiceStatus.Running:
                    if (ActiveDetectionStrategy == DetectionStrategy.Auto)
                    {
                        RefillGlobalQueryQueue();
                    }
                    break;
            }
            UpdateEnvironmentObservationVisuals();
        }

        private void _objectAnchorsService_ObjectRemoved(object sender, IObjectAnchorsServiceEventArgs e)
        {
            UpdateQueue.Enqueue(() => { ObjectRemoved(e); });
        }

        private void ObjectRemoved(IObjectAnchorsServiceEventArgs e)
        {
            Debug.Log($"remove instance {e.InstanceId} of model {e.ModelId}");
            TrackedObject to;
            if (_instanceToTrackedObject.TryGetValue(e.InstanceId, out to))
            {
                Destroy(to.gameObject);
                _instanceToTrackedObject.Remove(e.InstanceId);
            }
        }

        private void _objectAnchorsService_ObjectUpdated(object sender, IObjectAnchorsServiceEventArgs e)
        {
            UpdateQueue.Enqueue(() => { ObjectUpdated(e); });
        }

        private void ObjectUpdated(IObjectAnchorsServiceEventArgs e)
        {
            AddOrUpdate(e);
        }

        private void _objectAnchorsService_ObjectAdded(object sender, IObjectAnchorsServiceEventArgs e)
        {
            UpdateQueue.Enqueue(() => { ObjectAdded(e); });
        }

        private void ObjectAdded(IObjectAnchorsServiceEventArgs e)
        {
            AddOrUpdate(e);
        }

        private void AddOrUpdate(IObjectAnchorsServiceEventArgs e)
        {
            Debug.Log($"" +
                $"add or update\n" +
                $"Model: {e.ModelId}\n" +
                $"Instance: {e.InstanceId}\n" +
                $"Tracking Mode: {e.TrackingMode}\n" +
                $"Coverage: {e.SurfaceCoverage}\n" +
                $"Scale: {e.ScaleChange.x} {e.ScaleChange.y} {e.ScaleChange.z}\n" +
                $"Updated: {e.LastUpdatedTime}\n");

            TrackedObject to;
            if (!_instanceToTrackedObject.TryGetValue(e.InstanceId, out to))
            {
                GameObject prefab = Instantiate(TrackedObjectPrefab);

                to = prefab.GetComponent<TrackedObject>();
                if (to == null)
                {
                    to = prefab.AddComponent<TrackedObject>();
                }
                _instanceToTrackedObject.Add(e.InstanceId, to);
            }

            if (TrackingStrategy != TrackingModeStrategy.Auto && e.TrackingMode != strategyToMode[TrackingStrategy])
            {
                _objectAnchorsService.SetObjectInstanceTrackingMode(e.InstanceId, strategyToMode[TrackingStrategy]);
            }

            to.UpdateTrackedObjectData(e);
        }
#endregion

        private void TrackingStrategyUpdated()
        {
            Debug.Log($"Switching to tracking strategy {TrackingStrategy}");
            switch (TrackingStrategy)
            {
                case TrackingModeStrategy.Auto:
                    // nothing to do, the next update will take care of the tracking mode.
                    break;
                case TrackingModeStrategy.Accurate:
                case TrackingModeStrategy.Coarse:
                case TrackingModeStrategy.Pause:
                    foreach (KeyValuePair<Guid, TrackedObject> kvp in _instanceToTrackedObject)
                    {
                        _objectAnchorsService.SetObjectInstanceTrackingMode(kvp.Key, strategyToMode[TrackingStrategy]);
                    }
                    break;
            }
        }

        /// <summary>
        /// Manages tracking mode based on use visibility of an object.
        /// </summary>
        private void ManageLocatedObjectTrackingStates()
        {
            List<ObjectQuery> nextQuerySet = new List<ObjectQuery>();
            // Start with a 'refining' query for objects in the users field of view.
            foreach (KeyValuePair<Guid, TrackedObject> kvp in _instanceToTrackedObject)
            {
                if (_mainCamera.IsInFOV(kvp.Value.LogicalCenter.transform.position))
                {
                    // if the user is looking at the object, set its tracking mode to high accuracy.
                    _objectAnchorsService.SetObjectInstanceTrackingMode(kvp.Key, ObjectInstanceTrackingMode.HighLatencyAccuratePosition);
                }
                else
                {
                    // otherwise, pause tracking for the object
                    _objectAnchorsService.SetObjectInstanceTrackingMode(kvp.Key, ObjectInstanceTrackingMode.Paused);
                }
            }
        }

        /// <summary>
        /// creates queries around the user for all known models
        /// </summary>
        private void RefillGlobalQueryQueue()
        {
            Debug.Log("Prepping queries");
            // Then do a global query for any new objects
            ObjectAnchorsBoundingBox globalBoundingBox = new ObjectAnchorsBoundingBox();
            globalBoundingBox.Center = _mainCamera.transform.position;
            globalBoundingBox.Extents = Vector3.one * 5;
            globalBoundingBox.Orientation = _mainCamera.transform.rotation;
            QueueQueriesInBounds(globalBoundingBox);
        }

        /// <summary>
        /// Creates queries around the user for all known models.
        /// use to run a single set of queries to find objects not previously found
        /// </summary>
        public void StartQuery()
        {
            RefillGlobalQueryQueue();
        }

        /// <summary>
        /// Queues queries for all known models around the requested bounds
        /// </summary>
        /// <param name="queryBounds"></param>
        public async void QueueQueriesInBounds(ObjectAnchorsBoundingBox queryBounds)
        {
            List<ObjectQuery> nextQuerySet = new List<ObjectQuery>();
            SpatialGraph.SpatialGraphCoordinateSystem? coordinateSystem = null;

#if SPATIALCOORDINATESYSTEM_API_PRESENT
            var worldOrigin = ObjectAnchorsWorldManager.WorldOrigin;
            coordinateSystem = await System.Threading.Tasks.Task.Run(() => worldOrigin?.TryToSpatialGraph());
#endif

            if (!coordinateSystem.HasValue)
            {
                Debug.LogError("no coordinate system?");
                return;
            }

            foreach (TrackableObjectQuery toq in _trackableObjectQueries)
            {
                var tod = toq.TrackableObjectData;
                ObjectQuery nextQuery = toq.Query;

                nextQuery.MaxScaleChange = MaxScaleChange;

                if (tod.UseCustomParameters)
                {
                    nextQuery.MinSurfaceCoverage = tod.MinSurfaceCoverage;
                    nextQuery.IsExpectedToBeStandingOnGroundPlane = tod.IsExpectedToBeStandingOnGroundPlane;
                    nextQuery.ExpectedMaxVerticalOrientationInDegrees = tod.ExpectedMaxVerticalOrientationInDegrees;
                    nextQuery.MaxScaleChange = tod.MaxScaleChange;
                }
                else
                {
                    nextQuery.MinSurfaceCoverage = tod.MinSurfaceCoverageFromObjectModel * CoverageThresholdFactor;
                    nextQuery.ExpectedMaxVerticalOrientationInDegrees = AllowedVerticalOrientationInDegrees;
                }

                nextQuery.SearchAreas.Clear();
                nextQuery.SearchAreas.Add(ObjectSearchArea.FromOrientedBox(
                       coordinateSystem.Value,
                       queryBounds.ToSpatialGraph())
                   );

                nextQuerySet.Add(nextQuery);
            }

            _queryQueue.Enqueue(new Tuple<ObjectAnchorsBoundingBox, IEnumerable<ObjectQuery>>(queryBounds, nextQuerySet));
            Debug.Log($"{Time.frameCount} next query size {nextQuerySet.Count} query queue size {_queryQueue.Count} max scale change {MaxScaleChange} AllowedVerticalOrientationInDegrees {AllowedVerticalOrientationInDegrees}");
        }

        public IEnumerable<TrackedObject> TrackedObjects => _instanceToTrackedObject.Values;
    }
}