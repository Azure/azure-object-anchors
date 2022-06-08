// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
using UnityEngine;
using UnityEngine.UI;

namespace Microsoft.Azure.ObjectAnchors.Unity.Sample
{
    /// <summary>
    /// Controls rendering global status information
    /// </summary>
    public class OverlayDebugText : MonoBehaviour
    {
        public Text TextField;

        private IObjectAnchorsService _objectAnchorsService;
        private ObjectTracker _objectTracker;

        private void Start()
        {
            _objectAnchorsService = ObjectAnchorsService.GetService();
            _objectTracker = FindObjectOfType<ObjectTracker>();
        }

        private void Update()
        {
            // Update the text at less than full FPS to reduce string garbage.
            if (Time.frameCount % 5 == 0)
            {
                UpdateText();
            }
        }

        private void UpdateText()
        {
            TextField.text =
                $"AOA Status: {_objectAnchorsService.Status}\n" +
                $"Tracked: {_objectAnchorsService.TrackingResults.Count}\n" +
                $"Models: {_objectAnchorsService.ModelIds.Count}\n" +
                $"Querying: {_objectTracker.QueryActive}";
        }
    }
}
