# AzureFunctions

Little Azure Functions helpers, samples, spikes, etc.

## VisualStudioGallery

This package makes it trivial to create a [custom Visual Studio gallery](https://msdn.microsoft.com/en-us/library/hh266746.aspx) 
feed using plain Azure Blob Storage to persist the feed as well as automatically update it from VSIX payloads pushed to the same storage container.

The service requires two functions: one for updating the feed blob, and another for returning the feed to the users. 

### Feed Updating Function

1. Create a new Azure Functions app, if you don't have one already.

2. Create a new Azure Funcion using the `BlobTrigger-CSharp` template.

    a. Name your function appropriately, like `vsgallery` or the like.

    b. The `Path` property should have a container name *and* 
       [name patterns](https://docs.microsoft.com/en-us/azure/azure-functions/functions-bindings-storage-blob#pattern) 
       for both the blob name and extension, like `vsgallery/{name}.{extension}`. This is needed for generating 
       appropriate blob name for the extension icon, if any is found in the VSIX.

    c. Name the Azure Storage connection appropriately too, say, `vsgallery_STORAGE`

3. Once created, open the `View files` panel to the right of the functions blade, and click `Add` to add a file named 
   [project.json](https://docs.microsoft.com/en-us/azure/azure-functions/functions-reference-csharp#package-management).
   Add the following content to the `project.json` file to reference the `AzureFunctions.VisualStudioGallery` package:

```
{
  "frameworks": {
    "net46": {
      "dependencies": {
        "AzureFunctions.VisualStudioGallery": "*"
      }
    }
  }
}
```

4. You can manually add the input and output bindings from the Functions UI, but it's much faster to just open the 
       `function.json` file, which would look like the following at this point:

```
{
  "bindings": [
    {
      "name": "myBlob",
      "type": "blobTrigger",
      "direction": "in",
      "path": "vsgallery/{name}.{extension}",
      "connection": "vsgallery_STORAGE"
    }
  ],
  "disabled": false
}
```

Update the contents to contain all the required bindings, as follows:

```
{
  "bindings": [
    {
      "type": "blobTrigger",
      "name": "blob",
      "path": "vsgallery/{name}.{extension}",
      "connection": "vsgallery_STORAGE",
      "direction": "in"
    },
    {
      "type": "blob",
      "name": "currentFeed",
      "path": "feed/vsgallery.xml",
      "connection": "vsgallery_STORAGE",
      "direction": "in"
    },
    {
      "type": "blob",
      "name": "updatedFeed",
      "path": "feed/vsgallery.xml",
      "connection": "vsgallery_STORAGE",
      "direction": "out"
    },
    {
      "type": "blob",
      "name": "icon",
      "path": "alpha/{name}.png",
      "connection": "vsgallery_STORAGE",
      "direction": "out"
    }
  ],
  "disabled": false
}
```

> NOTE: update the `vsgallery_STORAGE` connection value to whatever you had before in the for the 
> `myBlob` default binding that was generated. 
      
> NOTE: also note that the feed XML itself (bindings `currentFeed` and `updatedFeed`) should be in 
> *another* storage container, to avoid unnecessarily re-triggering the same function when you 
> update the feed.
      
5. Finally, open the `run.csx` file that contains the actual function code and replace it entirely with:

```csharp
const string storageUrl = "[YOUR BLOB STORAGE CONTAINER URL WHERE VSIXes ARE UPLOADED]";
const string feedId = "[OPTIONAL FEED ID, DEFAULTS TO 'Gallery']";
const string feedTitle = "[OPTIONAL FEED TITLE, DEFAULTS TO 'Gallery']";

public static void Run(Stream blob, string name, Stream currentFeed, Stream updatedFeed, Stream icon, TraceWriter log)
{
    new VisualStudioGallery(storageUrl, feedId, feedTitle)
        .UpdateFeed(blob, name, currentFeed, updatedFeed, icon);
}
```

That's all that's needed. 

You can test your function by using the [Azure Storage Explorer](http://storageexplorer.com/) to upload 
a VSIX, and see the function run almost in real-time and create the initial atom feed for you.

### Feed Retrieving Function

Technically, you don't need another function to retrieve the Atom feed for VS to consume. You can just 
make the storage container/blob publicly accessible and then just use that instead, such as 
`https://vsgallery.blob.core.windows.net/feed/vsgallery.xml`. 

Since functions can additionally have a custom domain assigned, you may prefer to have a function with 
a custom domain to retrieve it. If so, the function is quite trivial.

1. Create another Azure Funcion using the `HttpTrigger-CSharp` template.

    a. Name your function appropriately, like `feed` or the like.

    b. Set the Authorization level to Anonymous (or whatever is appropriate for your gallery).

2. Add a Blob input binding to the function, either from the UI or by adjusting the `function.json` as follows:

```
{
  "bindings": [
    {
      "authLevel": "anonymous",
      "name": "req",
      "type": "httpTrigger",
      "direction": "in"
    },
    {
      "name": "$return",
      "type": "http",
      "direction": "out"
    },
    {
      "type": "blob",
      "name": "feed",
      "path": "feed/vsgallery.xml",
      "connection": "vsgallery_STORAGE",
      "direction": "in"
    }
  ],
  "disabled": false
}
```

> NOTE: update the `vsgallery_STORAGE` connection value to whatever you have in the function that 
> updates the feed. Update the feed path too.
        
3. Replace `run.csx` with the following code to just return the feed as an Atom XML content:

```csharp
#r "System.Xml.Linq"

using System.Net;
using System.Text;
using System.Xml.Linq;

public static HttpResponseMessage Run(HttpRequestMessage req, Stream feed, TraceWriter log) =>  
    new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent(XDocument.Load(feed).ToString(), Encoding.UTF8, "application/atom+xml")
    };

```

> NOTE: you can use the cool C# lambda syntax since it's a one-liner ;)

With that, you can now head over to the [MSDN documentation on how to add a private Gallery to Visual Studio](https://msdn.microsoft.com/en-us/library/hh266746.aspx) and try it out!
