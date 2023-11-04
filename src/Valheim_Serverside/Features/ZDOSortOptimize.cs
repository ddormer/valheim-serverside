using FeaturesLib;
using HarmonyLib;
using PluginConfiguration;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ObjectType = ZDO.ObjectType;

namespace Valheim_Serverside.Features
{
	class ZDOSortOptimize : IFeature
	{
		public bool FeatureEnabled()
		{
			return Configuration.zdoSortOptimizeEnabled.Value;
		}

		public static class SectorMan
		{
			private static Sector[] sectors;
			private static Dictionary<ZDO, Vector2i> lastKnownZdoSector = new Dictionary<ZDO, Vector2i>();

			public static void UpdateZDOSector(ZDO zdo)
			{
				Vector2i sectorVec = zdo.GetSector();
				Vector2i lastKnownSector;
				if (!lastKnownZdoSector.TryGetValue(zdo, out lastKnownSector))
				{
					AddToSector(zdo, sectorVec);
				}
				else if (lastKnownSector != sectorVec)
				{
					RemoveFromSector(zdo, lastKnownSector);
					AddToSector(zdo, sectorVec);
				}
			}

			public static void ZDOReleased(ZDO zdo)
			{
				RemoveFromSector(zdo, zdo.GetSector());
			}

			public static void AddToSector(ZDO zdo, Vector2i sectorVec)
			{
				int sectorIndex = ZDOMan.instance.SectorToIndex(sectorVec);
				if (lastKnownZdoSector.ContainsKey(zdo))
				{
					ZLog.LogError("Adding ZDO without removing from old sector!!!");
				}
				if (sectorIndex >= 0)
				{
					Sector sector = GetSector(sectorIndex);
					sector.AddZDO(zdo);
					lastKnownZdoSector[zdo] = sectorVec;
				}
			}

			public static void RemoveFromSector(ZDO zdo, Vector2i sectorVec)
			{
				int sectorIndex = ZDOMan.instance.SectorToIndex(sectorVec);
				if (sectorIndex >= 0)
				{
					Sector sector = GetSector(sectorIndex);
					sector.RemoveZDO(zdo);
					lastKnownZdoSector.Remove(zdo);
				}
			}

			/*public static void TypeChanged(ZDO zdo, ZDO.ObjectType previousType)
			{
				int sectorIndex = zdo.m_zdoMan.SectorToIndex(zdo.m_sector);
				if (sectorIndex >= 0)
				{
					Sector sector = GetSector(sectorIndex);
					sector.GetTypeList(previousType).Remove(zdo);
					sector.GetTypeList(zdo.m_type).Add(zdo);
				}
			}*/

			public static void PostResetSectorArray(int width)
			{
				sectors = new Sector[width * width];
			}

			public static Sector GetSector(Vector2i sectorVector)
			{
				int sectorIndex = ZDOMan.instance.SectorToIndex(sectorVector);
				return GetSector(sectorIndex);
			}

			private static Sector GetSector(int sectorIndex)
			{
				Sector sector = sectors[sectorIndex];
				if (sector == null)
				{
					sector = new Sector();
					sectors[sectorIndex] = sector;
				}
				return sectors[sectorIndex];
			}

			public class Sector
			{
				public List<ZDO> terrainObjects = new List<ZDO>();
				public List<ZDO> solidObjects = new List<ZDO>();
				public List<ZDO> prioritizedObjects = new List<ZDO>();
				public List<ZDO> defaultObjects = new List<ZDO>();

				public List<ZDO> distantObjects = new List<ZDO>();

				public long owner = 0;

				// <TODO> Sector.SetActive system
				private bool active = true;
				// give this a better name; implement disabling sector objects with SetActive when no peers present (investigate if faster than destroying all the objects at once)
				private List<GameObject> objectsThatWereActive = new List<GameObject>();
				// </TODO>

				public List<ZDO> GetTypeList(ZDO zdo)
				{
					return GetTypeList(zdo.Type);
				}

