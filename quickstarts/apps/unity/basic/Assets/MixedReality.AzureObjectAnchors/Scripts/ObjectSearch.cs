// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
#if UNITY_WSA
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

using Microsoft.Azure.ObjectAnchors.Unity;

public class ObjectSearch : MonoBehaviour
{
    [Tooltip("Far distance in meter of object search frustum.")]
    public float SearchFrustumFarDistance = 4.0f;

    [Tooltip("Horizontal field of view in degrees of object search frustum.")]
    public float SearchFrustumHorizontalFovInDegrees = 75.0f;

    [Tooltip("Aspect ratio (horizontal / vertical) of object search frustum.")]
    public float SearchFrustumAspectRatio = 1.0f;

    [Tooltip("Scale on model size to deduce object search area.")]
    public float SearchAreaScaleFactor = 2.0f;

    [Tooltip("Search object in an bounding box in front of the user.")]
    public bool SearchAreaAsBoundingBox = false;

    [Tooltip("Search object in the view frustum.")]
    public bool SearchAreaAsFieldOfView = true;

    [Tooltip("Search object in a sphere in front of the user.")]
    public bool SearchAreaAsSphere = false;

    [Tooltip("Search single vs. multiple instances.")]
    public bool SearchSingleInstance = true;

    [Tooltip(@"Sentinel file in `Application\LocalCache` folder to enable capturing diagnostics.")]
    public string DiagnosticsSentinelFilename = "debug";

    [Tooltip("Material used to render a wire frame.")]
    public Material WireframeMaterial;

    /// <summary>
    /// Flag to indicate the detection operation, 0 - in detection, 1 - detection completed.
    /// </summary>
    private int _detectionCompleted = 1;

    /// <summary>
    /// Cached camera instance.
    /// </summary>
    private Camera _cachedCameraMain;

    /// <summary>
    /// Object Anchors service object.
    /// </summary>
    private IObjectAnchorsService _objectAnchorsService;

    /// <summary>
    /// Bounding box associated with each object instance with guid as instance id.
    /// </summary>
    private Dictionary<Guid, WireframeBoundingBox> _boundingBoxes = new Dictionary<Guid, WireframeBoundingBox>();

    private enum ObjectAnchorsServiceEventKind
    {
        /// <summary>
        /// Attempted to detect objects.
        /// </summary>
        DetectionAttempted,
        /// <summary>
        /// An new object is found for the first time.
        /// </summary>
        Added,
        /// <summary>
        /// State of a tracked object changed.
        /// </summary>
        Updated,
        /// <summary>
        /// An object lost tracking.
        /// </summary>
        Removed,
    }

    private class ObjectAnchorsServiceEvent
    {
        public ObjectAnchorsServiceEventKind Kind;
        public IObjectAnchorsServiceEventArgs Args;
    }

    /// <summary>
    /// A queue to cache the Object Anchors events.
    /// Events are added in the callbacks from Object Anchors service, then consumed in the Update method.
    /// </summary>
    private ConcurrentQueue<ObjectAnchorsServiceEvent> _objectAnchorsEventQueue = new ConcurrentQueue<ObjectAnchorsServiceEvent>();

    /// <summary>
    /// Returns true if diagnostics capture is enabled.
    /// </summary>
    public bool IsDiagnosticsCaptureEnabled
    {
        get
        {
            return File.Exists(Path.Combine(Application.persistentDataPath.Replace('/', '\\'), DiagnosticsSentinelFilename));
        }
    }

    private void Awake()
    {
        _objectAnchorsService = ObjectAnchorsService.GetService();

        AddObjectAnchorsListeners();
    }

    private async void Start()
    {
        await _objectAnchorsService.InitializeAsync();

        TextLogger.Log($"Object search initialized.");

        foreach (var file in FileHelper.GetFilesInDirectory(Application.persistentDataPath, "*.ou"))
        {
            TextLogger.Log($"Loading model ({Path.GetFileNameWithoutExtension(file)})");

            await _objectAnchorsService.AddObjectModelAsync(file.Replace('/', '\\'));
        }

        if (IsDiagnosticsCaptureEnabled)
        {
            TextLogger.Log($"Start capture diagnostics.");

            _objectAnchorsService.StartDiagnosticsSession();
        }
    }

