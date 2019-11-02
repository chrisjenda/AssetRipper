using System;
using System.Collections.Generic;
using System.IO;
using uTinyRipper.Game;
using uTinyRipper.Classes;
using uTinyRipper.SerializedFiles;

using Object = uTinyRipper.Classes.Object;

namespace uTinyRipper
{
	/// <summary>
	/// Serialized files contain binary serialized objects and optional run-time type information.
	/// They have file name extensions like .asset, .assets, .sharedAssets but may also have no extension at all
	/// </summary>
	public sealed class SerializedFile : ISerializedFile
	{
		internal SerializedFile(IFileCollection collection, IAssemblyManager manager, SerializedFileScheme scheme)
		{
			Collection = collection ?? throw new ArgumentNullException(nameof(collection));
			AssemblyManager = manager ?? throw new ArgumentNullException(nameof(manager));
			FilePath = scheme.FilePath;
			NameOrigin = scheme.Name;
			Name = FilenameUtils.FixFileIdentifier(scheme.Name);
			Flags = scheme.Flags;

			Header = scheme.Header;
			Metadata = scheme.Metadata;

			for (int i = 0; i < Metadata.Entries.Length; i++)
			{
				m_assetEntryLookup.Add(Metadata.Entries[i].PathID, i);
			}
		}

		public static bool IsSerializedFile(string filePath)
		{
			if (!MultiFileStream.Exists(filePath))
			{
				throw new Exception($"Serialized file at '{filePath}' doesn't exist");
			}

			using (Stream stream = MultiFileStream.OpenRead(filePath))
			{
				return IsSerializedFile(stream);
			}
		}

		public static bool IsSerializedFile(Stream stream)
		{
			return IsSerializedFile(stream, stream.Position, stream.Length - stream.Position);
		}

		public static bool IsSerializedFile(Stream stream, long offset, long size)
		{
			using (PartialStream bundleStream = new PartialStream(stream, offset, size))
			{
				using (EndianReader reader = new EndianReader(bundleStream, EndianType.BigEndian))
				{
					return SerializedFileHeader.IsSerializedFileHeader(reader, (uint)size);
				}
			}
		}

		public static SerializedFileScheme LoadScheme(string filePath, string fileName)
		{
			if (!MultiFileStream.Exists(filePath))
			{
				throw new Exception($"Serialized file at path '{filePath}' doesn't exist");
			}
			using (SmartStream fileStream = SmartStream.OpenRead(filePath))
			{
				return ReadScheme(fileStream, 0, fileStream.Length, filePath, fileName);
			}
		}

		public static SerializedFileScheme LoadScheme(string filePath, string fileName, TransferInstructionFlags flags)
		{
			if (!MultiFileStream.Exists(filePath))
			{
				throw new Exception($"Serialized file at path '{filePath}' doesn't exist");
			}
			using (SmartStream fileStream = SmartStream.OpenRead(filePath))
			{
				return ReadScheme(fileStream, 0, fileStream.Length, filePath, fileName, flags);
			}
		}

		public static SerializedFileScheme ReadScheme(SmartStream stream, long offset, long size, string filePath, string fileName)
		{
			return SerializedFileScheme.ReadSceme(stream, offset, size, filePath, fileName);
		}

		public static SerializedFileScheme ReadScheme(SmartStream stream, long offset, long size, string filePath, string fileName, TransferInstructionFlags flags)
		{
			return SerializedFileScheme.ReadSceme(stream, offset, size, filePath, fileName, flags);
		}

		private static string[] GetGenerationVersions(FileGeneration generation)
		{
			if (generation < FileGeneration.FG_120_200)
			{
				return new[] { "1.2.2" };
			}

			switch (generation)
			{
				case FileGeneration.FG_120_200:
					return new[] { "2.0.0", "1.6.0", "1.5.0", "1.2.2" };
				case FileGeneration.FG_210_261:
					return new[] { "2.6.1", "2.6.0", "2.5.1", "2.5.0", "2.1.0", };
				case FileGeneration.FG_300b:
					return new[] { "3.0.0b1" };
				default:
					throw new NotSupportedException();
			}
		}