				public List<ZDO> GetTypeList(ObjectType objectType)
				{
					switch (objectType)
					{
						case ObjectType.Terrain:
							return terrainObjects;
						case ObjectType.Solid:
							return solidObjects;
						case ObjectType.Prioritized:
							return prioritizedObjects;
						case ObjectType.Default:
							return defaultObjects;
						default:
							return null;
					}
				}

				public void AddZDO(ZDO zdo)
				{
					GetTypeList(zdo).Add(zdo);
					if (zdo.Distant)
					{
						distantObjects.Add(zdo);
					}
				}

				public void RemoveZDO(ZDO zdo)
				{
					GetTypeList(zdo).Remove(zdo);
					if (zdo.Distant)
					{
						distantObjects.Remove(zdo);
					}
				}

				public bool ContainsZDO(ZDO zdo)
				{
					return GetTypeList(zdo).Contains(zdo);
				}
			}
		}

		[Harmony]
		public static class Patches
		{
			/* ZDOs are either newly created in ZNetView::Awake or loaded from existing data in ZDOMan::RPC_ZDOData;
			 * we call SectorMan.UpdateZDOSector through hooks on these two paths after we know the ZDO is fully loaded.
			 * 
			 * For the RPC_ZDOData path, we hook on ZDO::Deserialize which is only called from ZDOMan::RPC_ZDOData
			 */
			[HarmonyPatch(typeof(ZNetView), nameof(ZNetView.Awake))]
			public static class Patch_ZNetView_Awake
			{
				private static void Prefix(ZNetView __instance, out bool __state)
				{
					// Don't update ZDO sector if ZNetView::Awake is going to destroy the ZDO anyway
					if (ZNetView.m_forceDisableInit || ZNetView.m_initZDO != null)
					{
						// ZNetView disabled or using existing zdo
						__state = false;
					}
					else
					{
						// Will create new ZDO
						__state = true;
					}
				}

				private static void Postfix(ZNetView __instance, bool __state)
				{
					if (__state)
					{
						// ZDO is new, type et al has now been set properly; we can add to sector
						SectorMan.UpdateZDOSector(__instance.m_zdo);
					}
				}
			}

			[HarmonyPatch(typeof(ZDO))]
			public static class ZDO_Patches
			/* ZDO lifecycle patches for updating sector manager
			 */
			{
				[HarmonyPatch(typeof(ZDO), nameof(ZDO.SetPosition))]
				[HarmonyPostfix]
				private static void Post_SetPosition(ZDO __instance)
				{
					SectorMan.UpdateZDOSector(__instance);
				}

				[HarmonyPatch(typeof(ZDO), nameof(ZDO.Load))]
				[HarmonyPostfix]
				private static void Post_Load(ZDO __instance)
				/* Add ZDO to SectorMan when loading from world file (ZDOMan::Load calls ZDO::Load)
				*/
				{
					SectorMan.UpdateZDOSector(__instance);
				}

				[HarmonyPatch(nameof(ZDO.Deserialize))]
				[HarmonyPostfix]
				private static void Post_Deserialize(ZDO __instance)
				/* Add ZDO to SectorMan on ZDO data received (ZDOMan::RPC_ZDOData calls ZDO::Deserialize)
				 */
				{
					// ZDO type etc. has now been properly set from inside ZDOMan.RPC_ZDOData; we can now update ZDO sector
					SectorMan.UpdateZDOSector(__instance);
				}

				[HarmonyPatch(nameof(ZDO.InvalidateSector))]
				[HarmonyPostfix]
				private static void Post_InvalidateSector(ZDO __instance)
				/* ZDO::InvalidateSector does: this.SetSector(new Vector2i(-100000, -10000));
				 * 
				 * SectorMan handles this by removing the ZDO from its cache if the sector is negative
				 * ZDO is then found in ZDOMan.m_objectsByOutsideSector by our FindObjects patch
				 */
				{
					SectorMan.UpdateZDOSector(__instance);
				}
			}