    private async void OnDestroy()
    {
        _objectAnchorsService.Pause();

        await _objectAnchorsService.StopDiagnosticsSessionAsync();

        RemoveObjectAnchorsListeners();

        _objectAnchorsService.Dispose();
    }

    private void Update()
    {
        // Process current events.
        HandleObjectAnchorsServiceEvent();

        // Optionally kick off a detection if no object found yet.
        TrySearchObject();
    }

    private void AddObjectAnchorsListeners()
    {
        _objectAnchorsService.RunningChanged += ObjectAnchorsService_RunningChanged;
        _objectAnchorsService.ObjectAdded += ObjectAnchorsService_ObjectAdded;
        _objectAnchorsService.ObjectUpdated += ObjectAnchorsService_ObjectUpdated;
        _objectAnchorsService.ObjectRemoved += ObjectAnchorsService_ObjectRemoved;
    }

    private void RemoveObjectAnchorsListeners()
    {
        _objectAnchorsService.RunningChanged -= ObjectAnchorsService_RunningChanged;
        _objectAnchorsService.ObjectAdded -= ObjectAnchorsService_ObjectAdded;
        _objectAnchorsService.ObjectUpdated -= ObjectAnchorsService_ObjectUpdated;
        _objectAnchorsService.ObjectRemoved -= ObjectAnchorsService_ObjectRemoved;
    }

    private void ObjectAnchorsService_RunningChanged(object sender, ObjectAnchorsServiceStatus status)
    {
        TextLogger.Log($"Object search {status}");
    }

    private void ObjectAnchorsService_ObjectAdded(object sender, IObjectAnchorsServiceEventArgs args)
    {
        // This event handler is called from a non-UI thread.

        _objectAnchorsEventQueue.Enqueue(
            new ObjectAnchorsServiceEvent
            {
                Kind = ObjectAnchorsServiceEventKind.Added,
                Args = args
            });
    }

    private void ObjectAnchorsService_ObjectUpdated(object sender, IObjectAnchorsServiceEventArgs args)
    {
        // This event handler is called from a non-UI thread.

        _objectAnchorsEventQueue.Enqueue(
             new ObjectAnchorsServiceEvent
             {
                 Kind = ObjectAnchorsServiceEventKind.Updated,
                 Args = args
             });
    }

    private void ObjectAnchorsService_ObjectRemoved(object sender, IObjectAnchorsServiceEventArgs args)
    {
        // This event handler is called from a non-UI thread.

        _objectAnchorsEventQueue.Enqueue(
            new ObjectAnchorsServiceEvent
            {
                Kind = ObjectAnchorsServiceEventKind.Removed,
                Args = args
            });
    }

    private void HandleObjectAnchorsServiceEvent()
    {
        Func<IObjectAnchorsServiceEventArgs, string> EventArgsFormatter = args =>
            {
                return $"[{args.LastUpdatedTime.ToLongTimeString()}] ${TextLogger.Truncate(args.InstanceId.ToString(), 5)}";
            };

        ObjectAnchorsServiceEvent _event;
        while (_objectAnchorsEventQueue.TryDequeue(out _event))
        {
            switch (_event.Kind)
            {
                case ObjectAnchorsServiceEventKind.DetectionAttempted:
                    {
                        TextLogger.Log($"detection attempted");
                        break;
                    }
                case ObjectAnchorsServiceEventKind.Added:
                    {
                        TextLogger.LogRaw($"{EventArgsFormatter(_event.Args)} found, coverage {_event.Args.SurfaceCoverage.ToString("0.00")}");

                        DrawBoundingBox(_event.Args);
                        break;
                    }
                case ObjectAnchorsServiceEventKind.Updated:
                    {
                        TextLogger.LogRaw($"{EventArgsFormatter(_event.Args)} updated, coverage {_event.Args.SurfaceCoverage.ToString("0.00")}");

                        DrawBoundingBox(_event.Args);
                        break;
                    }
                case ObjectAnchorsServiceEventKind.Removed:
                    {
                        TextLogger.LogRaw($"{EventArgsFormatter(_event.Args)} removed");

                        var bbox = _boundingBoxes[_event.Args.InstanceId];
                        _boundingBoxes.Remove(_event.Args.InstanceId);

                        bbox.gameObject.SetActive(false);
                        DestroyImmediate(bbox);

                        break;
                    }
            }
        }
    }

