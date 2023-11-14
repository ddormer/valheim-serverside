using FeaturesLib;
using HarmonyLib;
using MonoMod.Cil;
using PluginConfiguration;
using UnityEngine;
using OC = Mono.Cecil.Cil.OpCodes;

namespace Valheim_Serverside.Features
{
	public class ServerOwnsPiece : IFeature
	{
		public bool FeatureEnabled()
		{
			return Configuration.serverOwnsPiece.Value;
		}

		[HarmonyPatch(typeof(ZDOMan), "RPC_ZDOData")]
		public class ZDOMan_RPC_ZDODataPatch
		{
			static void CheckShouldOwn(ZDO zdo, bool isNew)
			{
				long myid = ZNet.GetUID();
				long owner = zdo.GetOwner();
				if (isNew && owner != 0L && owner != myid)
				{
					int prefabHash = zdo.GetPrefab();
					GameObject prefab = ZNetScene.instance.GetPrefab(prefabHash);
					// Only take control of building pieces for now
					// TODO: See if this should be expanded
					if (prefab != null && prefab.GetComponent<Piece>() != null)
					{
#if DEBUG
						ServersidePlugin.logger.LogMessage($"Taking ownership of new ZDO (player id: {owner} id: {zdo.m_uid} name: {prefab.name}");
#endif
						zdo.SetOwner(myid);
						// Increment owner revision to prevent owner getting desynced with high latency clients
						zdo.OwnerRevision += 10;
						zdo.DataRevision += 10;
						return;
					}
				}
			}

			public static void ILManipulator(ILContext il)
			{
				int idx_stZDO = 0;
				int idx_stZDOIsNew = 0;

				new ILCursor(il)
					.GotoNext(
						i => i.MatchCall<ZDOMan>("CreateNewZDO"),
						i => i.MatchStloc(out idx_stZDO),
						i => i.MatchLdcI4(out _),
						i => i.MatchStloc(out idx_stZDOIsNew)
					)
					.GotoNext(MoveType.After,
						i => i.MatchLdloc(out _),
						i => i.MatchLdloc(out _),
						i => i.MatchCallvirt<ZDO>("Deserialize")
					)
					.Emit(OC.Ldloc, idx_stZDO)
					.Emit(OC.Ldloc, idx_stZDOIsNew)
					.Emit(OC.Call, AccessTools.Method(typeof(ZDOMan_RPC_ZDODataPatch),
													  nameof(ZDOMan_RPC_ZDODataPatch.CheckShouldOwn)))
				;
			}
		}
	}
}
