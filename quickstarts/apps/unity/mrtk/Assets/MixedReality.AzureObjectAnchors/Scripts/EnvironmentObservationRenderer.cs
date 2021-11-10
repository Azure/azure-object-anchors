// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
#if WINDOWS_UWP || DOTNETWINRT_PRESENT
#define SPATIALCOORDINATESYSTEM_API_PRESENT
#endif

using Microsoft.Azure.ObjectAnchors;
using Microsoft.Azure.ObjectAnchors.Unity;
using System;
using System.Threading.Tasks;
using UnityEngine;

public class EnvironmentObservationRenderer : MonoBehaviour
{
    /// <summary>
    /// The query which will provide the environment observations to render.
    /// </summary>
    [HideInInspector]
    public ObjectQuery Query;

    /// <summary>
    /// The topology to use when rendering the environment.
    /// </summary>
    [HideInInspector]
    public EnvironmentObservationTopology EnvironmentTopology;

    /// <summary>
    /// The material to use when the environment is rendered as a point cloud.
    /// </summary>
    public Material PointCloudMaterial;

    private DateTime _lastMeshUpdateTime = DateTime.MinValue;
    private bool _updateInProgress = false;
    private Mesh _mesh;

    private void Start()
    {
        _mesh = GetComponent<MeshFilter>().mesh;
        if (EnvironmentTopology == EnvironmentObservationTopology.PointCloud)
        {
            GetComponent<MeshRenderer>().material = PointCloudMaterial;
        }
    }

    async void Update()
    {
        if (!_updateInProgress && (DateTime.Now - _lastMeshUpdateTime) > TimeSpan.FromSeconds(2))
        {
            _updateInProgress = true;
            EnvironmentObservation observation = await Query.ComputeLatestEnvironmentObservationAsync(EnvironmentTopology);
#if SPATIALCOORDINATESYSTEM_API_PRESENT
            ObjectAnchorsLocation? observationLocation = await _mesh.LocateAndSetFromEnvironmentObservation(observation);
            if (observationLocation.HasValue)
            {
                transform.SetPositionAndRotation(observationLocation.Value.Position, observationLocation.Value.Orientation);
            }
#endif

            _lastMeshUpdateTime = DateTime.Now;
            _updateInProgress = false;
        }
    }
}