    private void DrawBoundingBox(IObjectAnchorsServiceEventArgs instance)
    {
        WireframeBoundingBox bbox;
        if (!_boundingBoxes.TryGetValue(instance.InstanceId, out bbox))
        {
            var boundingBox = _objectAnchorsService.GetModelBoundingBox(instance.ModelId);
            Debug.Assert(boundingBox.HasValue);

            bbox = new GameObject("Bounding Box").AddComponent<WireframeBoundingBox>();
            bbox.transform.SetParent(transform, true);
            bbox.gameObject.SetActive(false);
            bbox.UpdateBounds(boundingBox.Value.Center, Vector3.Scale(boundingBox.Value.Extents, instance.ScaleChange), boundingBox.Value.Orientation, WireframeMaterial);

            _boundingBoxes.Add(instance.InstanceId, bbox);
        }
        else
        {
            bbox.gameObject.SetActive(false);
        }

        var location = instance.Location;
        if (location.HasValue)
        {
            bbox.transform.SetPositionAndRotation(location.Value.Position, location.Value.Orientation);
            bbox.gameObject.SetActive(true);
        }
    }

    private void TrySearchObject()
    {
        if (Interlocked.CompareExchange(ref _detectionCompleted, 0, 1) == 1)
        {
            if (_cachedCameraMain == null)
            {
                _cachedCameraMain = Camera.main;
            }

            var cameraLocation = new ObjectAnchorsLocation
            {
                Position = _cachedCameraMain.transform.position,
                Orientation = _cachedCameraMain.transform.rotation,
            };

            var coordinateSystem = ObjectAnchorsWorldManager.GlobalCoordinateSystem;

            Task.Run(async () =>
            {
                try
                {
                    await DetectObjectAsync(coordinateSystem, cameraLocation);
                }
                catch (Exception ex)
                {
                    UnityEngine.WSA.Application.InvokeOnAppThread(() =>
                    {
                        TextLogger.Log($"Detection failed. Exception message: {ex.ToString()}");

                    }, false);
                }

                Interlocked.CompareExchange(ref _detectionCompleted, 1, 0);
            });
        }
    }

