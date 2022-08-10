// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.

using System;
using System.Numerics;
using System.Text.Json.Serialization;
using Azure.MixedReality.ObjectAnchors.Conversion;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ConversionQuickstart
{
    public class Configuration
    {
        // Azure Object Anchors account identifier
        public string AccountId = "set-me";

        // Azure Object Anchors account primary key
        public string AccountKey = "set-me";

        // Azure Object Anchors account domain
        public string AccountDomain = "set-me";

        // Path to 3D asset file on your local machine
        public string InputAssetPath = "set-me";

        // Timeout to wait for job completion
        public TimeSpan WaitForJobCompletionTimeout = TimeSpan.FromMinutes(40);

        // Gravity direction of 3D model
        public Vector3 Gravity = new Vector3(0.0f, -1.0f, 0.0f);

        // The unit of measurement of the 3D model
        public AssetLengthUnit AssetDimensionUnit = AssetLengthUnit.Meters;

        [JsonIgnore]
        public ILogger Logger = NullLogger.Instance;
    }
}
