using HarmonyLib;
using System.Collections.Generic;
using System;
using System.Linq;

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

		public static void FindSectorsSurrounding(Vector2i sector, int area, int distantArea, List<Vector2i> nearbySectors, List<Vector2i> distantSectors = null)
		/*
			Find nearby and distant sectors surrounding a given sector.
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

			List<Vector2i> sectors = (distantSectors != null) ? distantSectors : nearbySectors;
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

		public static void FindActiveSectors(int area, int distantArea, List<Vector2i> nearbySectors, List<Vector2i> distantSectors = null)
		/*
			Find surrounding nearby and distant sectors for all active peers and deduplicate list.
		*/
		{
			foreach (ZNetPeer peer in ZNet.instance.GetPeers())
			{
				Vector2i sector = ZoneSystem.GetZone(peer.GetRefPos());
				FindSectorsSurrounding(sector, area, distantArea, nearbySectors, distantSectors);
			}
			nearbySectors = nearbySectors.Distinct().ToList();

			if (distantSectors != null)
			{
				// Remove all nearbySectors from distantSectors (deduplication)
				distantSectors = distantSectors.Distinct().Except(nearbySectors).ToList();
			}
		}

		public static void FindObjectsInSectors(List<Vector2i> sectors, List<ZDO> objects)
		{
			foreach (Vector2i sector in sectors)
			{
				ZDOMan.instance.FindObjects(sector, objects);
			}
		}

		public static void FindDistantObjectsInSectors(List<Vector2i> sectors, List<ZDO> objects)
		{
			foreach (Vector2i sector in sectors)
			{
				ZDOMan.instance.FindDistantObjects(sector, objects);
			}
		}
	}
}