    private Task DetectObjectAsync(Microsoft.Azure.ObjectAnchors.SpatialGraph.SpatialGraphCoordinateSystem? coordinateSystem, ObjectAnchorsLocation cameraLocation)
    {
        //
        // Coordinate system may not be available at this time, try it later.
        //

        if (!coordinateSystem.HasValue)
        {
            return Task.CompletedTask;
        }

        //
        // Get camera location and coordinate system.
        //

        var cameraForward = cameraLocation.Orientation * Vector3.forward;
        var estimatedTargetLocation = new ObjectAnchorsLocation
        {
            Position = cameraLocation.Position + cameraForward * SearchFrustumFarDistance * 0.5f,
            Orientation = Quaternion.Euler(0.0f, cameraLocation.Orientation.eulerAngles.y, 0.0f),
        };

        //
        // Remove detected objects far away from the camera.
        //

        foreach (var instance in _objectAnchorsService.TrackingResults)
        {
            var location = instance.Location;
            if (location.HasValue)
            {
                var modelBbox = _objectAnchorsService.GetModelBoundingBox(instance.ModelId);
                Debug.Assert(modelBbox.HasValue);

                // Compute the coordinate of instance bounding box center in Unity world.
                var instancePosition = location.Value.Position + location.Value.Orientation * modelBbox.Value.Center;

                var offset = instancePosition - cameraLocation.Position;

                if (offset.magnitude > SearchFrustumFarDistance * 1.5f)
                {
                    _objectAnchorsService.RemoveObjectInstance(instance.InstanceId);
                }
            }
        }

        //
        // Detect object(s) in field of view, bounding box, or sphere.
        //

        var objectQueries = new List<Microsoft.Azure.ObjectAnchors.ObjectQuery>();

        var trackingResults = _objectAnchorsService.TrackingResults;

        foreach (var modelId in _objectAnchorsService.ModelIds)
        {
            //
            // Optionally skip a model detection if an instance is already found.
            //

            if(SearchSingleInstance)
            {
                if(trackingResults.Where(r => r.ModelId == modelId).Count() > 0)
                {
                    continue;
                }
            }

            var modelBox = _objectAnchorsService.GetModelBoundingBox(modelId);
            Debug.Assert(modelBox.HasValue);

            //
            // Create a query and set the parameters.
            //

            var query = _objectAnchorsService.CreateObjectQuery(modelId);
            Debug.Assert(query != null);

            if (SearchAreaAsBoundingBox)
            {
                // Adapt bounding box size to model size. Note that Extents.z is model's height.
                float modelXYSize = new Vector2(modelBox.Value.Extents.x, modelBox.Value.Extents.y).magnitude;

                var boundingBox = new ObjectAnchorsBoundingBox
                {
                    Center = estimatedTargetLocation.Position,
                    Orientation = estimatedTargetLocation.Orientation,
                    Extents = new Vector3(modelXYSize * SearchAreaScaleFactor, modelBox.Value.Extents.z * SearchAreaScaleFactor, modelXYSize * SearchAreaScaleFactor),
                };

                query.SearchAreas.Add(
                    Microsoft.Azure.ObjectAnchors.ObjectSearchArea.FromOrientedBox(
                        coordinateSystem.Value,
                        boundingBox.ToOuSdk()));
            }

            if (SearchAreaAsFieldOfView)
            {
                var fieldOfView = new ObjectAnchorsFieldOfView
                {
                    Position = cameraLocation.Position,
                    Orientation = cameraLocation.Orientation,
                    FarDistance = SearchFrustumFarDistance,
                    HorizontalFieldOfViewInDegrees = SearchFrustumHorizontalFovInDegrees,
                    AspectRatio = SearchFrustumAspectRatio,
                };

                query.SearchAreas.Add(
                    Microsoft.Azure.ObjectAnchors.ObjectSearchArea.FromFieldOfView(
                        coordinateSystem.Value,
                        fieldOfView.ToOuSdk()));
            }

            if (SearchAreaAsSphere)
            {
                // Adapt sphere radius to model size.
                float modelDiagonalSize = modelBox.Value.Extents.magnitude;

                var sphere = new ObjectAnchorsSphere
                {
                    Center = estimatedTargetLocation.Position,
                    Radius = modelDiagonalSize * 0.5f * SearchAreaScaleFactor,
                };

                query.SearchAreas.Add(
                    Microsoft.Azure.ObjectAnchors.ObjectSearchArea.FromSphere(
                        coordinateSystem.Value,
                        sphere.ToOuSdk()));
            }

            objectQueries.Add(query);
        }

        //
        // Pause a while if detection is not required.
        //

        if (objectQueries.Count == 0)
        {
            Thread.Sleep(100);

            return Task.CompletedTask;
        }

        //
        // Run detection.
        //

        // Add event to the queue.
        _objectAnchorsEventQueue.Enqueue(
           new ObjectAnchorsServiceEvent
           {
               Kind = ObjectAnchorsServiceEventKind.DetectionAttempted,
               Args = null
           });

        return _objectAnchorsService.DetectObjectAsync(objectQueries.ToArray());
    }

}
#endif
