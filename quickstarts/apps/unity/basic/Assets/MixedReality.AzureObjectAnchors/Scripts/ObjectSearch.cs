// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
#if WINDOWS_UWP || DOTNETWINRT_PRESENT
#define SPATIALCOORDINATESYSTEM_API_PRESENT
#endif

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

using Microsoft.Azure.ObjectAnchors.Unity;

#if WINDOWS_UWP
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.Storage.Search;
#endif

public class ObjectSearch : MonoBehaviour
{
    public enum SearchAreaKind { Box, FieldOfView, Sphere };

    [Tooltip("Far distance in meter of object search frustum.")]
    public float SearchFrustumFarDistance = 4.0f;

    [Tooltip("Horizontal field of view in degrees of object search frustum.")]
    public float SearchFrustumHorizontalFovInDegrees = 75.0f;

    [Tooltip("Aspect ratio (horizontal / vertical) of object search frustum.")]
    public float SearchFrustumAspectRatio = 1.0f;

    [Tooltip("Scale on model size to deduce object search area.")]
    public float SearchAreaScaleFactor = 2.0f;

    [Tooltip("Search area shape.")]
    public SearchAreaKind SearchAreaShape = SearchAreaKind.Box;

    [Tooltip("Observation mode.")]
    public Microsoft.Azure.ObjectAnchors.ObjectObservationMode ObservationMode = Microsoft.Azure.ObjectAnchors.ObjectObservationMode.Ambient;

    [Tooltip("Show environment observations.")]
    public bool ShowEnvironmentObservations = false;

    [Tooltip("Search single vs. multiple instances.")]
    public bool SearchSingleInstance = true;

    [Tooltip(@"Sentinel file in `Application\LocalCache` folder to enable capturing diagnostics.")]
    public string DiagnosticsSentinelFilename = "debug";

    [Tooltip("Material used to render a wire frame.")]
    public Material WireframeMaterial;

    [Tooltip("Material used to render the environment.")]
    public Material EnvironmentMaterial;
    
    [Tooltip("Prefab used to determine placement of detected objects.")]
    public GameObject MultiAnchorPlacementPrefab;

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
    /// Placement of each object instance with guid as instance id.
    /// </summary>
    private Dictionary<Guid, MultiAnchorObjectPlacement> _objectPlacements = new Dictionary<Guid, MultiAnchorObjectPlacement>();

    /// <summary>
    /// Query associated with each model with guid as model id.
    /// </summary>
    private Dictionary<Guid, ObjectQueryState> _objectQueries = new Dictionary<Guid, ObjectQueryState>();
    private Dictionary<Guid, ObjectQueryState> InitializeObjectQueries()
    {
        var objectQueries = new Dictionary<Guid, ObjectQueryState>();

        foreach (var modelId in _objectAnchorsService.ModelIds)
        {
            //
            // Create a query and set the parameters.
            //

            var queryState = new GameObject($"ObjectQueryState for model {modelId}").AddComponent<ObjectQueryState>();
            queryState.Query = _objectAnchorsService.CreateObjectQuery(modelId, ObservationMode);
            if (ShowEnvironmentObservations)
            {
                queryState.EnvironmentMaterial = EnvironmentMaterial;
            }

            objectQueries.Add(modelId, queryState);
        }

        return objectQueries;
    }

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
        try
        {
            await _objectAnchorsService.InitializeAsync();
        }
        catch (System.ArgumentException ex)
        {
#if WINDOWS_UWP
            string message = ex.Message;
            Windows.Foundation.IAsyncOperation<Windows.UI.Popups.IUICommand> dialog = null;
            UnityEngine.WSA.Application.InvokeOnUIThread(() => dialog = new Windows.UI.Popups.MessageDialog(message, "Invalid account information").ShowAsync(), true);
            await dialog;
#elif UNITY_EDITOR
            UnityEditor.EditorUtility.DisplayDialog("Invaild account information", ex.Message, "OK");
#endif // WINDOWS_UWP
            throw ex;
        }

        TextLogger.Log($"Object search initialized.");

        foreach (var file in FileHelper.GetFilesInDirectory(Application.persistentDataPath, "*.ou"))
        {
            TextLogger.Log($"Loading model ({Path.GetFileNameWithoutExtension(file)})");

            await _objectAnchorsService.AddObjectModelAsync(file.Replace('/', '\\'));
        }


#if WINDOWS_UWP

        // Accessing a known but protected folder only works when using the StorageFolder/StorageFile apis
        // and not the System.IO apis. On some devices, the static StorageFolder for well known folders
        // like the 3d objects folder returns access denied when queried, but behaves as expected when
        // accessed by path. Frustratingly, on some devices the opposite is true, and the static StorageFolder 
        // works and the workaround finds no files.
        StorageFolder objects3d = KnownFolders.Objects3D;

