// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
using UnityEngine;

using Microsoft.Azure.ObjectAnchors.Unity;

public class AppPaused : MonoBehaviour
{
    void OnApplicationFocus(bool focus)
    {
        var service = ObjectAnchorsService.GetService();

        if (focus)
        {
            service.Resume();

            if (GetComponent<ObjectSearch>().IsDiagnosticsCaptureEnabled)
            {
                TextLogger.Log("Start capture diagnostics.");

                service.StartDiagnosticsSession();
            }
        }
        else
        {
            // Pause could block the UI for a very short time, application can put it
            // into a coroutine or background task.
            service.Pause();

            // This method can run asynchronously if the Unity app supports background running.
            // Here we wait until the diagnostics data has been fully committed to the storage since this app
            // doesn't support background running.
            service.StopDiagnosticsSessionAsync().Wait();
        }
    }
}
