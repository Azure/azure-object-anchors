// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.

using System;
using System.Diagnostics.Tracing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Core.Diagnostics;
using Azure.MixedReality.ObjectAnchors.Conversion;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace ConversionQuickstart
{
    public class Program
    {
        private const string OptionalConfigFileName = "Config_AOA.json";

        private Configuration configuration;

        public static async Task<int> Main(string[] args)
        {
            string optionalConfigPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), OptionalConfigFileName);
            Configuration configuration = File.Exists(optionalConfigPath)
                ? JsonConvert.DeserializeObject<Configuration>(File.ReadAllText(optionalConfigPath))
                : new Configuration();

            string jobId = string.Empty;
            if (args.Length >= 1)
            {
                if (args[0].Equals("/?") || args[0].Equals("-?"))
                {
                    Console.WriteLine($"Usage: {AppDomain.CurrentDomain.FriendlyName} <optional asset path>");
                    Console.WriteLine($"You can also provide a {OptionalConfigFileName} file");
                    return await Task.FromResult(0);
                }
                else
                {
                    configuration.InputAssetPath = args[0];
                }
            }
            else
            {
                Console.WriteLine("Do you want to monitor an existing job? If so, type the job id. If not, just leave it blank and press Enter.");
                Console.Write("Job Id: ");
                jobId = Console.ReadLine();
            }

            Program program = new Program(configuration);
            return await program.RunJob(jobId);
        }

        public Program(Configuration configuration)
        {
            this.configuration = configuration;
        }

        public async Task<int> RunJob(string jobId = "")
        {
            int returnValue = 0;
            try
            {
                using AzureEventSourceListener listener = new AzureEventSourceListener(
                    (e, message) => configuration.Logger.LogInformation("[{0:HH:mm:ss:fff}][{1}] {2}", DateTimeOffset.Now, e.Level, message),
                    level: EventLevel.Verbose);

                // Initialize Object Anchors client
                ObjectAnchorsConversionClient client = new ObjectAnchorsConversionClient(Guid.Parse(configuration.AccountId), configuration.AccountDomain, new AzureKeyCredential(configuration.AccountKey));

                AssetConversionOperation conversionOperation = null;
                if (string.IsNullOrEmpty(jobId))
                {
                    Console.WriteLine($"Asset   : {configuration.InputAssetPath}");
                    Console.WriteLine($"Gravity : {configuration.Gravity}");
                    Console.WriteLine($"Unit    : {Enum.GetName(typeof(AssetLengthUnit), configuration.AssetDimensionUnit)}");

                    // Upload our asset
                    Console.WriteLine("Attempting to upload asset...");
                    Uri assetUri = (await client.GetAssetUploadUriAsync()).Value.UploadUri;
                    BlobClient uploadBlobClient = new BlobClient(assetUri);
                    using (FileStream fs = File.OpenRead(configuration.InputAssetPath))
                    {
                        await uploadBlobClient.UploadAsync(fs);
                    }

                    // Schedule our asset conversion job specifying:
                    // - The uri to our uploaded asset
                    // - Our asset file format
                    // - Gravity direction of 3D asset
                    // - The unit of measurement of the 3D asset
                    Console.WriteLine("Attempting to create asset conversion job...");
                    var assetOptions = new AssetConversionOptions(
                        assetUri,
                        AssetFileType.FromFilePath(configuration.InputAssetPath),
                        configuration.Gravity,
                        configuration.AssetDimensionUnit);
                    conversionOperation = await client.StartAssetConversionAsync(assetOptions);

                    jobId = conversionOperation.Id;
                    Console.WriteLine($"Successfully created asset conversion job. Job ID: {jobId}");
                }
                else
                {
                    conversionOperation = new AssetConversionOperation(Guid.Parse(jobId), client);
                }

                // Wait for job to complete
                Console.WriteLine("Waiting for job completion...");
                var response = await conversionOperation.WaitForCompletionAsync(new CancellationTokenSource((int)configuration.WaitForJobCompletionTimeout.TotalMilliseconds).Token);
                returnValue = EvaluateJobResults(response.Value);

                if (response.Value.ConversionStatus == AssetConversionStatus.Succeeded)
                {
                    string outputPath = Path.Combine(Path.GetDirectoryName(configuration.InputAssetPath), Path.GetFileNameWithoutExtension(configuration.InputAssetPath) + "_" + jobId + ".ou");
                    Console.WriteLine($"Attempting to download result as '{outputPath}'...");
                    BlobClient downloadBlobClient = new BlobClient(response.Value.OutputModelUri);
                    await downloadBlobClient.DownloadToAsync(outputPath);
                    Console.WriteLine("Success!");
                }
            }
            catch (TaskCanceledException)
            {
                returnValue = 1;
                Console.Error.WriteLine($"Timed out waiting for your job to complete.");
            }
            catch (RequestFailedException e)
            {
                returnValue = 1;
                Console.Error.WriteLine($"\nYour request failed:\n\n{e.Message}");
            }
            catch (Exception e)
            {
                returnValue = 1;
                Console.Error.WriteLine($"\n{e.GetType().Name}:\n{e.Message}");
            }

            return returnValue;
        }

        private static int EvaluateJobResults(AssetConversionProperties jobResults)
        {
            int returnValue = 0;
            switch (jobResults.ConversionStatus)
            {
                case AssetConversionStatus.Succeeded:
                    Console.WriteLine("\nAsset conversion job completed successfully.");
                    break;

                case AssetConversionStatus.Cancelled:
                    Console.WriteLine($"\nAsset conversion job was cancelled.");
                    break;

                case AssetConversionStatus.Failed:
                    Console.WriteLine($"\nAsset conversion job failed with an error.\n\tClientErrorDetails: {jobResults.ClientErrorDetails}\n\tServerErrorDetails: {jobResults.ServerErrorDetails}");
                    break;

                default:
                    Console.WriteLine($"\nAsset conversion job has an unexpected status: ${jobResults.ConversionStatus}");
                    break;
            }
            return returnValue;
        }
    }
}
