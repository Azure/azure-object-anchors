// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
#if UNITY_WSA
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

#if WINDOWS_UWP
using Windows.Storage;
using Windows.Storage.Streams;
#endif // WINDOWS_UWP

namespace Microsoft.Azure.ObjectAnchors.Unity.Sample
{
    /// <summary>
    /// Loads models from the applications 'LocalState' folder
    /// </summary>
    public class TrackableObjectDataLoader : IDisposable
    {
        private static TrackableObjectDataLoader _instance;
        public static TrackableObjectDataLoader Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new TrackableObjectDataLoader();
                }

                return _instance;
            }
        }

        // A list of things that can be searched for
        private List<TrackableObjectData> _trackableObjects = new List<TrackableObjectData>();

        public IReadOnlyList<TrackableObjectData> TrackableObjects
        {
            get
            {
                return _trackableObjects.AsReadOnly();
            }
        }

        public event EventHandler<EventArgs> ModelsLoaded;

        // to avoid needing to frequently iterate to find the record with a particular ID, we maintain a dictionary
        private Dictionary<Guid, TrackableObjectData> _modelIdToTrackableObject = new Dictionary<Guid, TrackableObjectData>();

        public TrackableObjectData TrackableObjectDataFromId(Guid modelId)
        {
            TrackableObjectData result;
            _modelIdToTrackableObject.TryGetValue(modelId, out result);
            return result;
        }

        public async Task<bool> LoadObjectModelsAsync(string modelPath, ObjectObservationMode observationMode)
        {
            IObjectAnchorsService objectAnchorsService = ObjectAnchorsService.GetService();
            Debug.Log($"{Application.persistentDataPath} {objectAnchorsService != null}");
            string[] ouFiles = Directory.GetFiles(modelPath, "*.ou", SearchOption.AllDirectories);
            foreach (var file in ouFiles)
            {
                // Represent a model by TrackableObject, and load its model into OU service.
                var trackableObject = new TrackableObjectData();

                trackableObject.ModelFilePath = file.Replace('/', '\\');
                string appPath = Application.persistentDataPath.Replace('/', '\\');
                if (trackableObject.ModelFilePath.Contains(appPath))
                {
                     trackableObject.ModelId = await objectAnchorsService.AddObjectModelAsync(trackableObject.ModelFilePath);
                }
                else
                {
#if WINDOWS_UWP                    
                    byte[] buffer = await ReadFileBytesAsync(trackableObject.ModelFilePath);
                    trackableObject.ModelId = await objectAnchorsService.AddObjectModelAsync(buffer);
#endif // WINDOWS_UWP
                }

                if (trackableObject.ModelId != Guid.Empty)
                {
                    // Query the default coverage threshold from this object model.
                    trackableObject.Query = objectAnchorsService.CreateObjectQuery(trackableObject.ModelId, observationMode);
                    trackableObject.MinSurfaceCoverageFromObjectModel = trackableObject.Query.MinSurfaceCoverage;

                    trackableObject.ModelMesh = GenerateMesh(trackableObject.ModelId);
                    trackableObject.logicalBoundingBox = objectAnchorsService.GetModelBoundingBox(trackableObject.ModelId);
                    _trackableObjects.Add(trackableObject);
                    _modelIdToTrackableObject.Add(trackableObject.ModelId, trackableObject);

                    Debug.Log($"Loaded Model\n{trackableObject}");
                       
                }
                else
                {
                    Debug.LogError($"failed to load model {trackableObject.ModelFilePath}");
                }
            }
            if (_trackableObjects.Count > 0)
            {
                ModelsLoaded?.Invoke(this, EventArgs.Empty);
            }
            return _trackableObjects.Count > 0;
        }

        /// <summary>
        /// Generates a mesh from an Azure Object Anchors SDK model
        /// Flips handedness of vertices and modifies triangle list to 
        /// clockwise winding in order to be usable in Unity.
        /// </summary>
        /// <param name="modelId">The id of the model to get the mesh for</param>
        /// <returns>A mesh with the requested model's geometry</returns>
        private Mesh GenerateMesh(Guid modelId)
        {
            IObjectAnchorsService objectAnchorsService = ObjectAnchorsService.GetService();
            var vertices = objectAnchorsService.GetModelVertexPositions(modelId);
            int[] indices = (int[])(object)objectAnchorsService.GetModelTriangleIndices(modelId);
            Debug.Log($"mesh has {indices.Length} indices");
            Mesh mesh = new Mesh();
            Vector3[] unityVertices = new Vector3[vertices.Length];

            for (int k = 0; k < vertices.Length; k++)
            {
                unityVertices[k] = vertices[k].ToUnity();
            }

            mesh.vertices = unityVertices;

            if (unityVertices.Length > 65535)
            {
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }

            if (indices != null && indices.Length > 2)
            {
                for (int k = 0; k < indices.Length - 2; k += 3)
                {
                    int tmp = indices[k + 2];
                    indices[k + 2] = indices[k + 1];
                    indices[k + 1] = tmp;
                }
                mesh.SetIndices(indices, MeshTopology.Triangles, 0);
            }
            else
            {
                int[] vertexIndices = new int[vertices.Length];

                for (int k = 0; k < vertices.Length; k++)
                {
                    vertexIndices[k] = k;
                }

                mesh.SetIndices(vertexIndices, MeshTopology.Points, 0);
            }

            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            return mesh;
        }

#if WINDOWS_UWP
        private async Task<byte[]> ReadFileBytesAsync(string filePath)
        {
            StorageFile file = await StorageFile.GetFileFromPathAsync(filePath);
            if(file == null)
            {
                return null; 
            }           

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
#endif // WINDOWS_UWP

        public void Dispose()
        {
            foreach (var tod in _trackableObjects)
            {
                tod.Query.Dispose();
            }
            _trackableObjects.Clear();
            _instance = null;
        }
    }
}
#endif // UNITY_WSA