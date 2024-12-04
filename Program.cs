using Autodesk.Authentication;
using Autodesk.Authentication.Model;
using Autodesk.DataManagement;
using Autodesk.DataManagement.Model;
using Autodesk.Oss;
using Autodesk.SDKManager;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Xml.Linq;

namespace ACCUploadApp
{
	internal class Program
	{
		static void Main(string[] args)
		{
			string client_id = Environment.GetEnvironmentVariable("client_id");
			string client_secret = Environment.GetEnvironmentVariable("client_secret");
			SDKManager sdkManager = SdkManagerBuilder
								.Create() // Creates SDK Manager Builder itself.
								.Build();
			DataManagementClient _dmClient = new DataManagementClient(sdkManager);
			AuthenticationClient _authClient = new AuthenticationClient(sdkManager);
			OssClient _ossClient = new OssClient(sdkManager);
			TwoLeggedToken twoLeggedToken = _authClient.GetTwoLeggedTokenAsync(client_id, client_secret, new List<Scopes>() { Scopes.DataRead, Scopes.DataWrite, Scopes.DataCreate }).GetAwaiter().GetResult();

			Console.WriteLine("Please write the project id where the file should be uploaded prefixed with 'a.' or 'b.'");
			string project_id = Console.ReadLine();
			if (project_id.StartsWith("a."))
			{
				Console.WriteLine("Please provide 3-legged access token:");
                string access_token = Console.ReadLine();
				twoLeggedToken = new TwoLeggedToken();
				twoLeggedToken.AccessToken = access_token;
            }
			Console.WriteLine("Please write the path of the file to be uploaded");
			string file_path = Console.ReadLine();
			string file_name = Path.GetFileName(file_path);
			Console.WriteLine("Please write the folder id where the file should be uploaded");
			string folder_id = Console.ReadLine();

			Console.WriteLine("Creating Storage...");
			Storage storage = CreateStorage(_dmClient, twoLeggedToken, project_id, file_name, folder_id);
			Console.WriteLine("Storage created!");
			string bucket_key = storage.Data.Id.Split(':')[3].Split("/")[0];
			string object_key = storage.Data.Id.Split(':')[3].Split("/")[1];
			Console.WriteLine($"ObjectKey={object_key} and BucketKey={bucket_key}");

			Console.WriteLine($"Uploading the file to the bucket...");
			ReadAndUploadFile(_ossClient, twoLeggedToken, file_path, bucket_key, object_key);
			Console.WriteLine("File uploaded to the bucket");

			try
			{
				Item newItem = CreateNewItem(_dmClient, twoLeggedToken, project_id, file_name, folder_id, storage);
				Console.WriteLine(newItem.ToString());
			}
			catch (DataManagementApiException ex)
			{
				//If there's a conflict, it means there's already an item with the same name, then we update its version
				if (ex.HttpResponseMessage.StatusCode == HttpStatusCode.Conflict)
				{
					Console.WriteLine("One item with this name already exists! Creating a new version...");
					string item_id = GetItemId(_dmClient, twoLeggedToken,project_id, folder_id, file_name);
					CreateNewVersion(_dmClient, twoLeggedToken, project_id, file_name, storage, item_id);
					Console.WriteLine("Version Created!");
				}
			}
			Console.ReadKey();
		}

		private static string GetItemId(DataManagementClient _dmClient, TwoLeggedToken twoLeggedToken, string project_id, string folder_id, string file_name)
		{
			var itemType = "items:autodesk.bim360:File";
			if (project_id.StartsWith("a."))
			{
				itemType = "items:autodesk.core:File";
            }

            List<string> filterExtensionType = new List<string>() { itemType };
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

		private static void ReadAndUploadFile(OssClient _ossClient, TwoLeggedToken twoLeggedToken, string file_path, string bucket_key, string object_key)
		{
			using (FileStream fileStream = new FileStream(file_path, FileMode.Open, FileAccess.Read))
			{
				_ossClient.Upload(bucket_key, object_key, fileStream, accessToken: twoLeggedToken.AccessToken, CancellationToken.None).GetAwaiter().GetResult();
			}
		}

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

		private static Item CreateNewItem(DataManagementClient _dmClient, TwoLeggedToken twoLeggedToken, string project_id, string file_name, string folder_id, Storage storage)
		{
			var itemType = Autodesk.DataManagement.Model.Type.ItemsautodeskBim360File;
			var versionType = Autodesk.DataManagement.Model.Type.VersionsautodeskBim360File;
			if (project_id.StartsWith("a.")) 
			{
				itemType = Autodesk.DataManagement.Model.Type.ItemsautodeskCoreFile;
				versionType = Autodesk.DataManagement.Model.Type.VersionsautodeskCoreFile;
			}

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
							Type = itemType,
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
														Type = versionType,
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
	}
}