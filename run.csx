#r "../bin/Microsoft.Azure.EventHubs.dll"
#r "../bin/SendGrid.dll"
#r "../bin/Newtonsoft.Json.dll"
#r "../bin/Microsoft.WindowsAzure.Storage.dll"
// #r "../bin/Twilio.dll"
// #r "../bin/Microsoft.Azure.WebJobs.Extensions.Twilio.dll"

using System;
using System.Text;
using System.Net;
using System.IO;
using System.Configuration;
using Microsoft.Azure.EventHubs;
using SendGrid.Helpers.Mail;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
//using System.IO;
// using Microsoft.Azure.WebJobs.Extensions.Twilio;
// using Twilio.Rest.Api.V2010.Account;
// using Twilio.Types;


public class AlertContent
{
    public string PartitionKey { get; set; }
    public string RowKey { get; set; }
    public string alertType { get; set; }
    public string confidence { get; set; }
    public string content { get; set; }
}

//public static SendGridMessage Run(EventData myEventHubMessage, ICollector<AlertContent> outputTable, out CreateMessageOptions messagetosms, Binder binder, ILogger log)
public static SendGridMessage Run(EventData myEventHubMessage, ICollector<AlertContent> outputTable, ILogger log)
{
    // log.LogInformation($"C# Event Hub trigger function processed a message: {myEventHubMessage}");
    var payload  = Encoding.UTF8.GetString(myEventHubMessage.Body);
    //log.LogInformation($"Message = {payload}");

    string alerttypelookup = "alertType";
    string confidencelookup = "confidence";
    string imagePathlookup = "imagePath";
    string detTSlookup = "detectionTimestamp";

    int alertTypeOccur = payload.IndexOf(alerttypelookup);
    int confidenceOccur = payload.IndexOf(confidencelookup);
    int imagePathOccur = payload.IndexOf(imagePathlookup);
    //log.LogInformation($"alertTypeOccur = {alertTypeOccur}, confidenceOccur = {confidenceOccur}, imagePathOccur = {imagePathOccur}");

    int alertTypeOccurComma = payload.IndexOf(',', alertTypeOccur+1, payload.Length - alertTypeOccur - 1); //, sc);
    int confidenceOccurComma = payload.IndexOf(',', confidenceOccur+1, payload.Length - confidenceOccur - 1);
    int imagePathOccurComma = payload.IndexOf(',', imagePathOccur+1, payload.Length - imagePathOccur - 1);
    //log.LogInformation($"alertTypeOccurComma = {alertTypeOccurComma}, confidenceOccurComma = {confidenceOccurComma}, imagePathOccurComma = {imagePathOccurComma}");
    
    string alertTypeFound = payload.Substring(alertTypeOccur + alerttypelookup.Length, alertTypeOccurComma - alertTypeOccur - alerttypelookup.Length);
    string confidenceFound = payload.Substring(confidenceOccur + confidencelookup.Length, confidenceOccurComma - confidenceOccur - confidencelookup.Length);
    string imagePathFound = payload.Substring(imagePathOccur + imagePathlookup.Length, imagePathOccurComma - imagePathOccur - imagePathlookup.Length);
    //log.LogInformation($"alertTypeFound = {alertTypeFound}, confidenceFound = {confidenceFound}, imagePathFound = {imagePathFound}");

    string alertTypeFoundcleanedup = alertTypeFound.Substring(alertTypeFound.IndexOf(':') + 1).Trim();
    string confidenceFoundcleanedup = confidenceFound.Substring(confidenceFound.IndexOf(':') + 1).Trim();


    //Partition key = Date part of "detectionTimestamp": "2018-08-16T21:15:19.125Z"
    int detTimeStampOccur = payload.IndexOf(detTSlookup);
    int detTimeStampOccurT = payload.IndexOf('T', detTimeStampOccur + detTSlookup.Length, payload.Length - detTimeStampOccur - detTSlookup.Length);
    log.LogInformation($"detTimeStampOccurT = {detTimeStampOccurT.ToString()}");
    int numchar2Esc = Int32.Parse(Environment.GetEnvironmentVariable("xtrastrspanTimestamp"));
    string detTimeStampFound = payload.Substring(detTimeStampOccur + detTSlookup.Length + numchar2Esc, detTimeStampOccurT - detTimeStampOccur - detTSlookup.Length - numchar2Esc);
    
    //Save to the Azure Storage table with PK as Date of detection
    outputTable.Add(
            new AlertContent() { 
                PartitionKey = detTimeStampFound, 
                RowKey = Guid.NewGuid().ToString(),
                alertType = alertTypeFoundcleanedup,
                confidence = confidenceFoundcleanedup,
                content = payload }
            );

    //Get the image which is in the blob and do a SAS on it and send and email
    string imagePathFoundcleanedup = imagePathFound.Substring(imagePathFound.IndexOf(':') + 1).Trim(); //.Replace("\\\\","\\").Replace("\"","");
    string imagePathFoundFinal = imagePathFoundcleanedup.Replace("\\\\\\\\","\\").Replace("\"","");
    string container;
    int blobptr = imagePathFoundFinal.IndexOf("\\", 1);
    if(imagePathFoundFinal.Substring(0, 1) != "\\")
    {
        container = imagePathFoundFinal.Substring(0, blobptr-1);
    }
    else
    {
        container = imagePathFoundFinal.Substring(1, blobptr-1);
    }
    var tmpblobname = imagePathFoundFinal.Substring(blobptr + 1);
    var blobNameb4 = tmpblobname.Replace("\\","/");
    int lastslash = blobNameb4.LastIndexOf('/');
    string blobName = blobNameb4;
    if (lastslash>0)
    {
        blobName = blobNameb4.Substring(0,lastslash);
    }
    var storageAccount = CloudStorageAccount.Parse(Environment.GetEnvironmentVariable("nrdfuncraisealeaf0e_STORAGE")); //ConfigurationManager.AppSettings["nrdfuncraisealeaf0e_STORAGE"]);
    var blobClient = storageAccount.CreateCloudBlobClient();

    var containerref = blobClient.GetContainerReference(container);
    var blobReference = containerref.GetBlockBlobReference(blobName);
    //log.LogInformation($"blobReference.Uri: {blobReference.Uri}, containerref.Uri: {containerref.Uri}");
    var permissions = SharedAccessBlobPermissions.Read | SharedAccessBlobPermissions.List; // default to read permissions
    //log.LogInformation($"container: {container}, blobName = {blobName}");
    var sasToken = GetBlobSasToken(containerref, blobReference, permissions);
    var path = $"{blobReference.Uri}{sasToken}";
    
    //log.LogInformation($"alertTypeFoundcleanedup = {alertTypeFoundcleanedup}, confidenceFoundcleanedup = {confidenceFoundcleanedup}, imagePathFoundcleanedup = {imagePathFoundcleanedup}, imagePathFoundFinal = {imagePathFoundFinal}, container = {container}, path = {path}");

    SendGridMessage message = new SendGridMessage();
    var msg = $"There is an alert of type {alertTypeFoundcleanedup} with a confidence of {confidenceFoundcleanedup}. The image path is here: {path}";
    message.AddContent("text/plain", msg);

    // messagetosms = new CreateMessageOptions(new PhoneNumber("+918879258025"));
    // messagetosms.Body = msg;

    return message;
}



public static string GetBlobSasToken(CloudBlobContainer container, CloudBlockBlob blob, SharedAccessBlobPermissions permissions, string policyName = null)
{
    string sasBlobToken;
    if (policyName == null) {
        var adHocSas = CreateAdHocSasPolicy(permissions);
        sasBlobToken = blob.GetSharedAccessSignature(adHocSas);
    }
    else {
        sasBlobToken = blob.GetSharedAccessSignature(null, policyName);
    }     
    return sasBlobToken;
}

private static SharedAccessBlobPolicy CreateAdHocSasPolicy(SharedAccessBlobPermissions permissions)
{
    return new SharedAccessBlobPolicy() {
        SharedAccessStartTime = DateTime.UtcNow.AddMinutes(-5),
        SharedAccessExpiryTime = DateTime.UtcNow.AddHours(24),
        Permissions = permissions
    };
}
