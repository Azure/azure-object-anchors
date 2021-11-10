// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
#if WINDOWS_UWP || DOTNETWINRT_PRESENT
#define SPATIALCOORDINATESYSTEM_API_PRESENT
#endif

using UnityEngine;
using Numerics = System.Numerics;

using Microsoft.Azure.ObjectAnchors.Unity;
using System.Threading.Tasks;
using System;
using Microsoft.Azure.ObjectAnchors;

public static class MeshLoader
{
    // System-provided coordinates are right-handed, and triangle indices are counter-clockwise.
    struct SystemMeshData
    {
        public Numerics.Vector3[] Vertices;
        public Numerics.Vector3[] Normals;
        public uint[] Indices;

        public static SystemMeshData FromObjectModel(Guid modelId)
        {
#if UNITY_WSA
            var objectAnchorsService = ObjectAnchorsService.GetService();
            return new SystemMeshData()
            {
                Vertices = objectAnchorsService.GetModelVertexPositions(modelId),
                Normals = objectAnchorsService.GetModelVertexNormals(modelId),
                Indices = objectAnchorsService.GetModelTriangleIndices(modelId)
            };
#else
            return new SystemMeshData();
#endif
        }

        public static SystemMeshData FromEnvironmentObseration(EnvironmentObservation observation)
        {
            var meshData = new SystemMeshData()
            {
                Vertices = new Numerics.Vector3[observation.VertexCount],
                Normals = new Numerics.Vector3[observation.VertexCount],
                Indices = new uint[observation.TriangleIndexCount]
            };
#if UNITY_WSA
            observation.GetVertexPositions(meshData.Vertices);
            observation.GetVertexNormals(meshData.Normals);
            observation.GetTriangleIndices(meshData.Indices);
#endif
            return meshData;
        }
    }

    // Unity coordinates are left-handed, and triangle indices are clockwise.
    struct UnityMeshData
    {
        public Vector3[] Vertices;
        public Vector3[] Normals;
        public int[] Indices;
        public MeshTopology Topology;

        // We need to flip handedness of vertices and modify triangle list to
        // clockwise winding in order to be usable in Unity.
        public static Task<UnityMeshData> FromSystemDataAsync(Func<SystemMeshData> getMeshData)
        {
            return Task.Run(() =>
            {
                SystemMeshData systemMeshData = getMeshData();
                if (systemMeshData.Vertices.Length != systemMeshData.Normals.Length)
                {
                    throw new System.ArgumentException($"Count of normals ({systemMeshData.Normals.Length}) does not match count of vertices ({systemMeshData.Vertices.Length})", "systemMeshData.Normals");
                }

                UnityMeshData meshData;
                meshData.Vertices = new Vector3[systemMeshData.Vertices.Length];
                meshData.Normals = new Vector3[systemMeshData.Vertices.Length];
                for (uint i = 0; i < systemMeshData.Vertices.Length; ++i)
                {
                    meshData.Vertices[i] = systemMeshData.Vertices[i].ToUnity();
                    meshData.Normals[i] = systemMeshData.Normals[i].ToUnity();
                }

                if (systemMeshData.Indices != null && systemMeshData.Indices.Length != 0)
                {
                    if (systemMeshData.Indices.Length % 3 != 0)
                    {
                        throw new System.ArgumentException($"Count of triangle indices ({systemMeshData.Indices.Length}) is not a multiple of three", "systemMeshData.Indices");
                    }

                    // Clock wise
                    meshData.Indices = new int[systemMeshData.Indices.Length];
                    for (uint i = 0; i < systemMeshData.Indices.Length; i += 3)
                    {
                        meshData.Indices[i + 0] = (int)systemMeshData.Indices[i + 2];
                        meshData.Indices[i + 1] = (int)systemMeshData.Indices[i + 1];
                        meshData.Indices[i + 2] = (int)systemMeshData.Indices[i + 0];
                    }

                    meshData.Topology = MeshTopology.Triangles;
                }
                else
                {
                    meshData.Indices = new int[systemMeshData.Vertices.Length];
                    for (int i = 0; i < systemMeshData.Vertices.Length; ++i)
                    {
                        meshData.Indices[i] = i;
                    }
                    meshData.Topology = MeshTopology.Points;
                }

                return meshData;
            });
        }
    }

    static async Task SetFromSystemMeshDataAsync(this Mesh mesh, Func<SystemMeshData> getMeshData)
    {
        var meshData = await UnityMeshData.FromSystemDataAsync(getMeshData);
        mesh.Clear();

        mesh.vertices = meshData.Vertices;
        if (mesh.vertices.Length > ushort.MaxValue)
        {
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        }

        mesh.SetIndices(meshData.Indices, meshData.Topology, 0);
        mesh.normals = meshData.Normals;
        mesh.RecalculateBounds();
    }

    public static Task SetFromObjectModel(this Mesh mesh, Guid modelId)
    {
        return mesh.SetFromSystemMeshDataAsync(() => SystemMeshData.FromObjectModel(modelId));
    }

#if SPATIALCOORDINATESYSTEM_API_PRESENT
    public static async Task<ObjectAnchorsLocation?> LocateAndSetFromEnvironmentObservation(this Mesh mesh, EnvironmentObservation observation)
    {
        ObjectAnchorsLocation? observationLocation = null;
        await mesh.SetFromSystemMeshDataAsync(() =>
        {
            observationLocation = observation.Origin.ToSpatialCoordinateSystem().TryGetTransformTo(ObjectAnchorsWorldManager.WorldOrigin)?.ToUnityLocation();
            return SystemMeshData.FromEnvironmentObseration(observation);
        });
        return observationLocation;
    }
#endif
}
