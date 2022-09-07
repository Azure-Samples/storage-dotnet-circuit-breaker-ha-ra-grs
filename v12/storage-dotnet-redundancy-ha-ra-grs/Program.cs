using System.Diagnostics.Tracing;
using System.IO;
using Azure;
using Azure.Core;
using Azure.Core.Diagnostics;
using Azure.Identity;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using StorageRedundancy;

Console.WriteLine("Azure Storage redundancy sample\n ");

try
{
    await RunStorageRedundancyAsync();
}
catch (Exception ex)
{
    Console.WriteLine(ex.Message);
}

/// <summary>
/// Sets up the objects needed, then loops over blob download operations against primary and secondary endpoints
/// </summary>
static async Task RunStorageRedundancyAsync()
{
    int retryCount = 0;
    const int retryThreshold = 3;
    int secondaryReadCount = 0;
    const int secondaryReadThreshold = 5;

    // TODO: update accountName const to match your storage account name
    const string accountName = "YOURSTORAGEACCOUNTNAME";
    Uri primaryAccountUri = new($"https://{accountName}.blob.core.windows.net/");
    Uri secondaryAccountUri = new($"https://{accountName}-secondary.blob.core.windows.net/");

    string blobName = "MyTestBlob";
    BlobContainerClient? containerClient = null;

    // Provide the client configuration options for connecting to Azure Blob Storage
    BlobClientOptions options = new()
    {
        Retry =
        {
            Delay = TimeSpan.FromSeconds(2),     //The delay between retry attempts for a fixed approach or the delay on which to base 
                                                 //calculations for a backoff-based approach
            MaxRetries = 5,                      //The maximum number of retry attempts before giving up
            Mode = RetryMode.Exponential,        //The approach to use for calculating retry delays
            MaxDelay = TimeSpan.FromSeconds(10)  //The maximum permissible delay between retry attempts
        },
        Diagnostics =
        {
            IsLoggingContentEnabled = true,
        },
        // If the GeoRedundantSecondaryUri property is set, the secondary Uri will be used for GET or HEAD requests during retries.
        // If the status of the response from the secondary Uri is a 404, then subsequent retries for the request will not use the
        // secondary Uri again, as this indicates that the resource may not have propagated there yet.
        // Otherwise, subsequent retries will alternate back and forth between primary and secondary Uri.
        GeoRedundantSecondaryUri = secondaryAccountUri
    };

    BlobClientOptions optionsSecondary = new()
    {
        Retry =
        {
            Delay = TimeSpan.FromSeconds(2),     //The delay between retry attempts for a fixed approach or the delay on which to base 
                                                 //calculations for a backoff-based approach
            MaxRetries = 5,                      //The maximum number of retry attempts before giving up
            Mode = RetryMode.Exponential,        //The approach to use for calculating retry delays
            MaxDelay = TimeSpan.FromSeconds(10)  //The maximum permissible delay between retry attempts
        },
    };

    // Add an HttpPipelinePolicy for debugging purposes
    // This will help us see which endpoint each retry request is being sent to
    options.AddPolicy(new ShowRequestInfoPolicy(), Azure.Core.HttpPipelinePosition.PerRetry);
    optionsSecondary.AddPolicy(new ShowRequestInfoPolicy(), Azure.Core.HttpPipelinePosition.PerRetry);

    try
    {
        // Create a client object for the blob service with the options defined above 
        BlobServiceClient blobServiceClient = new(primaryAccountUri, new DefaultAzureCredential(), options);

        // Create a client object for the blob service which points to the secondary region endpoint
        BlobServiceClient blobServiceClientSecondary = new(secondaryAccountUri, new DefaultAzureCredential(), optionsSecondary);

        // Get some statistics about the secondary region when using RA-GRS or RA-GZRS
        Response<BlobServiceStatistics> responseStats = await blobServiceClientSecondary.GetStatisticsAsync();
        if (responseStats.Value.GeoReplication.Status == BlobGeoReplicationStatus.Live)
        {
            Console.WriteLine("Secondary region is live");
            Console.WriteLine($"Last sync time: {responseStats.Value.GeoReplication.LastSyncedOn}");
        }

        CancellationTokenSource source = new CancellationTokenSource();
        CancellationToken cancellationToken = source.Token;

        // Create a unique name for the container
        string containerName = $"container-{Guid.NewGuid()}";

        // Create the container and return a container client object
        Console.WriteLine("\nCreating container");
        containerClient = await blobServiceClient.CreateBlobContainerAsync(containerName, PublicAccessType.None, null, cancellationToken);

        if (await containerClient.ExistsAsync())
        {
            Console.WriteLine($"Created container {containerClient.Name}\n");
        }

        // Create a new block blob client object
        // The blob client retains the credential and client options
        BlobClient blobClient = containerClient.GetBlobClient(blobName);

        // Upload the data
        Console.WriteLine($"\nUploading blob: {blobName}");
        await blobClient.UploadAsync(BinaryData.FromString("If at first you don't succeed, hopefully you have a good retry policy.").ToStream(), overwrite: true);

        // Check to see if the blob has been replicated to the secondary data center
        BlobClient blobSecondary = blobServiceClientSecondary.GetBlobContainerClient(containerName).GetBlobClient(blobName);
        int counter = 0;
        Console.WriteLine("Checking secondary region endpoint to see if the blob has replicated");
        while (counter < 60)
        {
            counter++;

            Console.WriteLine("Attempt {0} to see if the blob has replicated", counter);

            if (await blobSecondary.ExistsAsync())
            {
                // The blob is found, so break the loop and continue on
                Console.WriteLine("Blob has replicated to secondary region");
                break;
            }

            // If the blob is not replicated yet, wait a second then try again
            await Task.Delay(TimeSpan.FromSeconds(1));
        }
        if (counter >= 60)
        {
            throw new Exception("Unable to find the blob in the secondary region");
        }

        // Download the blob
        Console.WriteLine("\nPress any key to download the blob - Esc to exit");

        // Add an event listener to listen to events produced by Azure SDK client libraries
        // This is a basic example to keep track of request retries
        using AzureEventSourceListener listener = new((args, message) =>
        {
            if (args.EventSource.Name.StartsWith("Azure-Core") && args.EventName == "RequestRetrying")
            {
                retryCount++;
            }
        }, EventLevel.LogAlways);

        while (Console.ReadKey().Key != ConsoleKey.Escape)
        {
            Response<BlobDownloadInfo> response;

            Console.WriteLine($"\nDownloading blob {blobName}:");

            // If the number of retries exceeds the threshold, switch to the BlobServiceClient
            // pointing at the secondary region endpoint for read requests
            if (retryCount > retryThreshold)
            {
                response = await blobSecondary.DownloadAsync();
                secondaryReadCount++;

                // If the number of read requests against the secondary region endpoint exceeds
                // the threshold, reset the variables and try the primary region endpoint again
                if (secondaryReadCount > secondaryReadThreshold)
                {
                    retryCount = 0;
                    secondaryReadCount = 0;
                }
            }
            else
            {
                response = await blobClient.DownloadAsync();
            }

            BlobDownloadInfo downloadInfo = response.Value;

            // Write out the response status
            Console.WriteLine($"Response status: {response.GetRawResponse().Status} ({response.GetRawResponse().ReasonPhrase})");

            // Write out the blob data
            Console.Write("Blob data: ");
            Console.WriteLine((await BinaryData.FromStreamAsync(downloadInfo.Content)).ToString());
            Console.WriteLine($"\nRetry count: {retryCount}");
            Console.WriteLine($"Secondary region read count: {secondaryReadCount}");

            Console.WriteLine("\nPress any key to download the blob again - Esc to exit");
        }
    }
    catch (RequestFailedException e)
    {
        Console.WriteLine(e.Message);
        Console.ReadLine();
        throw;
    }
    finally
    {
        Console.WriteLine("\nThe program has completed successfully");
        Console.WriteLine("Press 'Enter' to delete the sample container and exit the application");
        Console.ReadLine();

        // Clean up resources
        try
        {
            if (containerClient != null)
            {
                Console.WriteLine($"Deleting the container {containerClient.Name}");
                await containerClient.DeleteAsync();
            }
        }
        catch (RequestFailedException e)
        {
            Console.WriteLine(e.Message);
            Console.ReadLine();
        }
    }
}