// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
#if UNITY_WSA
using Microsoft.Azure.ObjectAnchors.Unity;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace Microsoft.Azure.ObjectAnchors.Unity.Sample
{
    /// <summary>
    /// handles starting/stopping and uploading diagnostics
    /// </summary>
    public class ObjectTrackerDiagnostics
    {
        private static ObjectTrackerDiagnostics _instance;
        public static ObjectTrackerDiagnostics Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new ObjectTrackerDiagnostics(ObjectAnchorsService.GetService());
                }

                return _instance;
            }
        }

        private IObjectAnchorsService _objectAnchorsService;
        private Queue<string> _diagnosticsFilePaths = new Queue<string>();

        public ObjectTrackerDiagnostics(IObjectAnchorsService objectAnchorsService)
        {
            _objectAnchorsService = objectAnchorsService;
        }

        /// <summary>
        /// Start a new diagnostics session.
        /// If StopDiagnosticsSessionAsync is not called, data from previous session will be discard.
        /// </summary>
        public void StartDiagnosticsSession()
        {
            Debug.Log("Staring diagnostics session");
            _objectAnchorsService.StartDiagnosticsSession();
        }

        /// <summary>
        /// Stop a current diagnostics session. Data will written to ObjectAnchorsServiceProfile.DiagnosticsFolderPath.
        /// </summary>
        public async Task StopDiagnosticsSessionAsync()
        {
            Debug.Log("Stopping diagnostics session");
            // This method may take long to complete when the diagnostics file is large.
            var diagnosticsFilePath = await _objectAnchorsService.StopDiagnosticsSessionAsync();
            Debug.Log($"diagnostics stopped.  file at {diagnosticsFilePath}");
            if (!string.IsNullOrEmpty(diagnosticsFilePath))
            {
                _diagnosticsFilePaths.Enqueue(diagnosticsFilePath);
            }
        }

        /// <summary>
        /// Uploads outstanding diagnostics sessions
        /// </summary>
        /// <param name="subscription">class with account information</param>
        /// <returns>true if any of the sessions uploaded. false if none did.</returns>
        public async Task<bool> UploadDiagnosticsAsync()
        {
            Debug.Log("Uploading diagnostics");
            bool uploaded = false;
            Queue<string> failureQueue = new Queue<string>();
            while (_diagnosticsFilePaths.Count > 0)
            {
                string diagnosticsFilePath = _diagnosticsFilePaths.Dequeue();
                Debug.Log($"uploading {diagnosticsFilePath}");
                // Uploading may take long when the network is slow or the diagnostics file is large.
                if (await _objectAnchorsService.UploadDiagnosticsAsync(diagnosticsFilePath))
                {
                    Debug.Log($"'{diagnosticsFilePath}' has been uploaded to Object Anchors blob storage.");

                    File.Delete(diagnosticsFilePath);

                    uploaded = true;
                }
                else
                {
                    Debug.LogWarning($"failed to upload {diagnosticsFilePath}");
                    if (File.Exists(diagnosticsFilePath))
                    {
                        // requeue it if the user wants to try again later.
                        failureQueue.Enqueue(diagnosticsFilePath);
                    }
                }
            }

            _diagnosticsFilePaths = failureQueue;
            Debug.Log($"Uploading diagnostics complete. any uploads? {uploaded} remaining: {_diagnosticsFilePaths.Count}");
            return uploaded;
        }
    }
}
#endif // UNITY_WSA