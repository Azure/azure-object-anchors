// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
using UnityEngine;

namespace Microsoft.Azure.ObjectAnchors.Unity.Sample
{
    /// <summary>
    /// Billboards the debug information over a tracked object.
    /// </summary>
    public class PositionDebugInfo : MonoBehaviour
    {
        private Camera _mainCamera;

        private void Start()
        {
            // Cache the main camera
            _mainCamera = Camera.main;
        }

        private void Update()
        {
            // Look at the camera
            transform.LookAt(_mainCamera.transform);

            // But look at ends up looking away, so turn 180 degrees.
            transform.Rotate(Vector3.up * 180);
        }
    }
}