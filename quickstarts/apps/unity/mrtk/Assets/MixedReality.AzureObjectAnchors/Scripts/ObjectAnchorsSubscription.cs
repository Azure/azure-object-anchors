// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.

using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace Microsoft.Azure.ObjectAnchors.Unity.Sample
{
    /// <summary>
    /// Login information required for azure
    /// </summary>
    public class ObjectAnchorsSubscription
    {
        public string AccountId;
        public string AccountKey;
        public string AccountDomain;

        public static async Task<ObjectAnchorsSubscription> LoadObjectAnchorsSubscriptionIfExists()
        {
            ObjectAnchorsSubscription subscripton = null;

            var subscriptionFilePath = Path.Combine(Application.persistentDataPath, "subscription.json").Replace('/', '\\');

            if (File.Exists(subscriptionFilePath))
            {
                using (var reader = new StreamReader(subscriptionFilePath))
                {
                    var content = await reader.ReadToEndAsync();

                    try
                    {
                        subscripton = JsonUtility.FromJson<ObjectAnchorsSubscription>(content);

                        if (string.IsNullOrEmpty(subscripton.AccountId) || string.IsNullOrEmpty(subscripton.AccountKey) || string.IsNullOrEmpty(subscripton.AccountDomain))
                        {
                            Debug.LogWarning("Invalid Azure Object Anchors subscription information.");

                            subscripton = null;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"Fail to load Azure Object Anchors subscription from '{subscriptionFilePath}'. Exception message: '{ex.ToString()}'.");
                    }
                }
            }

            return subscripton;
        }
    }
}