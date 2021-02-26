// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
#if UNITY_WSA
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
        ///     - Objects will attempt to be be detected in the area round the user when no objects are detected
        ///       Object detection will stop once an object has been found.
        /// Manual
        ///     - Objects will attempt to be detected when requested through the StartQuery or QueueQueriesInBounds methods
        /// </summary>
        public DetectionStrategy ActiveDetectionStrategy = DetectionStrategy.Auto;

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
            await _objectAnchorsService.InitializeAsync();
            _objectAnchorsService.Pause();
            bool hasObjects = await _trackableObjectDataLoader.LoadObjectModelsAsync(Application.persistentDataPath);
            _awaiting = false;

            // if the trackable object loader doesn't find anything, may as well give up.
            // future: add a refresh button...
            if (hasObjects)
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
        public void QueueQueriesInBounds(ObjectAnchorsBoundingBox queryBounds)
        {
            List<ObjectQuery> nextQuerySet = new List<ObjectQuery>();
            SpatialGraph.SpatialGraphCoordinateSystem? coordinateSystem = ObjectAnchorsWorldManager.GlobalCoordinateSystem;
            if (!coordinateSystem.HasValue)
            {
                Debug.LogError("no coordinate system?");
                return;
            }

            foreach (TrackableObjectData tod in _trackableObjectDataLoader.TrackableObjects)
            {
                ObjectQuery nextQuery = _objectAnchorsService.CreateObjectQuery(tod.ModelId);

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
                    nextQuery.MinSurfaceCoverage *= CoverageThresholdFactor;
                    nextQuery.ExpectedMaxVerticalOrientationInDegrees = AllowedVerticalOrientationInDegrees;
                }

                nextQuery.SearchAreas.Add(ObjectSearchArea.FromOrientedBox(
                       coordinateSystem.Value,
                       queryBounds.ToOuSdk())
                   );

                nextQuerySet.Add(nextQuery);
            }

            _queryQueue.Enqueue(new Tuple<ObjectAnchorsBoundingBox, IEnumerable<ObjectQuery>>(queryBounds, nextQuerySet));
            Debug.Log($"{Time.frameCount} next query size {nextQuerySet.Count} query queue size {_queryQueue.Count} max scale change {MaxScaleChange} AllowedVerticalOrientationInDegrees {AllowedVerticalOrientationInDegrees}");
        }

        public IEnumerable<TrackedObject> TrackedObjects => _instanceToTrackedObject.Values;
    }
}
#endif // UNITY_WSA