		public Object GetAsset(long pathID)
		{
			Object asset = FindAsset(pathID);
			if (asset == null)
			{
				throw new Exception($"Object with path ID {pathID} wasn't found");
			}

			return asset;
		}

		public Object GetAsset(int fileIndex, long pathID)
		{
			return FindAsset(fileIndex, pathID, false);
		}

		public Object FindAsset(long pathID)
		{
			m_assets.TryGetValue(pathID, out Object asset);
			return asset;
		}

		public Object FindAsset(int fileIndex, long pathID)
		{
			return FindAsset(fileIndex, pathID, true);
		}

		public Object FindAsset(ClassIDType classID)
		{
			foreach (Object asset in FetchAssets())
			{
				if (asset.ClassID == classID)
				{
					return asset;
				}
			}

			foreach (FileIdentifier identifier in Dependencies)
			{
				ISerializedFile file = Collection.FindSerializedFile(identifier);
				if (file == null)
				{
					continue;
				}
				foreach (Object asset in file.FetchAssets())
				{
					if (asset.ClassID == classID)
					{
						return asset;
					}
				}
			}
			return null;
		}

		public Object FindAsset(ClassIDType classID, string name)
		{
			foreach (Object asset in FetchAssets())
			{
				if (asset.ClassID == classID)
				{
					NamedObject namedAsset = (NamedObject)asset;
					if (namedAsset.ValidName == name)
					{
						return asset;
					}
				}
			}

			foreach (FileIdentifier identifier in Dependencies)
			{
				ISerializedFile file = Collection.FindSerializedFile(identifier);
				if (file == null)
				{
					continue;
				}
				foreach (Object asset in file.FetchAssets())
				{
					if (asset.ClassID == classID)
					{
						NamedObject namedAsset = (NamedObject)asset;
						if (namedAsset.Name == name)
						{
							return asset;
						}
					}
				}
			}
			return null;
		}

		public AssetEntry GetAssetEntry(long pathID)
		{
			return Metadata.Entries[m_assetEntryLookup[pathID]];
		}

		public ClassIDType GetAssetType(long pathID)
		{
			return Metadata.Entries[m_assetEntryLookup[pathID]].ClassID;
		}

		public PPtr<T> CreatePPtr<T>(T asset)
			where T : Object
		{
			if (asset.File == this)
			{
				return new PPtr<T>(0, asset.PathID);
			}

			for (int i = 0; i < Dependencies.Count; i++)
			{
				FileIdentifier identifier = Dependencies[i];
				ISerializedFile file = Collection.FindSerializedFile(identifier);
				if (asset.File == file)
				{
					return new PPtr<T>(i + 1, asset.PathID);
				}
			}

			throw new Exception("Asset doesn't belong to this serialized file or its dependencies");
		}

		public IEnumerable<Object> FetchAssets()
		{
			return m_assets.Values;
		}

		public override string ToString()
		{
			return Name;
		}

		internal void Read(EndianReader reader)
		{
			if (RTTIClassHierarchyDescriptor.HasSignature(Header.Generation))
			{
				ReadAssets(reader);
			}
			else
			{
				Logger.Log(LogType.Warning, LogCategory.Import, $"Can't determine file version for generation {Header.Generation} for file '{Name}'");
				string[] versions = GetGenerationVersions(Header.Generation);
				for (int i = 0; i < versions.Length; i++)
				{
					string version = versions[i];
					Logger.Log(LogType.Debug, LogCategory.Import, $"Try parse {Name} as {version} version");
					Metadata.Hierarchy.Version.Parse(version);
					m_assets.Clear();
					try
					{
						ReadAssets(reader);
						UpdateFileVersion();
						break;
					}
					catch
					{
						Logger.Log(LogType.Debug, LogCategory.Import, "Faild");
						if (i == versions.Length - 1)
						{
							throw;
						}
					}
				}
			}
		}

