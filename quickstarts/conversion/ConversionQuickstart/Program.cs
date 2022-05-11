// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.

using Azure;
using Azure.Core.Diagnostics;
using Azure.Core.Pipeline;
using Azure.MixedReality.ObjectAnchors.Conversion;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Diagnostics.Tracing;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ConversionQuickstart
{
    public class Program
    {
        private const string OptionalConfigFileName = "Config_AOA.json";

        private readonly Configuration configuration;

        public static async Task<int> Main(string[] args)
        {
            string optionalConfigPath = Path.Combine(System.AppContext.BaseDirectory, OptionalConfigFileName);
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
            return (await program.RunJob(jobId)).result;
        }

        public Program(Configuration configuration)
        {
            this.configuration = configuration;
        }

        public async Task<(int result, string jobId)> RunJob(string jobId = "")
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
                    Console.WriteLine($"\tUpload Uri: {assetUri}");
                    BlobClient uploadBlobClient = new BlobClient(assetUri, new BlobClientOptions
                    {
                        // The default timeout for HttpClient and RetryOptions.NetworkTimeout is 100 seconds.
                        // Increasing to 5 minutes allows for more flexibility when uploading assets
                        Transport = new HttpClientTransport(new HttpClient { Timeout = TimeSpan.FromMinutes(5) }),
                        Retry = { NetworkTimeout = TimeSpan.FromMinutes(5) }
                    });
                    await UploadBlobAsync(uploadBlobClient, configuration.InputAssetPath);
                    Console.WriteLine("\nAsset uploaded");

                    // Schedule our asset conversion job specifying:
                    // - The uri to our uploaded asset
                    // - Our asset file format
                    // - Gravity direction of 3D asset
                    // - The unit of measurement of the 3D asset
                    Console.WriteLine("Attempting to create asset conversion job...");
                    AssetConversionOptions assetOptions = new AssetConversionOptions(
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
                Response<AssetConversionProperties> response = await conversionOperation.WaitForCompletionAsync(new CancellationTokenSource((int)configuration.WaitForJobCompletionTimeout.TotalMilliseconds).Token);
                Console.WriteLine(
                    $"\nAsset dimensions calculated during conversion (in meters): " +
                    $"({(response.Value.ScaledAssetDimensions?.X.ToString() ?? "NULL")}," +
                    $" {(response.Value.ScaledAssetDimensions?.Y.ToString() ?? "NULL")}," +
                    $" {(response.Value.ScaledAssetDimensions?.Z.ToString() ?? "NULL")})");

                returnValue = EvaluateJobResults(response.Value);

                if (response.Value.ConversionStatus == AssetConversionStatus.Succeeded)
                {
                    string tempFolderPath = Path.GetTempPath();
                    string outputFolderPath = Path.GetDirectoryName(configuration.InputAssetPath);
                    string zipFilePath = Path.Combine(tempFolderPath, Path.GetFileNameWithoutExtension(configuration.InputAssetPath) + "_" + jobId + ".zip");
                    Console.WriteLine($"Attempting to download zip result here: '{zipFilePath}' ...");
                    BlobClient downloadBlobClient = new BlobClient(response.Value.OutputModelUri);
                    await downloadBlobClient.DownloadToAsync(zipFilePath);

                    Console.WriteLine($"Unzipping '{zipFilePath}' to '{outputFolderPath}' ...");
                    ZipFile.ExtractToDirectory(zipFilePath, outputFolderPath, overwriteFiles: true);
                    Console.WriteLine($"Success! Your OU Model is here: '{outputFolderPath}\\{jobId.ToUpperInvariant()}.ou'");
                    File.Delete(zipFilePath);
                }
                else
                {
                    ConversionErrorCode errorCode = response.Value.ErrorCode;

                    if (errorCode == ConversionErrorCode.AssetCannotBeConverted)
                    {
                        Console.WriteLine("The asset was unable to be converted. Refer to asset conversion guidelines.");
                    }
                    else if (errorCode == ConversionErrorCode.AssetDimensionsOutOfBounds)
                    {
                        Console.WriteLine("Asset dimensions exceeded those allowed for an asset to be converted.");
                    }
                    else if (errorCode == ConversionErrorCode.AssetSizeTooLarge)
                    {
                        Console.WriteLine("The size of the asset file was too large. Refer to asset conversion guidelines for the maximum allowed size.");
                    }
                    else if (errorCode == ConversionErrorCode.InvalidAssetUri)
                    {
                        Console.WriteLine("The URI provided didn't link to an uploaded asset for conversion.");
                    }
                    else if (errorCode == ConversionErrorCode.InvalidFaceVertices)
                    {
                        Console.WriteLine("The uploaded asset included references to vertices that don't exist. Ensure the asset file is correctly formed.");
                    }
                    else if (errorCode == ConversionErrorCode.InvalidGravity)
                    {
                        Console.WriteLine("The provided gravity vector for the asset was incorrect. Ensure it is not the default all-zero vector.");
                    }
                    else if (errorCode == ConversionErrorCode.InvalidJobId)
                    {
                        Console.WriteLine("The provided job ID doesn't exist.");
                    }
                    else if (errorCode == ConversionErrorCode.InvalidScale)
                    {
                        Console.WriteLine("The provided scale for the asset was either zero or negative.");
                    }
                    else if (errorCode == ConversionErrorCode.TooManyRigPoses)
                    {
                        Console.WriteLine("The provided asset had more rig poses than is permitted for an asset to be converted. Refer to asset conversion guidelines for the maximum allowed size.");
                    }
                    else if (errorCode == ConversionErrorCode.ZeroFaces)
                    {
                        Console.WriteLine("The provided asset had no faced. Ensure the asset file is correctly formed.");
                    }
                    else if (errorCode == ConversionErrorCode.ZeroTrajectoriesGenerated)
                    {
                        Console.WriteLine("The provided asset was determined to have no trajectories generated. Ensure the asset file is correctly formed.");
                    }
                    else
                    {
                        Console.WriteLine("An unexpected error was encountered. If the issue persists, reach out to customer support.");
                    }
                }
            }
            catch (AssetFileTypeNotSupportedException ex)
            {
                returnValue = 1;
                string supportedFileTypeList = string.Join(", ", ex.SupportedAssetFileTypes);
                Console.Error.WriteLine($"The provided asset file of type {ex.AttemptedFileType} is not supported. Supported file types include {supportedFileTypeList}.");
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
                PrintException(e);
            }

            return (result: returnValue, jobId: jobId);
        }

        private static async Task UploadBlobAsync(BlobClient blobClient, string fileToUploadPath)
        {
            Progress<long> progressHandler = null;

            if (!Console.IsOutputRedirected)
            {
                FileInfo file = new FileInfo(fileToUploadPath);
                long uploadFileSize = file.Length;

                object lockObj = new object();
                int consoleRow = Console.CursorTop;
                progressHandler = new Progress<long>();
                progressHandler.ProgressChanged += new EventHandler<long>(delegate (object sender, long bytesUploaded)
                {
                    lock (lockObj)
                    {
                        Console.SetCursorPosition(0, consoleRow);
                        Console.Write($"\tProgress: {((float)bytesUploaded / uploadFileSize) * 100: 00.00}%");
                    }
                });
            }

            using (FileStream fs = File.OpenRead(fileToUploadPath))
            {
                await blobClient.UploadAsync(fs, progressHandler: progressHandler);
            }
        }

        private void PrintException(Exception e)
        {
            Console.Error.WriteLine($"\n{e.GetType().Name}:\n{e.Message}");
            if (e.InnerException != null)
            {
                PrintException(e.InnerException);
            }
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
