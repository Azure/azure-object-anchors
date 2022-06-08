// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

#if WINDOWS_UWP
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.Storage.Search;
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

#if WINDOWS_UWP
        public async Task<bool> LoadObjectModelsFromWellKnownFolderAsync(string wellKnownFolderPath)
        {
            IObjectAnchorsService objectAnchorsService = ObjectAnchorsService.GetService();
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
                    Debug.Log($"Loading model ({Path.GetFileNameWithoutExtension(filePath)})");
                    byte[] buffer =  await ReadFileBytesAsync(filePath);
                        
                    // Represent a model by TrackableObject, and load its model into OU service.
                    var trackableObject = new TrackableObjectData();
                    trackableObject.ModelFilePath = filePath.Replace('/', '\\');
                    trackableObject.ModelId = await objectAnchorsService.AddObjectModelAsync(buffer);

                    if (trackableObject.ModelId != Guid.Empty)
                    {
                        await FillTrackableObjectData(trackableObject);
                    }
                    else
                    {
                        Debug.LogError($"failed to load model {trackableObject.ModelFilePath}");
                    }
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
                        Debug.Log($"Loading model ({file.Path} {file.Name}");
                        byte[] buffer =  await ReadFileBytesAsync(file);
                        // Represent a model by TrackableObject, and load its model into OU service.
                        var trackableObject = new TrackableObjectData();
                        trackableObject.ModelFilePath = file.Path.Replace('/', '\\');
                        trackableObject.ModelId = await objectAnchorsService.AddObjectModelAsync(buffer);

                        if (trackableObject.ModelId != Guid.Empty)
                        {
                            await FillTrackableObjectData(trackableObject);
                        }
                        else
                        {
                            Debug.LogError($"failed to load model {trackableObject.ModelFilePath}");
                        }
                    }
                }
            }
            catch(Exception ex)
            {
                Debug.LogWarning("unexpected exception accessing objects 3d folder");
                Debug.LogException(ex);
                return false;
            }

            if (_trackableObjects.Count > 0)
            {
                ModelsLoaded?.Invoke(this, EventArgs.Empty);
            }
            return _trackableObjects.Count > 0;
        }
#endif
        public async Task<bool> LoadObjectModelsAsync(string modelPath)
        {
            try
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
                       await FillTrackableObjectData(trackableObject);
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
            catch(Exception ex)
            {
                Debug.LogException(ex);
            }

            return false;
        }

        private async Task FillTrackableObjectData(TrackableObjectData trackableObject)
        {
            IObjectAnchorsService objectAnchorsService = ObjectAnchorsService.GetService();
            trackableObject.ModelMesh = new Mesh();
            await trackableObject.ModelMesh.SetFromObjectModel(trackableObject.ModelId);
            Debug.Log($"mesh has {trackableObject.ModelMesh.triangles.Length} indices");

            trackableObject.logicalBoundingBox = objectAnchorsService.GetModelBoundingBox(trackableObject.ModelId);
            _trackableObjects.Add(trackableObject);
            _modelIdToTrackableObject.Add(trackableObject.ModelId, trackableObject);

            Debug.Log($"Loaded Model\n{trackableObject}");
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
#endif // WINDOWS_UWP

        public void Dispose()
        {
            _trackableObjects.Clear();
            _instance = null;
        }
    }
}