		private Object FindAsset(int fileIndex, long pathID, bool isSafe)
		{
			ISerializedFile file;
			if (fileIndex == 0)
			{
				file = this;
			}
			else
			{
				fileIndex--;
				if (fileIndex >= Dependencies.Count)
				{
					throw new Exception($"{nameof(SerializedFile)} with index {fileIndex} was not found in dependencies");
				}

				FileIdentifier fileRef = Dependencies[fileIndex];
				file = Collection.FindSerializedFile(fileRef);
			}

			if (file == null)
			{
				if (isSafe)
				{
					return null;
				}
				throw new Exception($"{nameof(SerializedFile)} with index {fileIndex} was not found in collection");
			}

			Object asset = file.FindAsset(pathID);
			if (asset == null)
			{
				if (isSafe)
				{
					return null;
				}
				throw new Exception($"Object with path ID {pathID} was not found");
			}
			return asset;
		}

		private void ReadAssets(EndianReader reader)
		{
			AssemblyManager.Version = Version;

			HashSet<int> preloaded = new HashSet<int>();
			using (AssetReader assetReader = new AssetReader(reader, Version, Platform, Flags))
			{
				if (SerializedFileMetadata.HasPreload(Header.Generation))
				{
					foreach (ObjectPtr ptr in Metadata.Preloads)
					{
						if (ptr.FileID == 0)
						{
							int index = m_assetEntryLookup[ptr.PathID];
							ReadAsset(assetReader, ref Metadata.Entries[index]);
							preloaded.Add(index);
						}
					}
				}

				for (int i = 0; i < Metadata.Entries.Length; i++)
				{
					if (Metadata.Entries[i].ClassID == ClassIDType.MonoScript)
					{
						if (preloaded.Add(i))
						{
							ReadAsset(assetReader, ref Metadata.Entries[i]);
						}
					}
				}

				for (int i = 0; i < Metadata.Entries.Length; i++)
				{
					if (!preloaded.Contains(i))
					{
						ReadAsset(assetReader, ref Metadata.Entries[i]);
					}
				}
			}
		}

		private void ReadAsset(AssetReader reader, ref AssetEntry info)
		{
			AssetInfo assetInfo = new AssetInfo(this, info.PathID, info.ClassID);
			Object asset = ReadAsset(reader, assetInfo, Header.DataOffset + info.Offset, info.Size);
			if (asset != null)
			{
				AddAsset(info.PathID, asset);
			}
		}

		private Object ReadAsset(AssetReader reader, AssetInfo assetInfo, long offset, int size)
		{
			Object asset = Collection.AssetFactory.CreateAsset(assetInfo);
			if (asset == null)
			{
				return null;
			}

			reader.BaseStream.Position = offset;
#if !DEBUG
			try
#endif
			{
				asset.Read(reader);
			}
#if !DEBUG
			catch (Exception ex)
			{
				throw new SerializedFileException($"Error during reading asset type {asset.ClassID}", ex, Version, Name, FilePath);
			}
#endif
			long read = reader.BaseStream.Position - offset;
			if (read != size)
			{
				throw new SerializedFileException($"Read {read} but expected {size} for asset type {asset.ClassID}", Version, Name, FilePath);
			}
			return asset;
		}

		private void UpdateFileVersion()
		{
			if (!RTTIClassHierarchyDescriptor.HasSignature(Header.Generation))
			{
				foreach (Object asset in FetchAssets())
				{
					if (asset.ClassID == ClassIDType.BuildSettings)
					{
						BuildSettings settings = (BuildSettings)asset;
						Metadata.Hierarchy.Version.Parse(settings.Version);
						return;
					}
				}
			}
		}

		private void AddAsset(long pathID, Object asset)
		{
			m_assets.Add(pathID, asset);
		}

		public string Name { get; }
		public string NameOrigin { get; }
		public string FilePath { get; }
		public SerializedFileHeader Header { get; }
		public SerializedFileMetadata Metadata { get; }
		public Version Version => Metadata.Hierarchy.Version;
		public Platform Platform => Metadata.Hierarchy.Platform;
		public TransferInstructionFlags Flags { get; private set; }

		public IFileCollection Collection { get; }
		public IAssemblyManager AssemblyManager { get; }
		public IReadOnlyList<FileIdentifier> Dependencies => Metadata.Dependencies;

		private readonly Dictionary<long, Object> m_assets = new Dictionary<long, Object>();
		private readonly Dictionary<long, int> m_assetEntryLookup = new Dictionary<long, int>();
	}
}
