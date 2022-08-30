---
page_type: sample
languages:
- csharp
products:
- azure
description: "This sample shows how to use the Circuit Breaker pattern with an RA-GRS storage account to switch your high-availability application to secondary storage when there is a problem with primary storage,"
urlFragment: storage-dotnet-circuit-breaker-ha-ra-grs
---

# Using geo-redundancy in your HA apps with RA-GRS Storage

This sample shows how to use geo-redundancy with read-access geo-redundant storage (RA-GRS) to switch your high-availability application to secondary storage when there is a problem with primary storage, and then switch back when primary storage becomes available again. For more information, please see [Designing HA Apps with RA-GRS storage](https://docs.microsoft.com/azure/storage/common/storage-designing-ha-apps-with-ragrs).

If you don't have a Microsoft Azure subscription, you can get a FREE trial account <a href="http://go.microsoft.com/fwlink/?LinkId=330212">here</a>.

## How it works

# [.NET v12 SDK](#tab/dotnet)

This application uploads a blob by creating a Stream object, then uploads the stream to a container for testing. It then enters a loop with a prompt to download the blob, reading from primary storage. If there is a retryable error reading from the primary region, a retry of the read request is performed against secondary storage. To exit the loop and clean up resources, press the `Esc` key at the prompt.

# [.NET v11 SDK](#tab/dotnet11)

This application uploads a file to a container in blob storage to use for the test. Then it proceeds to loop, downloading the file repeatedly, reading against primary storage. If there is an error reading the primary, a retry is performed, and your threshold has been exceeded, it will switch to secondary storage.

The application will continue to read from the secondary until it exceeds that threshold, and then it switches back to primary.

In the case included here, the thresholds are arbitrary numbers for the count of allowable retries against the primary before switching to the secondary, and the count of allowable reads against the secondary before switching back. You can use any algorithm to determine your thresholds; the purpose of this sample is just to show you how to capture the events and switch back and forth.

---

## How to run the sample

# [.NET v12 SDK](#tab/dotnet)

1. If you don't already have it installed, [install Fiddler](http://www.telerik.com/fiddler). In this application, we'll use Fiddler to intercept and modify a response from the service to indicate a failure, so it triggers a failover to the secondary region.

2. Run Fiddler.

3. Start the application in Visual Studio. It displays a console window showing the count of requests made against the storage service to download the file, and tells whether you are accessing the primary or secondary endpoint. You can also see this in the Fiddler trace.

4. At the prompt to download the blob, press any key to see that request is made on the primary region endpoint.

5. Before downloading the blob again, go to Fiddler and select Rules > Customize Rules. Search for the `OnBeforeResponse` function and insert the following code. (An example of the OnBeforeResponse method is included in the project in the Fiddler_script_v12.txt file.)

```
if ((oSession.hostname == "YOURSTORAGEACCOUNTNAME.blob.core.windows.net")) {
   oSession.responseCode = 503;  
}
```

Replace YOURSTORAGEACCOUNTNAME with the name of your storage account and make sure the above code is uncommented. Save your changes to the script.

6. Return to your application. At the prompt to download the blob, press any key to resume the application. In the output, you will see the request sent to the primary region. This request will fail because Fiddler has modified the response to return a 503, then the retry request is sent to the secondary region.

7. Go back into Fiddler, comment out the code, and save the script. At the prompt, press any key to download the blob. The output shows that the request to the primary region is successful again.

8. To exit the application and delete the newly created container, press the `Esc` key at the prompt.

If you run this application multiple times, make sure the added code in the script is commented out before you start the application.

# [.NET v11 SDK](#tab/dotnet11)

1. If you don't already have it installed, [install Fiddler](http://www.telerik.com/fiddler).
 
	This is used to modify the response from the service to indicate a failure, so it triggers the failover to secondary. 

2. Add an environment variable called **storageconnectionstring** string to your machine and put your storage connection string as the value. Change the `DefaultEndpointsProtocol` value from **https** to **http** within the connection string to ensure Fiddler can intercept the traffic. The account must have RA-GRS enabled, or the sample will fail.

    **Linux**
    
    ```bash
    export storageconnectionstring="DefaultEndpointsProtocol=http;AccountName=<mystorageaccount>;AccountKey=<myAccountKey>;EndpointSuffix=core.windows.net"
    ```
    **Windows**
    
    ```cmd
    setx storageconnectionstring "DefaultEndpointsProtocol=http;AccountName=<mystorageaccount>;AccountKey=<myAccountKey>;EndpointSuffix=core.windows.net"
    ```

3. Run Fiddler.

4. Start the application in Visual Studio. It displays a console window showing the count of requests made against the storage service to download the file, and tells whether you are accessing the primary or secondary endpoint. You can also see this in the Fiddler trace. 

5. Press any key to pause the application. 

6. Go to Fiddler and select Rules > Customize Rules. Look for the OnBeforeResponse function and insert this code. (An example of the OnBeforeResponse method is included in the project in the Fiddler_script.txt file.)

	if ((oSession.hostname == "YOURSTORAGEACCOUNTNAME.blob.core.windows.net") 
	&& (oSession.PathAndQuery.Contains("HelloWorld"))) {
	   oSession.responseCode = 503;  
        }

	Change YOURSTORAGEACCOUNTNAME to your storage account name, and uncomment out this code. Save your changes to the script. 

7. Return to your application and press any key to continue running it. In the output, you will see the errors against primary that come from the intercept in Fiddler. Then you will see the switch to secondary storage. After the number of reads exceeds the threshold, you will see it switch back to primary. It does this repeatedly. 

8. Pause the running application again. Go back into Fiddler and comment out the code and save the script. Continue running the application. You will see it switch back to primary and run successfully against primary again.

If you run this repeatedly, be sure the script change is commented out before you start the application.

---


## More information
- [About Azure storage accounts](https://docs.microsoft.com/azure/storage/storage-create-storage-account)
- [Designing HA Apps with RA-GRS storage](https://docs.microsoft.com/azure/storage/common/storage-designing-ha-apps-with-ragrs)
- [Azure Storage Replication](https://docs.microsoft.com/azure/storage/storage-redundancy)
