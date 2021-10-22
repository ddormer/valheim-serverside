using FeaturesLib;
using HarmonyLib;
using MonoMod.Cil;
using PluginConfiguration;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ObjectType = ZDO.ObjectType;
using OC = Mono.Cecil.Cil.OpCodes;

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
				Vector2i sectorVec = zdo.m_sector;
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
				RemoveFromSector(zdo, zdo.m_sector);
			}

			public static void AddToSector(ZDO zdo, Vector2i sectorVec)
			{
				int sectorIndex = zdo.m_zdoMan.SectorToIndex(sectorVec);
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
				int sectorIndex = zdo.m_zdoMan.SectorToIndex(sectorVec);
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
					return GetTypeList(zdo.m_type);
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
					if (zdo.m_distant)
					{
						distantObjects.Add(zdo);
					}
				}

				public void RemoveZDO(ZDO zdo)
				{
					GetTypeList(zdo).Remove(zdo);
					if (zdo.m_distant)
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
		static class Patches
		{
			/* ZDOs are either newly created in ZNetView::Awake or loaded from existing data in ZDOMan::RPC_ZDOData;
			 * we call SectorMan.UpdateZDOSector through hooks on these two paths after we know the ZDO is fully loaded.
			 * 
			 * For the RPC_ZDOData path, we hook on ZDO::Deserialize which is only called from ZDOMan::RPC_ZDOData
			 */
			[HarmonyPatch(typeof(ZNetView), nameof(ZNetView.Awake))]
			private static class Patch_ZNetView_Awake
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
			private static class ZDO_Patches
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
			private static void Patch_ZDOPool_Release(ZDO zdo)
			/* ZDO lifecycle patch for updating sector manager
			 */
			{
				SectorMan.ZDOReleased(zdo);
			}

			[HarmonyPatch(typeof(ZDOMan))]
			private static class ZDOMan_Patches
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

			//[HarmonyPatch(typeof(ZDOMan))]
			//private static class ZDOMan_Patches
			//{
			//    [HarmonyPostfix]
			//    [HarmonyPatch(nameof(ZDOMan.AddToSector)]
			//    private static void Post_AddToSector(ZDOMan __instance, ZDO zdo, Vector2i sector)
			//    {
			//        SectorMan.PostAddToSector(__instance, zdo, sector);
			//        //ZLog.Log("yes");
			//        /*int sectorIndex = __instance.SectorToIndex(sector);
			//        if (sectorIndex > 0)
			//        {
			//            SectorObjects objects = sectorObjectsBySector[sectorIndex];
			//            if (objects == null)
			//            {
			//                sectorObjectsBySector[sectorIndex] = new SectorObjects();
			//                objects = sectorObjectsBySector[sectorIndex];
			//            }
			//            switch (zdo.m_type)
			//            {
			//                case ZDO.ObjectType.Default:
			//                    objects.m_defaultObjects.Add(zdo);
			//                    break;
			//                case ZDO.ObjectType.Prioritized:
			//                    objects.m_prioritizedObjects.Add(zdo);
			//                    break;
			//                case ZDO.ObjectType.Solid:
			//                    objects.m_solidObjects.Add(zdo);
			//                    break;
			//                case ZDO.ObjectType.Terrain:
			//                    objects.m_terrainObjects.Add(zdo);
			//                    break;
			//            }
			//        }*/
			//    }

			//    [HarmonyPrefix]
			//    [HarmonyPatch("RemoveFromSector")]
			//    private static void Pre_RemoveFromSector(ZDOMan __instance, ZDO zdo, Vector2i sector)
			//    {
			//        //ZLog.Log("yes");
			//        int sectorIndex = __instance.SectorToIndex(sector);
			//        if (sectorIndex > 0)
			//        {
			//            SectorObjects objects = sectorObjectsBySector[sectorIndex];
			//            if (objects == null)
			//            {
			//                return;
			//            }
			//            switch (zdo.m_type)
			//            {
			//                case ZDO.ObjectType.Default:
			//                    objects.m_defaultObjects.Remove(zdo);
			//                    break;
			//                case ZDO.ObjectType.Prioritized:
			//                    objects.m_prioritizedObjects.Remove(zdo);
			//                    break;
			//                case ZDO.ObjectType.Solid:
			//                    objects.m_solidObjects.Remove(zdo);
			//                    break;
			//                case ZDO.ObjectType.Terrain:
			//                    objects.m_terrainObjects.Remove(zdo);
			//                    break;
			//            }
			//        }
			//    }

			//    [HarmonyPostfix]
			//    [HarmonyPatch("ResetSectorArray")]
			//    private static void Post_ResetSectorArray(ZDOMan __instance)
			//    {
			//        sectorObjectsBySector = new SectorObjects[__instance.m_width * __instance.m_width];
			//    }

			//    [HarmonyPrefix]
			//    [HarmonyPatch("FindSectorObjects", new Type[] { typeof(Vector2i), typeof(int), typeof(int), typeof(List<ZDO>), typeof(List<ZDO>) })]
			//    private static bool Pre_FindSectorObjects(Vector2i sector, int area, int distantArea, List<ZDO> sectorObjects, List<ZDO> distantSectorObjects)
			//    {
			//        CreateSyncList_Patch.SortFindSectorObjects(sector, area, distantArea, sectorObjects, distantSectorObjects, null, false);
			//        return false;
			//    }
			//}

			//[HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.CreateSyncList))]
			private static class CreateSyncList_Patch
			{
				/*private static void ILManipulator(ILContext il)
				{
					new ILCursor(il)
						.GotoNext(MoveType.AfterLabel,
							i => i.MatchCall<ZDOMan>("FindSectorObjects")
						)
						// Add List<ZDO> toSync to call
						.Emit(OC.Ldarg, 1)
						.Emit(OC.Call, AccessTools.Method(typeof(CreateSyncList_Patch),
														  nameof(CreateSyncList_Patch.DoSortFindSectorObjects)))
						.Remove()
					;
				}*/

				/*private static List<ZDO> tempPresortedObjects = new List<ZDO>();

				private static List<ZDO> tempTerrainObjects = new List<ZDO>();
				private static List<ZDO> tempSolidObjects = new List<ZDO>();
				private static List<ZDO> tempPrioritizedObjects = new List<ZDO>();
				private static List<ZDO> tempDefaultObjects = new List<ZDO>();*/

				//private static void FindObjectsPresorted(Vector2i sector, List<ZDO> terrainObjects, List<ZDO> solidObjects, List<ZDO> prioritizedObjects, List<ZDO> defaultObjects, List<ZDO> allObjects)
				//{
				//    int num = ZDOMan.instance.SectorToIndex(sector);
				//    List<ZDO> collection;
				//    if (num >= 0)
				//    {
				//        if (sectorObjectsBySector[num] != null)
				//        {
				//            terrainObjects.AddRange(sectorObjectsBySector[num].m_terrainObjects);
				//            solidObjects.AddRange(sectorObjectsBySector[num].m_solidObjects);
				//            prioritizedObjects.AddRange(sectorObjectsBySector[num].m_prioritizedObjects);
				//            defaultObjects.AddRange(sectorObjectsBySector[num].m_defaultObjects);
				//            return;
				//        }
				//    }
				//    else if (ZDOMan.instance.m_objectsByOutsideSector.TryGetValue(sector, out collection))
				//    {
				//        allObjects.AddRange(collection);
				//    }
				//}

				/*public static void SortFindSectorObjects(Vector2i sector, int area, int distantArea, List<ZDO> sectorObjects, List<ZDO> distantSectorObjects, ZDOMan.ZDOPeer peer = null, bool distance = false)
				{
					tempPresortedObjects.Clear();
					tempSolidObjects.Clear();
					tempPrioritizedObjects.Clear();
					tempDefaultObjects.Clear();

					ZDOMan __instance = ZDOMan.instance;
					FindObjectsPresorted(sector, tempTerrainObjects, tempSolidObjects, tempPrioritizedObjects, tempDefaultObjects, tempPresortedObjects);
					for (int i = 1; i <= area; i++)
					{
						for (int j = sector.x - i; j <= sector.x + i; j++)
						{
							FindObjectsPresorted(new Vector2i(j, sector.y - i), tempTerrainObjects, tempSolidObjects, tempPrioritizedObjects, tempDefaultObjects, tempPresortedObjects);
							FindObjectsPresorted(new Vector2i(j, sector.y + i), tempTerrainObjects, tempSolidObjects, tempPrioritizedObjects, tempDefaultObjects, tempPresortedObjects);
						}
						for (int k = sector.y - i + 1; k <= sector.y + i - 1; k++)
						{
							FindObjectsPresorted(new Vector2i(sector.x - i, k), tempTerrainObjects, tempSolidObjects, tempPrioritizedObjects, tempDefaultObjects, tempPresortedObjects);
							FindObjectsPresorted(new Vector2i(sector.x + i, k), tempTerrainObjects, tempSolidObjects, tempPrioritizedObjects, tempDefaultObjects, tempPresortedObjects);
						}
						// Only sort immediate objects to be all speedy like!
						*//*if (distance && i == 1)
						{
							tempPresortedObjects.AddRange(tempSolidObjects);
							tempPresortedObjects.AddRange(tempPrioritizedObjects);
							tempPresortedObjects.AddRange(tempDefaultObjects);
							tempSolidObjects.Clear();
							tempPrioritizedObjects.Clear();
							tempDefaultObjects.Clear();
							//ZDOMan.instance.ServerSortSendZDOS(tempPresortedObjects, peer.m_peer.GetRefPos(), peer);
							tempPresortedObjects.Clear();
						}*//*
					}

					if (peer != null)
					{
						Vector3 refPos = peer.m_peer.GetRefPos();
						tempTerrainObjects.OrderBy(o => Vector3.Distance(o.GetPosition(), refPos));
						tempSolidObjects.OrderBy(o => Vector3.Distance(o.GetPosition(), refPos));
						tempPrioritizedObjects.OrderBy(o => Vector3.Distance(o.GetPosition(), refPos));
						tempDefaultObjects.OrderBy(o => Vector3.Distance(o.GetPosition(), refPos));
					}

					sectorObjects.AddRange(tempTerrainObjects);
					sectorObjects.AddRange(tempPresortedObjects);
					sectorObjects.AddRange(tempSolidObjects);
					sectorObjects.AddRange(tempPrioritizedObjects);
					sectorObjects.AddRange(tempDefaultObjects);

					List<ZDO> objects = (distantSectorObjects != null) ? distantSectorObjects : sectorObjects;
					for (int l = area + 1; l <= area + distantArea; l++)
					{
						for (int m = sector.x - l; m <= sector.x + l; m++)
						{
							__instance.FindDistantObjects(new Vector2i(m, sector.y - l), objects);
							__instance.FindDistantObjects(new Vector2i(m, sector.y + l), objects);
						}
						for (int n = sector.y - l + 1; n <= sector.y + l - 1; n++)
						{
							__instance.FindDistantObjects(new Vector2i(sector.x - l, n), objects);
							__instance.FindDistantObjects(new Vector2i(sector.x + l, n), objects);
						}
					}
					//ZLog.Log(tempPresortedObjects.Count);
				}*/

				/*public static void DoSortFindSectorObjects(ZDOMan __instance, Vector2i sector, int area, int distantArea, List<ZDO> sectorObjects, List<ZDO> distantSectorObjects, ZDOMan.ZDOPeer peer)
				{
					SortFindSectorObjects(sector, area, distantArea, sectorObjects, distantSectorObjects, peer);
				}

				private static void ServerSortSendZDOS(List<ZDO> objects, Vector3 refPos, ZDOMan.ZDOPeer peer)
				{
					float time = Time.time;
					for (int i = 0; i < objects.Count; i++)
					{
						ZDO zdo = objects[i];
						Vector3 position = zdo.GetPosition();
						zdo.m_tempSortValue = Vector3.Distance(position, refPos);
						float num = 100f;
						ZDOMan.ZDOPeer.PeerZDOInfo peerZDOInfo;
						if (peer.m_zdos.TryGetValue(zdo.m_uid, out peerZDOInfo))
						{
							num = Mathf.Clamp(time - peerZDOInfo.m_syncTime, 0f, 100f);
							zdo.m_tempHaveRevision = true;
						}
						else
						{
							zdo.m_tempHaveRevision = false;
						}
						zdo.m_tempSortValue -= num * 1.5f;
					}
					ZDOMan.compareReceiver = peer.m_peer.m_uid;
					objects.Sort(new Comparison<ZDO>(ZDOMan.ServerSendCompare));
				}*/
			}

			[HarmonyPatch(typeof(ZDOMan))]
			private static class _ZDOMan_Patches
			{

				//[HarmonyPrefix]
				//[HarmonyPatch("ServerSortSendZDOS")]
				private static bool Pre_ServerSortSendZDOS(List<ZDO> objects, ZDOMan.ZDOPeer peer)
				{
					// ZDOMan.ShouldSend already filters on has revision, so we don't need the commented out code
					return false;

					/*float time = Time.time;

					foreach (ZDO zdo in objects)
					{
						ZDOMan.ZDOPeer.PeerZDOInfo peerZDOInfo;
						if (peer.m_zdos.TryGetValue(zdo.m_uid, out peerZDOInfo))
						{
							zdo.m_tempHaveRevision = true;
						}
						else
						{
							zdo.m_tempHaveRevision = false;
						}
					}
					return false;*/
				}

				/*            [HarmonyPatch(typeof(ZDOMan), "ClientSortSendZDOS")]
							private static class ClientSortSendZDOs_Patch
							{
								private static bool Prefix()
								{
									return false;
								}
							}*/
			}

			//[HarmonyPatch(typeof(ZNetScene), "CreateObjects")]
			private class ZNetScene_CreateObjects_Patch
			{
				private static void ILManipulator(ILContext il)
				{
					new ILCursor(il)
						.GotoNext(MoveType.AfterLabel,
							i => i.MatchLdcI4(10))
						.Remove()
						.Emit(OC.Ldc_I4, 20)
						.GotoNext(MoveType.AfterLabel,
							i => i.MatchLdcI4(100))
						.Remove()
						.Emit(OC.Ldc_I4, 200)
					;
				}
			}

			[HarmonyPatch(typeof(ZNetScene))]
			private static class ZNetScene_Patches
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
						if (zdo.m_tempCreateEarmark != frameCount && (zdo.m_distant || ZoneSystem.instance.IsZoneLoaded(zdo.GetSector())))
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
							zdo2.SetOwner(ZDOMan.instance.GetMyID());
							ZLog.Log("Destroyed invalid predab ZDO:" + zdo2.m_uid);
							ZDOMan.instance.DestroyZDO(zdo2);
						}
					}
					return false;
				}

				[HarmonyPrefix]
				[HarmonyPatch("RemoveObjects")]
				private static bool Pre_RemoveObjects(ZNetScene __instance, List<ZDO> currentNearObjects, List<ZDO> currentDistantObjects)
				{
					int frameCount = Time.frameCount;
					foreach (ZDO zdo in currentNearObjects)
					{
						zdo.m_tempRemoveEarmark = frameCount;
					}
					foreach (ZDO zdo2 in currentDistantObjects)
					{
						zdo2.m_tempRemoveEarmark = frameCount;
					}
					__instance.m_tempRemoved.Clear();
					int toDestroy = 0;
					foreach (ZNetView znetView in __instance.m_instances.Values)
					{
						if (znetView.GetZDO().m_tempRemoveEarmark != frameCount)
						{
							__instance.m_tempRemoved.Add(znetView);
							toDestroy++;
							if (/*!ZNetScene.instance.InLoadingScreen() &&*/ toDestroy >= 50) break;
						}
					}

					int num2 = 0;
					while (num2 < __instance.m_tempRemoved.Count)
					{
						ZNetView znetView2 = __instance.m_tempRemoved[num2];
						ZDO zdo3 = znetView2.GetZDO();
						znetView2.ResetZDO();
						UnityEngine.Object.Destroy(znetView2.gameObject);
						if (!zdo3.m_persistent && zdo3.IsOwner())
						{
							ZDOMan.instance.DestroyZDO(zdo3);
						}
						__instance.m_instances.Remove(zdo3);
						num2++;
					}

					return false;
				}
			}

			// private void PlaceRoom(DungeonDB.RoomData room, Vector3 pos, Quaternion rot, RoomConnection fromConnection, ZoneSystem.SpawnMode mode)
			//[HarmonyPatch(typeof(DungeonGenerator), nameof(DungeonGenerator.PlaceRoom), new[] { typeof(DungeonDB.RoomData), typeof(Vector3), typeof(Quaternion), typeof(RoomConnection), typeof(ZoneSystem.SpawnMode) })]
			private static class DungeonGenerator_PlaceRoom_Patch
			{
				public static Dictionary<GameObject, Transform> transformsToRestore = new Dictionary<GameObject, Transform>();

				private static void Prefix(DungeonDB.RoomData room, Vector3 pos, Quaternion rot, RoomConnection fromConnection, ZoneSystem.SpawnMode mode, out Dictionary<GameObject, Transform> __state)
				{
					if (mode == ZoneSystem.SpawnMode.Client)
					{
						__state = transformsToRestore;
						__state.Clear();

						foreach (ZNetView nview in room.m_room.GetComponentsInChildren<ZNetView>())
						{
							GameObject gameObject = nview.gameObject;
							if (gameObject.transform.parent != null)
							{
								__state.Add(gameObject, gameObject.transform.parent);
								gameObject.transform.parent = null;
							}
						}
					}
					else
					{
						__state = null;
					}
				}

				private static void Postfix(Dictionary<GameObject, Transform> __state)
				{
					if (__state == null)
					{
						return;
					}
					foreach (KeyValuePair<GameObject, Transform> kvp in __state)
					{
						GameObject gameObject = kvp.Key;
						Transform parent = kvp.Value;
						gameObject.transform.parent = parent;
					}
				}
			}

			//[HarmonyPatch(typeof(DungeonGenerator), nameof(DungeonGenerator.Load))]
			private static class DungeonGenerator_Load_Patch
			{
				private static bool Prefix(DungeonGenerator __instance)
				{
					if (!ZNet.instance.IsServer())
					{
						ZLog.LogWarning("DungeonGenerator_Load_Patch Preventing generating dungeon on client.");
						return false;
					}
					Vector2i zone = ZoneSystem.instance.GetZone(__instance.gameObject.transform.position);
					if (ZoneSystem.instance.IsZoneGenerated(zone))
					{
						ZLog.LogWarning("DungeonGenerator_Load_Patch Preventing dungeon load for already generated zone.");
						return false;
					}
					return true;
				}
			}

			//[HarmonyPatch(typeof(TerrainModifier), "PokeHeightmaps")]
			private static class PokeHeightmaps_Patch
			{
				private static bool Prefix()
				{
					return false;
				}
			}

			//[HarmonyPatch(typeof(Heightmap), "Poke")]
			public static class Heightmap_Poke_Patch
			{
				public static Queue<Heightmap> pokeQueue = new Queue<Heightmap>();
				public static bool allowPoke = false;

				private static bool Prefix(Heightmap __instance)
				{
					if (allowPoke)
					{
						return true;
					}
					if (!pokeQueue.Contains(__instance))
					{
						pokeQueue.Enqueue(__instance);
					}
					return false;
				}
			}

			//[HarmonyPatch(typeof(Heightmap), "RebuildCollisionMesh")]
			public static class Heightmap_RebuildCollisionMesh_Patch
			{
				public static Queue<Heightmap> meshRebuildQueue = new Queue<Heightmap>();
				public static bool allowRebuild = false;

				private static bool Prefix(Heightmap __instance)
				{
					if (allowRebuild)
					{
						return true;
					}
					if (!meshRebuildQueue.Contains(__instance))
					{
						meshRebuildQueue.Enqueue(__instance);
					}
					return false;
				}
			}

			//[HarmonyPatch(typeof(ZoneSystem), "SpawnLocation")]
			private static class SpawnLocation_Patch
			{
				public static Dictionary<GameObject, Transform> transformsToRestore = new Dictionary<GameObject, Transform>();

				private static void Prefix(ZoneSystem.ZoneLocation location, ZoneSystem.SpawnMode mode, out Dictionary<GameObject, Transform> __state)
				{
					if (mode == ZoneSystem.SpawnMode.Client)
					{
						__state = transformsToRestore;
						__state.Clear();

						for (int i = location.m_netViews.Count - 1; i >= 0; i--)
						{
							GameObject gameObject = location.m_netViews[i].gameObject;
							if (!gameObject.GetComponent<RandomSpawn>() && gameObject.transform.parent != null)
							{
								__state.Add(gameObject, gameObject.transform.parent);
								gameObject.transform.parent = null;
							}
						}
					}
					else
					{
						__state = null;
					}
				}

				private static void PostInstantiate(Dictionary<GameObject, Transform> __state)
				{
					if (__state == null)
					{
						return;
					}
					foreach (KeyValuePair<GameObject, Transform> kvp in __state)
					{
						GameObject gameObject = kvp.Key;
						Transform parent = kvp.Value;
						gameObject.transform.parent = parent;
					}
				}
			}

			[HarmonyILManipulator]
			[HarmonyPatch(typeof(ZNetScene), "IsAreaReady")]
			private static void Transpile_ZNetScene_IsAreaReady(ILContext il)
			{
				new ILCursor(il)
					.GotoNext(MoveType.Before,
						i => i.MatchLdcI4(1),
						i => i.MatchLdcI4(0)
					)
					.Remove()
					.Emit(OC.Ldc_I4_0)
				;
			}
		}
	}
}
