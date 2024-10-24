# aps-acc-upload-dotnet-sdk

![Platforms](https://img.shields.io/badge/platform-Windows|MacOS-lightgray.svg)
![.NET](https://img.shields.io/badge/.NET%20-8-blue.svg)
[![License](http://img.shields.io/:license-MIT-blue.svg)](http://opensource.org/licenses/MIT)
[![oAuth2](https://img.shields.io/badge/oAuth2-v1-green.svg)](http://developer.autodesk.com/)
[![Data-Management](https://img.shields.io/badge/Data%20Management-v2-green.svg)](http://developer.autodesk.com/)
[![BIM360](https://img.shields.io/badge/BIM360-v1-green.svg)](http://developer.autodesk.com/)
[![ACC](https://img.shields.io/badge/ACC-v1-green.svg)](http://developer.autodesk.com/)

## Introduction

This sample demonstrates the steps to upload a file to ACC using the APS .NET SDK

## How it works

We need to go through the same steps described in the [Upload a File](https://aps.autodesk.com/en/docs/data/v2/tutorials/upload-file/) tutorial.
In this sample we're going to focus in the steps from 3 to 8, as the steps 1 and 2 are covered by our [Hubs Browser](https://tutorials.autodesk.io/tutorials/hubs-browser/) tutorial.
First thing we need to address is the configuration of the SDKmanager, clients and token generation

```cs
string client_id = Environment.GetEnvironmentVariable("client_id");
string client_secret = Environment.GetEnvironmentVariable("client_secret");
SDKManager sdkManager = SdkManagerBuilder
					.Create() // Creates SDK Manager Builder itself.
					.Build();
DataManagementClient _dmClient = new DataManagementClient(sdkManager);
AuthenticationClient _authClient = new AuthenticationClient(sdkManager);
OssClient _ossClient = new OssClient(sdkManager);
TwoLeggedToken twoLeggedToken = _authClient.GetTwoLeggedTokenAsync(client_id, client_secret, new List<Scopes>() { Scopes.DataRead, Scopes.DataWrite, Scopes.DataCreate }).GetAwaiter().GetResult();
```


3.[Create a storage location](https://aps.autodesk.com/en/docs/data/v2/tutorials/upload-file/#step-3-create-a-storage-location)
For this step we need to use the [Data Management](https://www.nuget.org/packages/Autodesk.DataManagement/2.0.0-beta3) library through the `DataManagementClient`.

  ```cs
  private static Storage CreateStorage(DataManagementClient _dmClient, TwoLeggedToken twoLeggedToken, string project_id, string file_name, string folder_id)
  {
	  StoragePayload payload = new StoragePayload()
	  {
		  Jsonapi = new ModifyFolderPayloadJsonapi()
		  {
			  _Version = VersionNumber._10
		  },
		  Data = new StoragePayloadData()
		  {
			  Type = Autodesk.DataManagement.Model.Type.Objects,
			  Attributes = new StoragePayloadDataAttributes()
			  {
				  Name = file_name,
			  },
			  Relationships = new StoragePayloadDataRelationships()
			  {
				  Target = new ModifyFolderPayloadDataRelationshipsParent()
				  {
					  Data = new ModifyFolderPayloadDataRelationshipsParentData()
					  {
						  Type = Autodesk.DataManagement.Model.Type.Folders,
						  Id = folder_id,
					  }
				  }
			  }
		  }
	  };
	  Storage storage = _dmClient.CreateStorageAsync(project_id, storagePayload: payload, accessToken: twoLeggedToken.AccessToken).GetAwaiter().GetResult();
	  return storage;
  }
  ```

The next 3 steps are addressed together by one single SDK method.

4.[Generate a signed S3 url](https://aps.autodesk.com/en/docs/data/v2/tutorials/upload-file/#step-4-generate-a-signed-s3-url)
5.[Upload a file to the signed url](https://aps.autodesk.com/en/docs/data/v2/tutorials/upload-file/#step-5-upload-a-file-to-the-signed-url)
6.[Complete the upload](https://aps.autodesk.com/en/docs/data/v2/tutorials/upload-file/#step-6-complete-the-upload)
For these steps we need to use the [OSS](https://www.nuget.org/packages/Autodesk.Oss/1.1.1) library through the `OssClient`.

  ```cs
  private static void ReadAndUploadFile(OssClient _ossClient, TwoLeggedToken twoLeggedToken, string file_path, string bucket_key, string object_key)
  {
	  using (FileStream fileStream = new FileStream(file_path, FileMode.Open, FileAccess.Read))
	  {
		  _ossClient.Upload(bucket_key, object_key, fileStream, accessToken: twoLeggedToken.AccessToken, CancellationToken.None).GetAwaiter().GetResult();
	  }
  }
  ```

Now there's a tricky part. We either create a new item (v1) or add a new version to an existing item.
In this sample, we're basically trying the first option, and in case an item with the same name already exists, an error with status 409 is thrown, then we can move to the second option.

7.[Create the first version of the uploaded file](https://aps.autodesk.com/en/docs/data/v2/tutorials/upload-file/#step-7-create-the-first-version-of-the-uploaded-file)
For this step we need to use the [Data Management](https://www.nuget.org/packages/Autodesk.DataManagement/2.0.0-beta3) library through the `DataManagementClient`.

  ```cs
  private static Item CreateNewItem(DataManagementClient _dmClient, TwoLeggedToken twoLeggedToken, string project_id, string file_name, string folder_id, Storage storage)
  {
	  ItemPayload itemPayload = new ItemPayload()
	  {
		  Jsonapi = new ModifyFolderPayloadJsonapi()
		  {
			  _Version = VersionNumber._10
		  },
		  Data = new ItemPayloadData()
		  {
			  Type = Autodesk.DataManagement.Model.Type.Items,
			  Attributes = new ItemPayloadDataAttributes()
			  {
				  DisplayName = file_name,
				  Extension = new ItemPayloadDataAttributesExtension()
				  {
					  Type = Autodesk.DataManagement.Model.Type.ItemsautodeskBim360File,
					  _Version = VersionNumber._10
				  }
			  },
			  Relationships = new ItemPayloadDataRelationships()
			  {
				  Tip = new FolderPayloadDataRelationshipsParent()
				  {
					  Data = new FolderPayloadDataRelationshipsParentData()
					  {
						  Type = Autodesk.DataManagement.Model.Type.Versions,
						  Id = "1"
					  }
				  },
				  Parent = new FolderPayloadDataRelationshipsParent()
				  {
					  Data = new FolderPayloadDataRelationshipsParentData()
					  {
						  Type = Autodesk.DataManagement.Model.Type.Folders,
						  Id = folder_id
					  }
				  }
			  }
		  },
		  Included = new List<ItemPayloadIncluded>()
				  {
						  new ItemPayloadIncluded()
						  {
								  Type = Autodesk.DataManagement.Model.Type.Versions,
								  Id = "1",
								  Attributes = new ItemPayloadIncludedAttributes()
								  {
										  Name = file_name,
										  Extension = new ItemPayloadDataAttributesExtension()
										  {
												  Type = Autodesk.DataManagement.Model.Type.VersionsautodeskBim360File,
												  _Version = VersionNumber._10
										  }
								  },
								  Relationships = new ItemPayloadIncludedRelationships()
								  {
									  Storage = new FolderPayloadDataRelationshipsParent()
									  {
										  Data = new FolderPayloadDataRelationshipsParentData()
										  {
											  Type = Autodesk.DataManagement.Model.Type.Objects,
											  Id = storage.Data.Id,
										  }
									  }
								  }
						  }
				  }
	  };
	  Item newItem = _dmClient.CreateItemAsync(project_id, itemPayload: itemPayload, accessToken: twoLeggedToken.AccessToken).GetAwaiter().GetResult();
	  return newItem;
  }
  ```

To add a new version, we need to find the item id.
That's done with the method below
```cs
private static string GetItemId(DataManagementClient _dmClient, TwoLeggedToken twoLeggedToken, string project_id, string folder_id, string file_name)
{
	List<string> filterExtensionType = new List<string>() { "items:autodesk.bim360:File" };
	FolderContents folderContents = _dmClient.GetFolderContentsAsync(project_id, folder_id, accessToken:twoLeggedToken.AccessToken, filterExtensionType: filterExtensionType).GetAwaiter().GetResult();
	List<FolderContentsData> matchingItems = folderContents.Data.Where(d => d.Attributes.DisplayName == file_name).ToList();
	int pageNumber = 0;
	while (matchingItems.Count > 0 & !string.IsNullOrEmpty(folderContents.Links.Next?.Href)) {
		pageNumber++;
		folderContents = _dmClient.GetFolderContentsAsync(project_id, folder_id, accessToken: twoLeggedToken.AccessToken, filterExtensionType: filterExtensionType, pageNumber:pageNumber).GetAwaiter().GetResult();
		matchingItems = folderContents.Data.Where(d => d.Attributes.DisplayName == file_name).ToList();
	}
	return matchingItems.First().Id;
}
```

8.[Update the version of a file](https://aps.autodesk.com/en/docs/data/v2/tutorials/upload-file/#step-8-update-the-version-of-a-file)
For this step we need to use the [Data Management](https://www.nuget.org/packages/Autodesk.DataManagement/2.0.0-beta3) library through the `_dmClient`.

  ```cs
  private static void CreateNewVersion(DataManagementClient _dmClient, TwoLeggedToken twoLeggedToken, string project_id, string file_name, Storage storage, string item_id)
  {
	  VersionPayload versionPayload = new VersionPayload()
	  {
		  Jsonapi = new ModifyFolderPayloadJsonapi()
		  {
			  _Version = VersionNumber._10
		  },
		  Data = new VersionPayloadData()
		  {
			  Type = Autodesk.DataManagement.Model.Type.Versions,
			  Attributes = new VersionPayloadDataAttributes()
			  {
				  Name = file_name,
				  Extension = new RelationshipRefsPayloadDataMetaExtension()
				  {
					  Type = Autodesk.DataManagement.Model.Type.VersionsautodeskBim360File,
					  _Version = VersionNumber._10
				  }
			  },
			  Relationships = new VersionPayloadDataRelationships()
			  {
				  Item = new FolderPayloadDataRelationshipsParent()
				  {
					  Data = new FolderPayloadDataRelationshipsParentData()
					  {
						  Type = Autodesk.DataManagement.Model.Type.Items,
						  Id = item_id
					  }
				  },
				  Storage = new FolderPayloadDataRelationshipsParent()
				  {
					  Data = new FolderPayloadDataRelationshipsParentData()
					  {
						  Type = Autodesk.DataManagement.Model.Type.Objects,
						  Id = storage.Data.Id,
					  }
				  }
			  }
		  }
	  };
	  Console.WriteLine(versionPayload.ToString()); 
	  ModelVersion newVersion = _dmClient.CreateVersionAsync(project_id, versionPayload: versionPayload, accessToken: twoLeggedToken.AccessToken).GetAwaiter().GetResult();
  }
  ```

# Setup

## Prerequisites

1. **APS Account**: Learn how to create a APS Account, activate subscription and create an app at [this tutorial](http://aps.autodesk.com/tutorials/#/account/).
2. **Visual Studio**: Community or Pro.
3. **.NET** basic knowledge with C#

## Running locally

Clone this project or download it. It's recommended to install [GitHub desktop](https://desktop.github.com/). To clone it via command line, use the following (**Terminal** on MacOSX/Linux, **Git Shell** on Windows):

    git clone https://github.com/joaomartins-callmejohn/aps-acc-upload-dotnet-sdk

**Visual Studio** (Windows):

Replace **client_id** and **client_secret** with your own keys.
You can do it directly in the 'Properties/lauchSettings.json' file or through Visual Studio UI under the debug properties.

# Further Reading

### Troubleshooting

1. **Incorrect id**: The project id must be passed with the b. prefix

2. **Not able to read the file**: The file path must be pasted without ""

3. **Not able to read ACC/BIM 360 data with acquired token**: Make sure to provision the APS App Client ID within the BIM 360 Account, [learn more here](https://aps.autodesk.com/blog/bim-360-docs-provisioning-forge-apps). This requires the Account Admin permission.

## License

This sample is licensed under the terms of the [MIT License](http://opensource.org/licenses/MIT). Please see the [LICENSE](LICENSE) file for full details.

## Written by

João Martins [in/jpornelas](https://linkedin.com/in/jpornelas)

