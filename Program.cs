//----------------------------------------------------------------------------------
//
// Copyright (c) Microsoft Corporation. All rights reserved.
//
// THIS CODE AND INFORMATION ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND, 
// EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE IMPLIED WARRANTIES 
// OF MERCHANTABILITY AND/OR FITNESS FOR A PARTICULAR PURPOSE.
//----------------------------------------------------------------------------------
// The example companies, organizations, products, domain names,
// e-mail addresses, logos, people, places, and events depicted
// herein are fictitious.  No association with any real company,
// organization, product, domain name, email address, logo, person,
// places, or events is intended or should be inferred.
//----------------------------------------------------------------------------------
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.RetryPolicies;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CircuitBreakerSample
{

    // Azure Storage Circuit Breaker Demo

    // INSTRUCTIONS
    // Please see the README.md file for an overview
    // explaining this application and how to run it. 

    public class Program
    {
        // Track how many times retry events occur.
        static int retryCount = 0;             // Number of retries that have occurred 
        static int retryThreshold = 5;         // Threshold number of retries before switching to secondary 
        static int secondaryReadCount = 0;     // Number of reads from secondary that have occurred
        static int secondaryThreshold = 20;    // Threshold number of reads from secondary before switching back to primary 

        // This is the CloudBlobClient object we use to access the blob service.
        static CloudBlobClient blobClient;

        // This is the container where we will store and access the blob to be used for testing.
        static CloudBlobContainer container = null;

        static void Main(string[] args)
        {
            Console.WriteLine("Azure Storage Circuit Breaker Sample\n ");

            try
            {
                RunCircuitBreakerAsync().Wait();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.Print("Error thrown = ", ex.ToString());
            }

            Console.WriteLine("\nPress any key to exit.");
            Console.ReadKey();
        }

        /// <summary>
        /// Main method. Sets up the objects needed, then performs a loop
        ///   to perform a blob operation repeatedly, responding to the Retry and Response Received events.
        /// </summary>
        private static async Task RunCircuitBreakerAsync()
        {
            // Name of image to use for testing.
            const string ImageToUpload = "HelloWorld.png";

            // Instantiate a reference to the storage account, then create a reference to the blob client and container.
            string storageConnectionString = Microsoft.Azure.CloudConfigurationManager.GetSetting("StorageConnectionString");
            CloudStorageAccount storageAccount = CreateStorageAccountFromConnectionString(storageConnectionString);
            blobClient = storageAccount.CreateCloudBlobClient();

            // Make the container unique by using a GUID in the name.
            string containerName = "democontainer" + System.Guid.NewGuid().ToString();
            CloudBlobContainer container = blobClient.GetContainerReference(containerName);

            try
            {
                await container.CreateIfNotExistsAsync();
            }
            catch
            {
                Console.WriteLine("Please make sure you have put the correct storage account name and key in the app.config file.");
                Console.ReadLine();
                throw;
            }

            // Define a reference to the actual blob.
            CloudBlockBlob blockBlob = null;

            // Upload a BlockBlob to the newly created container.
            blockBlob = container.GetBlockBlobReference(ImageToUpload);
            await blockBlob.UploadFromFileAsync(ImageToUpload);

            // Set the location mode to secondary so you can check just the secondary data center.
            BlobRequestOptions bro = new BlobRequestOptions();
            bro.LocationMode = LocationMode.SecondaryOnly;

            // Before proceeding, wait until the blob has been replicated to the secondary data center. 
            // Loop and check for the presence of the blob once a second
            //   until it hits 60 seconds or it finds it.
            int counter = 0;
            while (counter < 60)
            {
                counter++;

                Console.WriteLine("Attempt {0} to see if the blob has replicated to secondary yet.", counter);

                if (await blockBlob.ExistsAsync(bro, null))
                {
                    break;
                }

                // Wait a second, then loop around and try again.
                // When it's finished replicating to the secondary, continue on.
                await Task.Delay(1000);
            }

            // Get a reference to the blob we uploaded earlier. 
            blockBlob = container.GetBlockBlobReference(ImageToUpload);

            // Set the starting LocationMode to PrimaryThenSecondary. 
            // Note that the default is PrimaryOnly. 
            // You must have RA-GRS enabled to use this.
            blobClient.DefaultRequestOptions.LocationMode = LocationMode.PrimaryThenSecondary;

            //**************INSTRUCTIONS****************
            // To perform the test, first put your storage account name and key in App.config.
            // Every time it calls DownloadToFileAsync, it will hit the ResponseReceived event. 

            // Next, run this app in Visual Studio. While this loop is running, pause the program in Visual Studio, 
            //    and put the intercept code in Fiddler (that will intercept and return a 503).
            //
            // For instructions on modifying Fiddler, look at the Fiddler_script.text file in this project.
            // There are also full instructions in the ReadMe_Instructions.txt file included in this project.

            // After adding the custom script to Fiddler, calls to primary storage will fail with a retryable error, 
            //   which will trigger the Retrying event (above). 
            // Then it will switch over and read the secondary. It will do that 20 times, then
            //    try to switch back to the primary. 
            //    After seeing that happen, pause this again and remove the intercepting Fiddler code. 
            //    Then you'll see it return to the primary and finish. 

            Console.WriteLine("\nPress any key to pause the application");

            for (int i = 0; i < 1000; i++)
            {
                if (blobClient.DefaultRequestOptions.LocationMode == LocationMode.SecondaryOnly)
                {
                    Console.Write("S{0} ", i.ToString());
                }
                else
                {
                    Console.Write("P{0} ", i.ToString());
                }

                // Set up an operation context for the downloading the blob.
                OperationContext operation_context = new OperationContext();

                try
                {
                    // Hook up the event handlers for the Retry event and the Request Completed event
                    // These events are used to trigger the change from primary to secondary and back.
                    operation_context.Retrying += Operation_context_Retrying;
                    operation_context.RequestCompleted += Operation_context_RequestCompleted;

                    // Download the file.
                    Task task = blockBlob.DownloadToFileAsync(string.Format("./CopyOf{0}", ImageToUpload), FileMode.Create, null, null, operation_context);

                    // Allow user input to pause the application to implement simulated failures
                    while (!task.IsCompleted)
                    {
                        if (Console.KeyAvailable)
                        {
                            Console.ReadKey(true);
                            Console.WriteLine("\nPress any key to resume.");
                            Console.ReadKey();
                        }
                    }
                    await task;
                    if (blobClient.DefaultRequestOptions.LocationMode == LocationMode.SecondaryOnly)
                    {
                        // This (commented-out) code shows how to retrieve the LastSyncTime, which is the last time in UTC that all modifications made to primary
                        //   were replicated to the secondary.
                        // You can only retrieve this from the secondary endpoint.
                        // You can uncomment the code if you want to see it work. It's included here to give you an example in case you want to 
                        //   build it into your application somehow.
                        // For example, with a table, you can compare the entity date/time against the LastSyncTIme. If the entity date/time
                        //   is after the LastSyncTime, then there is some data still replicating from earlier.
                        // Here's the example of how to retrieve the LastSyncTime.
                        //Microsoft.WindowsAzure.Storage.Shared.Protocol.ServiceStats serviceStats = blobClient.GetServiceStats();
                        //string lastSyncTime = serviceStats.GeoReplication.LastSyncTime.HasValue ? serviceStats.GeoReplication.LastSyncTime.Value.ToString() : "empty";
                        //Console.WriteLine("{0}Replication Status = {1}, Last Sync Time = {2}", Environment.NewLine, serviceStats.GeoReplication.Status, lastSyncTime);
                    }
                }
                catch (Exception ex)
                {
                    //If you get a Gateway error here, check and make sure your storage account redundancy is set to RA-GRS.
                    Console.WriteLine(ex.ToString());
                }
                finally
                {
                    // Unhook the event handlers so everything can be garbage collected properly.
                    operation_context.Retrying -= Operation_context_Retrying;
                    operation_context.RequestCompleted -= Operation_context_RequestCompleted;
                }
            }
            // Clean up after ourselves
            container.DeleteIfExists();
        }

        /// RequestCompleted Event handler 
        /// If it's not pointing at the secondary, let it go through. It was either successful, 
        ///   or it failed with a nonretryable event (which we hope is temporary).
        /// If it's pointing at the secondary, increment the read count. 
        /// If the number of reads has hit the threshold of how many reads you want to do against the secondary
        ///   before you switch back to primary, switch back and reset the secondaryReadCount. 
        private static void Operation_context_RequestCompleted(object sender, RequestEventArgs e)
        {
            if (blobClient.DefaultRequestOptions.LocationMode == LocationMode.SecondaryOnly)
            {
                // You're reading the secondary. Let it read the secondary [secondaryThreshold] times, 
                //    then switch back to the primary and see if it's available now.
                secondaryReadCount++;
                if (secondaryReadCount >= secondaryThreshold)
                {
                    blobClient.DefaultRequestOptions.LocationMode = LocationMode.PrimaryThenSecondary;
                    secondaryReadCount = 0;
                }
            }
        }

        /// Retry Event handler 
        /// If it has retried more times than allowed, and it's not already pointed to the secondary,
        ///   flip it to the secondary and reset the retry count.
        /// If it has retried more times than allowed, and it's already pointed to secondary, throw an exception. 
        private static void Operation_context_Retrying(object sender, RequestEventArgs e)
        {
            retryCount++;
            Console.WriteLine("Retrying event because of failure reading the primary. RetryCount = " + retryCount);

            // Check if we have had more than n retries in which case switch to secondary.
            if (retryCount >= retryThreshold)
            {

                // Check to see if we can fail over to secondary.
                if (blobClient.DefaultRequestOptions.LocationMode != LocationMode.SecondaryOnly)
                {
                    blobClient.DefaultRequestOptions.LocationMode = LocationMode.SecondaryOnly;
                    retryCount = 0;
                }
                else
                {
                    throw new ApplicationException("Both primary and secondary are unreachable. Check your application's network connection. ");
                }
            }
        }


        /// <summary>
        /// Validates the connection string information in app.config and throws an exception if it looks like 
        /// the user hasn't updated this to valid values. 
        /// </summary>
        /// <param name="storageConnectionString">The storage connection string</param>
        /// <returns>CloudStorageAccount object</returns>
        private static CloudStorageAccount CreateStorageAccountFromConnectionString(string storageConnectionString)
        {
            CloudStorageAccount storageAccount;
            try
            {
                storageAccount = CloudStorageAccount.Parse(storageConnectionString);
            }
            catch (FormatException)
            {
                Console.WriteLine("Invalid storage account information provided. Please confirm the AccountName and AccountKey are valid in the app.config file - then restart the sample.");
                Console.ReadLine();
                throw;
            }
            catch (ArgumentException)
            {
                Console.WriteLine("Invalid storage account information provided. Please confirm the AccountName and AccountKey are valid in the app.config file - then restart the sample.");
                Console.ReadLine();
                throw;
            }

            return storageAccount;
        }

    }
}
