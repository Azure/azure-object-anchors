// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
using System;
using UnityEngine;

namespace Microsoft.Azure.ObjectAnchors.Unity.Sample
{
    public class TrackableObjectData
    {
        // Set to true if you want to use your own model parameters, false to ignore them
        public bool UseCustomParameters = false;

        // Is the model standing on the ground
        public bool IsExpectedToBeStandingOnGroundPlane;

        private float _minSurfaceCoverage = 0.4f;

        // The score needed before there is a match. The higher the amount, the more likely the physical object is actually a match.
        // Valid range between 0 and 1
        public float MinSurfaceCoverage
        {
            get
            {
                return _minSurfaceCoverage;
            }
            set
            {
                _minSurfaceCoverage = Mathf.Clamp(value, 0, 1);
            }
        }

        /// <summary>
        /// Default score from an object model, usually is good in many cases.
        /// </summary>
        public float MinSurfaceCoverageFromObjectModel { get; set; }

        private float _expectedMaxVerticalOrientationInDegrees = 0;

        // Max vertical alignment variance in degrees
        // Valid range between 0 and 180
        public float ExpectedMaxVerticalOrientationInDegrees
        {
            get
            {
                return _expectedMaxVerticalOrientationInDegrees;
            }
            set
            {
                _expectedMaxVerticalOrientationInDegrees = Mathf.Clamp(value, 0, 180);
            }
        }

        public float _maxScaleChange = 0.1f;
        // Max scale change along object model's XYZ principle axes.
        // Valid range between 0 and 1.
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

        // Unique id of an object model.
        public Guid ModelId = Guid.Empty;

        // The full path file path to the binary data.
        public string ModelFilePath = string.Empty;

        public Mesh ModelMesh = new Mesh();

        // A bounding box that surrounds the geometry of the object
        // Allows for cases where the object's local coordinate space
        // is not around (0,0,0) or (0,floor,0)
        public ObjectAnchorsBoundingBox? logicalBoundingBox;

        public override string ToString()
        {
            return
                $"Object Data\n" +
                $"Model Id: {ModelId}\n" +
                $"Path: {ModelFilePath}\n" +
                $"Mesh Vertices: {ModelMesh.vertices.Length}\n" +
                $"Mesh indices {ModelMesh.triangles.Length}\n" +
                $"Bounding Box: {(logicalBoundingBox.HasValue ? $"\n{BoundingBoxInfo(logicalBoundingBox.Value)}" : "None")}\n" +
                $"ExpectedMaxVerticalOrientationInDegrees {ExpectedMaxVerticalOrientationInDegrees}\n" +
                $"MinSurfaceCoverage {MinSurfaceCoverage}\n" +
                $"IsExpectedToBeStandingOnGroundPlane {IsExpectedToBeStandingOnGroundPlane}\n" +
                $"UseCustomParameters {UseCustomParameters}\n" +
                $"Max Scale Change {MaxScaleChange}";
        }

        public string BoundingBoxInfo(ObjectAnchorsBoundingBox boundingBox)
        {
            return 
                $"Center: {boundingBox.Center}\n" +
                $"Extents: {boundingBox.Extents}\n" +
                $"Orientation: {boundingBox.Orientation}";
        }
    }
}