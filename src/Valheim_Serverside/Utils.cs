using HarmonyLib;
using System.Collections.Generic;
using System;

namespace Valheim_Serverside
{
	public class Utilities
	{
		public static bool IsDebugBuild()
		{
#if DEBUG
			return true;
#endif
			return false;
		}

		public static void FindActiveSectors(Vector2i sector, int area, int distantArea, HashSet<Vector2i> nearbySectors, HashSet<Vector2i> distantSectors = null)
		/*
			Same logic as as ZDOMan.FindSectorObjects; output sectors instead of objects.
		*/
		{
			nearbySectors.Add(sector);
			for (int i = 1; i <= area; i++)
			{
				for (int j = sector.x - i; j <= sector.x + i; j++)
				{
					nearbySectors.Add(new Vector2i(j, sector.y - i));
					nearbySectors.Add(new Vector2i(j, sector.y + i));
				}
				for (int k = sector.y - i + 1; k <= sector.y + i - 1; k++)
				{
					nearbySectors.Add(new Vector2i(sector.x - i, k));
					nearbySectors.Add(new Vector2i(sector.x + i, k));
				}
			}

			HashSet<Vector2i> sectors = (distantSectors != null) ? distantSectors : nearbySectors;
			for (int l = area + 1; l <= area + distantArea; l++)
			{
				for (int m = sector.x - l; m <= sector.x + l; m++)
				{
					sectors.Add(new Vector2i(m, sector.y - l));
					sectors.Add(new Vector2i(m, sector.y + l));
				}
				for (int n = sector.y - l + 1; n <= sector.y + l - 1; n++)
				{
					sectors.Add(new Vector2i(sector.x - l, n));
					sectors.Add(new Vector2i(sector.x + l, n));
				}
			}
		}

		public static void FindAllActiveSectors(int area, int distantArea, HashSet<Vector2i> nearbySectors, HashSet<Vector2i> distantSectors = null)
		{
			foreach (ZNetPeer peer in ZNet.instance.GetPeers())
			{
				Vector2i sector = ZoneSystem.instance.GetZone(peer.GetRefPos());
				FindActiveSectors(sector, area, distantArea, nearbySectors, distantSectors);
			}

			if (distantSectors != null)
			{
				// Remove all nearbySectors from distantSectors (deduplication)
				distantSectors.ExceptWith(nearbySectors);
			}
		}

		public static void FindObjectsInSectors(HashSet<Vector2i> sectors, List<ZDO> objects)
		{
			foreach (Vector2i sector in sectors)
			{
				ZDOMan.instance.FindObjects(sector, objects);
			}
		}

		public static void FindDistantObjectsInSectors(HashSet<Vector2i> sectors, List<ZDO> objects)
		{
			foreach (Vector2i sector in sectors)
			{
				ZDOMan.instance.FindDistantObjects(sector, objects);
			}
		}
	}
}
