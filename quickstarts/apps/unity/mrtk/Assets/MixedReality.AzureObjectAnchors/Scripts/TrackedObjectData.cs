// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
using System;
using System.IO;
using UnityEngine;

namespace Microsoft.Azure.ObjectAnchors.Unity.Sample
{
    public class TrackedObjectData
    {
        public void UpdateTrackingData(IObjectAnchorsServiceEventArgs other)
        {
            BaseModelData = TrackableObjectDataLoader.Instance.TrackableObjectDataFromId(other.ModelId);
            Location = other.Location;
            Scale = other.ScaleChange;
            InstanceId = other.InstanceId;
            SurfaceCoverage = other.SurfaceCoverage;
            LastUpdatedTime = other.LastUpdatedTime;
            TrackingMode = other.TrackingMode;
        }

        public TrackableObjectData BaseModelData { get; set; }
        public ObjectAnchorsLocation? Location { get; set; }
        public Vector3 Scale { get; set; }

        public Guid ModelId
        {
            get
            {
                return BaseModelData.ModelId;
            }
        }

        public Guid InstanceId { get; set; }

        public float SurfaceCoverage { get; set; }

        public DateTime LastUpdatedTime { get; set; }

        public ObjectInstanceTrackingMode TrackingMode { get; set; }

        public Mesh ModelMesh
        {
            get
            {
                return BaseModelData.ModelMesh;
            }
        }

        public string ModelFileName
        {
            get
            {
                return Path.GetFileNameWithoutExtension(BaseModelData.ModelFilePath);
            }
        }

        public ObjectAnchorsBoundingBox? BaseLogicalBoundingBox
        {
            get
            {
                return BaseModelData.logicalBoundingBox;
            }
        }
    }
}