			[HarmonyPatch(typeof(ZDOPool), nameof(ZDOPool.Release), new[] { typeof(ZDO) })]
			[HarmonyPrefix]
			public static void Patch_ZDOPool_Release(ZDO zdo)
			/* ZDO lifecycle patch for updating sector manager
			 */
			{
				SectorMan.ZDOReleased(zdo);
			}

			[HarmonyPatch(typeof(ZDOMan))]
			public static class ZDOMan_Patches
			{
				[HarmonyPatch(nameof(ZDOMan.ResetSectorArray))]
				[HarmonyPostfix]
				private static void Post_ResetSectorArray(ZDOMan __instance, int ___m_width)
				/* ZDO lifecycle patch for updating sector manager
				 */
				{
					//ZLog.LogWarning("ResetSectorArray");
					SectorMan.PostResetSectorArray(___m_width);
				}

				private static List<ZDO> tempObjects = new List<ZDO>();

				private static void FindObjects(ZDOMan zdoman, Vector2i sectorVec, List<ZDO> objects, Vector3 refPos)
				{
					int num = zdoman.SectorToIndex(sectorVec);
					//ZLog.Log($"zdoman: {zdoman} / sectorvec: {sectorVec} / objects: {objects} / refPos: {refPos} / num: num");
					if (num >= 0)
					{
						SectorMan.Sector sector = SectorMan.GetSector(sectorVec);
						//ZLog.Log($"sector: {sector}");
						objects.AddRange(sector.terrainObjects.OrderBy(o => Vector3.Distance(o.GetPosition(), refPos)));
						objects.AddRange(sector.solidObjects.OrderBy(o => Vector3.Distance(o.GetPosition(), refPos)));
						objects.AddRange(sector.prioritizedObjects.OrderBy(o => Vector3.Distance(o.GetPosition(), refPos)));
						objects.AddRange(sector.defaultObjects.OrderBy(o => Vector3.Distance(o.GetPosition(), refPos)));
					}
					else
					{
						if (zdoman.m_objectsByOutsideSector.TryGetValue(sectorVec, out List<ZDO> collection))
						{
							objects.AddRange(collection);
						}
					}
				}

				private static void FindDistantObjects(Vector2i sectorVec, List<ZDO> objects)
				{
					SectorMan.Sector sector = SectorMan.GetSector(sectorVec);
					objects.AddRange(sector.distantObjects);
				}

				[HarmonyPrefix]
				[HarmonyPatch("FindSectorObjects", new[] { typeof(Vector2i), typeof(int), typeof(int), typeof(List<ZDO>), typeof(List<ZDO>) })]
				//public void FindSectorObjects(Vector2i sector, int area, int distantArea, List<ZDO> sectorObjects, List<ZDO> distantSectorObjects = null)
				private static bool Pre_FindSectorObjects(ZDOMan __instance, Vector2i sector, int area, int distantArea, List<ZDO> sectorObjects, List<ZDO> distantSectorObjects = null)
				{
					Vector3 refPos = ZNet.instance.GetReferencePosition();
					//ZLog.Log($"{__instance} / {sector} / {area} / {distantArea} / {sectorObjects} / {distantSectorObjects} / {refPos}");
					FindObjects(__instance, sector, sectorObjects, refPos);
					for (int i = 1; i <= area; i++)
					{
						for (int j = sector.x - i; j <= sector.x + i; j++)
						{
							FindObjects(__instance, new Vector2i(j, sector.y - i), sectorObjects, refPos);
							FindObjects(__instance, new Vector2i(j, sector.y + i), sectorObjects, refPos);
						}
						for (int k = sector.y - i + 1; k <= sector.y + i - 1; k++)
						{
							FindObjects(__instance, new Vector2i(sector.x - i, k), sectorObjects, refPos);
							FindObjects(__instance, new Vector2i(sector.x + i, k), sectorObjects, refPos);
						}
					}
					List<ZDO> objects = (distantSectorObjects != null) ? distantSectorObjects : sectorObjects;
					for (int l = area + 1; l <= area + distantArea; l++)
					{
						for (int m = sector.x - l; m <= sector.x + l; m++)
						{
							FindDistantObjects(new Vector2i(m, sector.y - l), objects);
							FindDistantObjects(new Vector2i(m, sector.y + l), objects);
						}
						for (int n = sector.y - l + 1; n <= sector.y + l - 1; n++)
						{
							FindDistantObjects(new Vector2i(sector.x - l, n), objects);
							FindDistantObjects(new Vector2i(sector.x + l, n), objects);
						}
					}
					return false;
				}
			}

