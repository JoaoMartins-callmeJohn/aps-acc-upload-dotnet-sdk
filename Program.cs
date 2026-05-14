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
			string file_path = Console.ReadLine().Trim().Trim('"');
			string file_name = SanitizeFileName(Path.GetFileName(file_path));
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
				CreatedItem newItem = CreateNewItem(_dmClient, twoLeggedToken, project_id, file_name, folder_id, storage);
				Console.WriteLine($"File \"{file_name}\" uploaded successfully! Item id: {newItem.Data.Id}");
			}
			catch (DataManagementApiException ex)
			{
				//If there's a conflict, it means there's already an item with the same name, then we update its version
				if (ex.HttpResponseMessage.StatusCode == HttpStatusCode.Conflict)
				{
					Console.WriteLine("One item with this name already exists! Creating a new version...");
					string item_id = GetItemId(_dmClient, twoLeggedToken,project_id, folder_id, file_name);
					CreateNewVersion(_dmClient, twoLeggedToken, project_id, file_name, storage, item_id);
					Console.WriteLine($"File \"{file_name}\" uploaded successfully as a new version!");
				}
			}
			Console.ReadKey();
		}

		/// <summary>
		/// ACC rejects file names that contain characters outside of its allowed set.
		/// Disallowed: \ / : * ? " &lt; &gt; | # % &amp; { } ~ and leading/trailing spaces or dots.
		/// </summary>
		private static string SanitizeFileName(string fileName)
		{
			// Characters explicitly rejected by the ACC Data Management API
			char[] invalidChars = { '\\', '/', ':', '*', '?', '"', '<', '>', '|', '#', '%', '&', '{', '}', '~' };
			string sanitized = string.Concat(fileName.Split(invalidChars));
			sanitized = sanitized.Trim().TrimEnd('.');

			if (sanitized != fileName)
			{
				Console.WriteLine($"Warning: file name contained invalid characters and was sanitized from \"{fileName}\" to \"{sanitized}\".");
			}

			if (string.IsNullOrWhiteSpace(sanitized))
				throw new ArgumentException($"File name \"{fileName}\" consists entirely of invalid characters and cannot be used.");

			return sanitized;
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
			List<ItemData> matchingItems = folderContents.Data.OfType<ItemData>().Where(d => d.Attributes.DisplayName == file_name).ToList();
			int pageNumber = 0;
			while (matchingItems.Count == 0 && !string.IsNullOrEmpty(folderContents.Links.Next?.Href)) {
				pageNumber++;
				folderContents = _dmClient.GetFolderContentsAsync(project_id, folder_id, accessToken: twoLeggedToken.AccessToken, filterExtensionType: filterExtensionType, pageNumber:pageNumber).GetAwaiter().GetResult();
				matchingItems = folderContents.Data.OfType<ItemData>().Where(d => d.Attributes.DisplayName == file_name).ToList();
			}
			return matchingItems.First().Id;
		}

		private static Storage CreateStorage(DataManagementClient _dmClient, TwoLeggedToken twoLeggedToken, string project_id, string file_name, string folder_id)
		{
			StoragePayload payload = new StoragePayload()
			{
				Jsonapi = new JsonApiVersion()
				{
					VarVersion = JsonApiVersionValue._10
				},
				Data = new StoragePayloadData()
				{
					Type = TypeObject.Objects,
					Attributes = new StoragePayloadDataAttributes()
					{
						Name = file_name,
					},
					Relationships = new StoragePayloadDataRelationships()
					{
						Target = new StoragePayloadDataRelationshipsTarget()
						{
							Data = new StoragePayloadDataRelationshipsTargetData()
							{
								Type = TypeFolderItemsForStorage.Folders,
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
				_ossClient.UploadObjectAsync(bucket_key, object_key, fileStream, cancellationToken: CancellationToken.None, accessToken: twoLeggedToken.AccessToken).GetAwaiter().GetResult();
			}
		}

		private static void CreateNewVersion(DataManagementClient _dmClient, TwoLeggedToken twoLeggedToken, string project_id, string file_name, Storage storage, string item_id)
		{
			var versionType = "versions:autodesk.bim360:File";
			if (project_id.StartsWith("a."))
			{
				versionType = "versions:autodesk.core:File";
			}

			VersionPayload versionPayload = new VersionPayload()
			{
				Jsonapi = new JsonApiVersion()
				{
					VarVersion = JsonApiVersionValue._10
				},
				Data = new VersionPayloadData()
				{
					Type = TypeVersion.Versions,
					Attributes = new VersionPayloadDataAttributes()
					{
						Name = file_name,
						Extension = new VersionPayloadDataAttributesExtension()
						{
							Type = versionType,
							VarVersion = "1.0"
						}
					},
					Relationships = new VersionPayloadDataRelationships()
					{
						Item = new VersionPayloadDataRelationshipsItem()
						{
							Data = new VersionPayloadDataRelationshipsItemData()
							{
								Type = TypeItem.Items,
								Id = item_id
							}
						},
						Storage = new VersionPayloadDataRelationshipsStorage()
						{
							Data = new VersionPayloadDataRelationshipsStorageData()
							{
								Type = TypeObject.Objects,
								Id = storage.Data.Id,
							}
						}
					}
				}
			};
			Console.WriteLine(versionPayload.ToString()); 
			_dmClient.CreateVersionAsync(project_id, versionPayload: versionPayload, accessToken: twoLeggedToken.AccessToken).GetAwaiter().GetResult();
		}

		private static CreatedItem CreateNewItem(DataManagementClient _dmClient, TwoLeggedToken twoLeggedToken, string project_id, string file_name, string folder_id, Storage storage)
		{
			var itemType = "items:autodesk.bim360:File";
			var versionType = "versions:autodesk.bim360:File";
			if (project_id.StartsWith("a.")) 
			{
				itemType = "items:autodesk.core:File";
				versionType = "versions:autodesk.core:File";
			}
 
            ItemPayload itemPayload = new ItemPayload()
			{
				Jsonapi = new JsonApiVersion()
				{
					VarVersion = JsonApiVersionValue._10
				},
				Data = new ItemPayloadData()
				{
					Type = TypeItem.Items,
					Attributes = new ItemPayloadDataAttributes()
					{
						DisplayName = file_name,
						Extension = new ItemPayloadDataAttributesExtension()
						{
							Type = itemType,
							VarVersion = "1.0"
						}
					},
					Relationships = new ItemPayloadDataRelationships()
					{
						Tip = new ItemPayloadDataRelationshipsTip()
						{
							Data = new ItemPayloadDataRelationshipsTipData()
							{
								Type = TypeVersion.Versions,
								Id = "1"
							}
						},
						Parent = new ItemPayloadDataRelationshipsParent()
						{
							Data = new ItemPayloadDataRelationshipsParentData()
							{
								Type = TypeFolder.Folders,
								Id = folder_id
							}
						}
					}
				},
				Included = new List<ItemPayloadIncluded>()
						{
								new ItemPayloadIncluded()
								{
										Type = TypeVersion.Versions,
										Id = "1",
										Attributes = new ItemPayloadIncludedAttributes()
										{
												Name = file_name,
												Extension = new ItemPayloadIncludedAttributesExtension()
												{
														Type = versionType,
														VarVersion = "1.0"
												}
										},
										Relationships = new ItemPayloadIncludedRelationships()
										{
											Storage = new ItemPayloadIncludedRelationshipsStorage()
											{
												Data = new ItemPayloadIncludedRelationshipsStorageData()
												{
													Type = TypeObject.Objects,
													Id = storage.Data.Id,
												}
											}
										}
								}
						}
			};
			CreatedItem newItem = _dmClient.CreateItemAsync(project_id, itemPayload: itemPayload, accessToken: twoLeggedToken.AccessToken).GetAwaiter().GetResult();
			return newItem;
		}
	}
}
