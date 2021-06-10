// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
using System.Linq;
using System.IO;
using UnityEditor;
using System;

namespace Microsoft.Azure.ObjectAnchors.Unity
{
    public static class Build
    {
        /// <summary>
        /// Generates a Player solution using the default configuration.
        /// </summary>
        public static void GenerateHoloLensPlayerSolution()
        {
            ConfigureCoreSettings();

            BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions()
            {
                locationPathName = "UWP",
                target = BuildTarget.WSAPlayer,
                targetGroup = BuildTargetGroup.WSA,
                options = BuildOptions.None,
                scenes = EditorBuildSettings.scenes
                         .Where(scene => scene.enabled)
                         .Select(scene => scene.path)
                         .ToArray(),
            };

            BuildPipeline.BuildPlayer(buildPlayerOptions);
        }

        public static void GenerateVisualizerSolution()
        {
            ConfigureCoreSettings();

            BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions()
            {
                locationPathName = "UWP_visual",
                target = BuildTarget.WSAPlayer,
                targetGroup = BuildTargetGroup.WSA,
                options = BuildOptions.None,
                scenes = EditorBuildSettings.scenes
                         .Where(scene => Path.GetFileName(scene.path).Equals("VisualizeScene.unity", StringComparison.OrdinalIgnoreCase))
                         .Select(scene => scene.path)
                         .ToArray(),
            };

            BuildPipeline.BuildPlayer(buildPlayerOptions);
        }

        private static void ConfigureCoreSettings()
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WSA, BuildTarget.WSAPlayer);
            EditorUserBuildSettings.SetPlatformSettings("WindowsStoreApps", "CopyReferences", "true");
            EditorUserBuildSettings.SetPlatformSettings("WindowsStoreApps", "CopyPDBFiles", "false");

            EditorUserBuildSettings.wsaUWPVisualStudioVersion = "Visual Studio 2019";
            EditorUserBuildSettings.wsaUWPSDK = "10.0.18362.0";
            EditorUserBuildSettings.wsaSubtarget = WSASubtarget.HoloLens;

            PlayerSettings.SetScriptingBackend(BuildTargetGroup.WSA, ScriptingImplementation.IL2CPP);
        }
    }
}