			[HarmonyPatch(typeof(ZNetScene))]
			public static class ZNetScene_Patches
			{
				[HarmonyPrefix]
				[HarmonyPatch("CreateObjectsSorted")]
				private static bool Pre_CreateObjectsSorted(ZNetScene __instance, List<ZDO> currentNearObjects, int maxCreatedPerFrame, ref int created, ref List<ZDO> ___m_tempCurrentObjects2)
				{
					//if (!ZoneSystem.instance.IsActiveAreaLoaded())
					//{
					//    return false;
					//}
					___m_tempCurrentObjects2.Clear();
					int frameCount = Time.frameCount;
					foreach (ZDO zdo in currentNearObjects)
					{
						if (zdo.TempCreateEarmark != frameCount && (zdo.Distant|| ZoneSystem.instance.IsZoneLoaded(zdo.GetSector())))
						{
							___m_tempCurrentObjects2.Add(zdo);
						}
					}
					foreach (ZDO zdo2 in ___m_tempCurrentObjects2)
					{
						if (__instance.CreateObject(zdo2) != null)
						{
							created++;
							if (created > maxCreatedPerFrame)
							{
								break;
							}
						}
						else if (ZNet.instance.IsServer())
						{
							zdo2.SetOwner(ZNet.GetUID());
							ZLog.Log("Destroyed invalid predab ZDO:" + zdo2.m_uid);
							ZDOMan.instance.DestroyZDO(zdo2);
						}
					}
					return false;
				}

				//[HarmonyPrefix]
				//[HarmonyPatch("RemoveObjects")]
				//private static bool Pre_RemoveObjects(ZNetScene __instance, List<ZDO> currentNearObjects, List<ZDO> currentDistantObjects)
				//{
				//	int frameCount = Time.frameCount;
				//	foreach (ZDO zdo in currentNearObjects)
				//	{
				//		zdo.m_tempRemoveEarmark = frameCount;
				//	}
				//	foreach (ZDO zdo2 in currentDistantObjects)
				//	{
				//		zdo2.m_tempRemoveEarmark = frameCount;
				//	}
				//	__instance.m_tempRemoved.Clear();
				//	int toDestroy = 0;
				//	foreach (ZNetView znetView in __instance.m_instances.Values)
				//	{
				//		if (znetView.GetZDO().m_tempRemoveEarmark != frameCount)
				//		{
				//			__instance.m_tempRemoved.Add(znetView);
				//			toDestroy++;
				//			if (/*!ZNetScene.instance.InLoadingScreen() &&*/ toDestroy >= 50) break;
				//		}
				//	}

				//	int num2 = 0;
				//	while (num2 < __instance.m_tempRemoved.Count)
				//	{
				//		ZNetView znetView2 = __instance.m_tempRemoved[num2];
				//		ZDO zdo3 = znetView2.GetZDO();
				//		znetView2.ResetZDO();
				//		UnityEngine.Object.Destroy(znetView2.gameObject);
				//		if (!zdo3.m_persistent && zdo3.IsOwner())
				//		{
				//			ZDOMan.instance.DestroyZDO(zdo3);
				//		}
				//		__instance.m_instances.Remove(zdo3);
				//		num2++;
				//	}

				//	return false;
				//}
			}
		}
	}
}
