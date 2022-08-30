using Azure;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Identity;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace StorageRedundancy
{
    internal class ShowRequestInfoPolicy : HttpPipelinePolicy
    {
        public override void Process(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline)
        {
            string? host = message.Request.Uri.Host;
            if (host is not null) 
            {
                string region = host.Contains("-secondary") ? "secondary" : "primary";
                Console.WriteLine($"Request sent to {region} region endpoint");
            }
            
            ProcessNext(message, pipeline);
        }

        public override async ValueTask ProcessAsync(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline)
        {
            string? host = message.Request.Uri.Host;
            if (host is not null)
            {
                string region = host.Contains("-secondary") ? "secondary" : "primary";
                Console.WriteLine($"Request sent to {region} region endpoint");
            }
            await ProcessNextAsync(message, pipeline);
        }
    }

    public class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Azure Storage redundancy sample\n ");

            try
            {
                RunStorageRedundancyAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

        }

        /// <summary>
        /// Main method -> sets up the objects needed, then loops over blob download operations against primary and secondary endpoints
        /// </summary>
        private static async Task RunStorageRedundancyAsync()
        {
            const string accountName = "pjstorageaccounttest";
            const string accountKey = "XKqGyS9HSkIOz3BgdJpL0WPLb56Cn5x+2VgL7zgWO7kzaFEXf8bNvfJnUyEpvmk/r+OmGYF6KaiC+AStyzIUNw==";
            Uri primaryAccountUri = new Uri($"https://{accountName}.blob.core.windows.net/");
            Uri secondaryAccountUri = new Uri($"https://{accountName}-secondary.blob.core.windows.net/");

            string blobName = "MyTestBlob";

            BlobContainerClient? containerClient = null;

            StorageSharedKeyCredential sharedKeyCredential = new StorageSharedKeyCredential(accountName, accountKey);

            // Provide the client configuration options for connecting to Azure Blob Storage
            BlobClientOptions options = new BlobClientOptions()
            {
                Retry = {
                    Delay = TimeSpan.FromSeconds(2),     //The delay between retry attempts for a fixed approach or the delay on which to base 
                                                         //calculations for a backoff-based approach
                    MaxRetries = 5,                      //The maximum number of retry attempts before giving up
                    Mode = RetryMode.Exponential,        //The approach to use for calculating retry delays
                    MaxDelay = TimeSpan.FromSeconds(10)  //The maximum permissible delay between retry attempts
                },

                // If the GeoRedundantSecondaryUri property is set, the secondary Uri will be used for GET or HEAD requests during retries.
                // If the status of the response from the secondary Uri is a 404, then subsequent retries for the request will not use the
                // secondary Uri again, as this indicates that the resource may not have propagated there yet.
                // Otherwise, subsequent retries will alternate back and forth between primary and secondary Uri.
                GeoRedundantSecondaryUri = secondaryAccountUri
            };

            // Add an HttpPipelinePolicy for debugging purposes
            // This will help us see which endpoint each retry request is being sent to
            options.AddPolicy(new ShowRequestInfoPolicy(), Azure.Core.HttpPipelinePosition.PerRetry);

            try
            {
                // Create a client object for the Blob service with the options defined above
                BlobServiceClient blobServiceClient = new BlobServiceClient(primaryAccountUri, sharedKeyCredential, options);
                //BlobServiceClient blobServiceClient = new BlobServiceClient(primaryAccountUri, new DefaultAzureCredential(), options);

                CancellationTokenSource source = new CancellationTokenSource();
                CancellationToken cancellationToken = source.Token;

                //Create a unique name for the container
                string containerName = $"container-{Guid.NewGuid()}";

                // Create the container and return a container client object
                Console.WriteLine("Creating container");
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

                // Download the blob
                Console.WriteLine("\nPress any key to download the blob - Esc to exit");

                while (Console.ReadKey().Key != ConsoleKey.Escape)
                {
                    Console.WriteLine($"\nDownloading blob {blobName}:");
                    Response<BlobDownloadInfo> response = await blobClient.DownloadAsync();
                    BlobDownloadInfo downloadInfo = response.Value;

                    // Write out the response status
                    Console.WriteLine($"Response status: {response.GetRawResponse().Status} ({response.GetRawResponse().ReasonPhrase})");

                    // Write out the blob data
                    Console.Write("Blob data: ");
                    Console.WriteLine((await BinaryData.FromStreamAsync(downloadInfo.Content)).ToString());

                    Console.WriteLine("\nModify OnBeforeResponse() in Fiddler Script if you wish to test retries on secondary storage");
                    Console.WriteLine("Press any key to download the blob again - Esc to exit");
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
    }
}