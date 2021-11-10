// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
using System;
using System.IO;
using System.Linq;
using UnityEditor;

namespace Microsoft.Azure.ObjectAnchors.Unity
{
    public static class Build
    {
        /// <summary>
        /// Generates a Player solution using the default configuration.
        /// </summary>
        public static void GenerateHoloLensPlayerSolutionForMRTKApp()
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WSA, BuildTarget.WSAPlayer);
            EditorUserBuildSettings.SetPlatformSettings("WindowsStoreApps", "CopyReferences", "true");
            EditorUserBuildSettings.SetPlatformSettings("WindowsStoreApps", "CopyPDBFiles", "false");

            EditorUserBuildSettings.wsaUWPVisualStudioVersion = "Visual Studio 2019";
            EditorUserBuildSettings.wsaUWPSDK = "10.0.18362.0";
            EditorUserBuildSettings.wsaSubtarget = WSASubtarget.HoloLens;

            PlayerSettings.SetScriptingBackend(BuildTargetGroup.WSA, ScriptingImplementation.IL2CPP);
            PlayerSettings.productName = "AOAMRTKApp";
            PlayerSettings.WSA.packageName = "AOAMRTKApp";
            PlayerSettings.WSA.applicationDescription = "AOA MRTK sample application.";
            
            BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions()
            {
                locationPathName = "UWP_mrtk",
                target = BuildTarget.WSAPlayer,
                targetGroup = BuildTargetGroup.WSA,
                options = BuildOptions.None,
                scenes = EditorBuildSettings.scenes
                         .Where(scene => scene.enabled && string.Equals(Path.GetFileName(scene.path), "AOASampleScene.unity", StringComparison.OrdinalIgnoreCase))
                         .Select(scene => scene.path)
                         .ToArray()
            };

            BuildPipeline.BuildPlayer(buildPlayerOptions);
        }
    }
}