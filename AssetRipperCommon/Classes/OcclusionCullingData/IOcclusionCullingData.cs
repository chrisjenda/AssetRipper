﻿using AssetRipper.Core.Classes.Misc;
using AssetRipper.Core.Extensions;
using AssetRipper.Core.Interfaces;
using AssetRipper.Core.IO.Asset;
using AssetRipper.Core.Logging;
using AssetRipper.Core.Project;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AssetRipper.Core.Classes.OcclusionCullingData
{
	public interface IOcclusionCullingData : INamedObject
	{
		byte[] PVSData { get; set; }
		IOcclusionScene[] Scenes { get; set; }
		ISceneObjectIdentifier[] StaticRenderers { get; set; }
		ISceneObjectIdentifier[] Portals { get; set; }
	}

	public static class OcclusionCullingDataExtensions
	{
		/// <summary>
		/// Not Release
		/// </summary>
		public static bool HasStaticRenderers(TransferInstructionFlags flags)
		{
			return !flags.IsRelease();
		}

		public static void Initialize(this IOcclusionCullingData occlusionCullingData, IExportContainer container, OcclusionCullingSettings.IOcclusionCullingSettings cullingSetting)
		{
			occlusionCullingData.PVSData = (byte[])cullingSetting.PVSData;
			int renderCount = cullingSetting.StaticRenderers.Length;
			int portalCount = cullingSetting.Portals.Length;
			IOcclusionScene scene = VersionHandling.VersionManager.GetHandler(container.ExportVersion).SceneObjectFactory.CreateOcclusionScene();
			scene.Initialize(cullingSetting.SceneGUID, renderCount, portalCount);
			occlusionCullingData.Scenes = new IOcclusionScene[] { scene };

			occlusionCullingData.StaticRenderers = new ISceneObjectIdentifier[scene.SizeRenderers];
			occlusionCullingData.Portals = new ISceneObjectIdentifier[scene.SizePortals];
			occlusionCullingData.SetIDs(container, cullingSetting, scene);
		}

		public static void SetExportData(this IOcclusionCullingData occlusionCullingData, IExportContainer container)
		{
			// if < 3.0.0 this asset doesn't exist

			// 3.0.0 to 5.5.0 this asset is created by culling settings so it has set data already
			if (OcclusionCullingSettings.OcclusionCullingSettingsExtensions.HasReadPVSData(container.Version))
			{
				return;
			}

			// if >= 5.5.0 and !Release this asset containts renderer data
			if (HasStaticRenderers(container.Flags))
			{
				return;
			}

			// if >= 5.5.0 and Release this asset doesn't containt renderers data so we need to create it
			List<OcclusionCullingSettings.IOcclusionCullingSettings> cullingSettings = new List<OcclusionCullingSettings.IOcclusionCullingSettings>();
			foreach (IUnityObjectBase asset in occlusionCullingData.File.Collection.FetchAssets())
			{
				if (asset.ClassID == ClassIDType.OcclusionCullingSettings)
				{
					OcclusionCullingSettings.IOcclusionCullingSettings cullingSetting = (OcclusionCullingSettings.IOcclusionCullingSettings)asset;
					if (cullingSetting.OcclusionCullingData.IsAsset(cullingSetting.File, occlusionCullingData))
					{
						cullingSettings.Add(cullingSetting);
					}
				}
			}

			int maxRenderer = occlusionCullingData.Scenes.Max(j => j.IndexRenderers + j.SizeRenderers);
			occlusionCullingData.StaticRenderers = new ISceneObjectIdentifier[maxRenderer];
			int maxPortal = occlusionCullingData.Scenes.Max(j => j.IndexPortals + j.SizePortals);
			occlusionCullingData.Portals = new ISceneObjectIdentifier[maxPortal];

			foreach (OcclusionCullingSettings.IOcclusionCullingSettings cullingSetting in cullingSettings)
			{
				int sceneIndex = occlusionCullingData.Scenes.IndexOf(t => t.Scene == cullingSetting.SceneGUID);
				if (sceneIndex == -1)
				{
					Logger.Log(LogType.Error, LogCategory.Export, $"Unable to find scene data with GUID {cullingSetting.SceneGUID} in {occlusionCullingData.GetValidName()}");
					continue;
				}

				IOcclusionScene scene = occlusionCullingData.Scenes[sceneIndex];
				if (scene.SizeRenderers != cullingSetting.StaticRenderers.Length)
				{
					throw new Exception($"Scene renderer count {scene.SizeRenderers} doesn't match with given {cullingSetting.StaticRenderers.Length}");
				}
				if (scene.SizePortals != cullingSetting.Portals.Length)
				{
					throw new Exception($"Scene portal count {scene.SizePortals} doesn't match with given {cullingSetting.Portals.Length}");
				}
				SetIDs(occlusionCullingData, container, cullingSetting, scene);
			}
		}

		private static void SetIDs(this IOcclusionCullingData occlusionCullingData, IExportContainer container, OcclusionCullingSettings.IOcclusionCullingSettings cullingSetting, IOcclusionScene scene)
		{
			for (int i = 0; i < cullingSetting.StaticRenderers.Length; i++)
			{
				PPtr<Renderer.IRenderer> prenderer = cullingSetting.StaticRenderers[i];
				Renderer.IRenderer renderer = prenderer.FindAsset(cullingSetting.File);
				occlusionCullingData.StaticRenderers[scene.IndexRenderers + i] = CreateObjectID(container, renderer);
			}

			for (int i = 0; i < cullingSetting.Portals.Length; i++)
			{
				PPtr<IOcclusionPortal> pportal = cullingSetting.Portals[i];
				IOcclusionPortal portal = pportal.FindAsset(cullingSetting.File);
				occlusionCullingData.Portals[scene.IndexPortals + i] = CreateObjectID(container, portal);
			}
		}

		private static ISceneObjectIdentifier CreateObjectID(IExportContainer container, IUnityObjectBase asset)
		{
			long lid = asset == null ? 0 : container.GetExportID(asset);
			ISceneObjectIdentifier sceneObjectIdentifier = VersionHandling.VersionManager.GetHandler(container.ExportVersion).SceneObjectFactory.CreateSceneObjectIdentifier(lid, 0);
			return sceneObjectIdentifier;
		}
	}
}