        // First try using the static folder directly, which will throw an exception on some devices
        try
        {
            foreach (string filePath in FileHelper.GetFilesInDirectory(objects3d.Path, "*.ou"))
            {
                TextLogger.Log($"Loading model ({Path.GetFileNameWithoutExtension(filePath)})");
                byte[] buffer =  await ReadFileBytesAsync(filePath);
                await _objectAnchorsService.AddObjectModelAsync(buffer);
            }
        }
        catch(UnauthorizedAccessException ex)
        {
            Debug.Log("access denied to objects 3d folder. Trying through path");
            StorageFolder objects3dAcc = await StorageFolder.GetFolderFromPathAsync(objects3d.Path);
            foreach(StorageFile file in await objects3dAcc.GetFilesAsync(CommonFileQuery.OrderByName))
            {
                if (Path.GetExtension(file.Name) == ".ou")
                {
                    TextLogger.Log($"Loading model ({file.Path} {file.Name}");
                    byte[] buffer =  await ReadFileBytesAsync(file);
                    await _objectAnchorsService.AddObjectModelAsync(buffer);
                }
            }
        }
        catch(Exception ex)
        {
            Debug.LogWarning("unexpected exception accessing objects 3d folder");
            Debug.LogException(ex);
        }
#endif

        _objectQueries = InitializeObjectQueries();

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

        lock (_objectQueries)
        {
            foreach (var query in _objectQueries)
            {
                if (query.Value != null)
                {
                    Destroy(query.Value.gameObject);
                }
            }
            _objectQueries.Clear();
        }

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

                        var placement = _objectPlacements[_event.Args.InstanceId];
                        _objectPlacements.Remove(_event.Args.InstanceId);

                        Destroy(placement.gameObject);

                        break;
                    }
            }
        }
    }

    private void DrawBoundingBox(IObjectAnchorsServiceEventArgs instance)
    {
        MultiAnchorObjectPlacement placement;
        if (!_objectPlacements.TryGetValue(instance.InstanceId, out placement))
        {
            var boundingBox = _objectAnchorsService.GetModelBoundingBox(instance.ModelId);
            Debug.Assert(boundingBox.HasValue);

            placement = Instantiate(MultiAnchorPlacementPrefab).GetComponent<MultiAnchorObjectPlacement>();

            var bbox = placement.ModelSpaceContent.AddComponent<WireframeBoundingBox>();
            bbox.UpdateBounds(boundingBox.Value.Center, Vector3.Scale(boundingBox.Value.Extents, instance.ScaleChange), boundingBox.Value.Orientation, WireframeMaterial);

            var mesh = new GameObject("Model Mesh");
            mesh.AddComponent<MeshRenderer>().sharedMaterial = WireframeMaterial;
            mesh.transform.SetParent(placement.ModelSpaceContent.transform, false);
            MeshLoader.AddMesh(mesh, _objectAnchorsService, instance.ModelId);

            _objectPlacements.Add(instance.InstanceId, placement);
        }

        if (instance.SurfaceCoverage > placement.SurfaceCoverage ||
            !instance.Location.HasValue)
        {
            placement.UpdatePlacement(instance);
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

#if SPATIALCOORDINATESYSTEM_API_PRESENT
            var coordinateSystem = ObjectAnchorsWorldManager.WorldOrigin;

            Task.Run(async () =>
            {
                try
                {
                    await DetectObjectAsync(coordinateSystem.TryToSpatialGraph(), cameraLocation);
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
#endif
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

        lock (_objectQueries)
        foreach (var objectQuery in _objectQueries)
        {
            var modelId = objectQuery.Key;
            var query = objectQuery.Value.Query;

            //
            // Optionally skip a model detection if an instance is already found.
            //

            if (SearchSingleInstance)
            {
                if (trackingResults.Where(r => r.ModelId == modelId).Count() > 0)
                {
                    continue;
                }
            }

            var modelBox = _objectAnchorsService.GetModelBoundingBox(modelId);
            Debug.Assert(modelBox.HasValue);

            query.SearchAreas.Clear();
            switch (SearchAreaShape)
            {
                case SearchAreaKind.Box:
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
                            boundingBox.ToSpatialGraph()));
                    break;
                }

                case SearchAreaKind.FieldOfView:
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
                            fieldOfView.ToSpatialGraph()));
                    break;
                }

                case SearchAreaKind.Sphere:
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
                            sphere.ToSpatialGraph()));
                    break;
                }
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

#if WINDOWS_UWP
    private async Task<byte[]> ReadFileBytesAsync(string filePath)
    {
        StorageFile file = await StorageFile.GetFileFromPathAsync(filePath);
        if (file == null)
        {
            return null; 
        }

        return await ReadFileBytesAsync(file);
    }    

    private async Task<byte[]> ReadFileBytesAsync(StorageFile file)
    {
        using (IRandomAccessStream stream = await file.OpenReadAsync())
        {
            using (var reader = new DataReader(stream.GetInputStreamAt(0)))
            {
                await reader.LoadAsync((uint)stream.Size);
                var bytes = new byte[stream.Size];
                reader.ReadBytes(bytes);
                return bytes;
            }
        }
    }
#endif

}
