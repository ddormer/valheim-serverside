using HarmonyLib;
using MonoMod.Cil;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using OC = Mono.Cecil.Cil.OpCodes;

namespace Valheim_Serverside.Features
{
	[Harmony]
	public class NetworkDump : FeaturesLib.IFeature
	{
		public static Mutex mut = new Mutex();
		public static ConcurrentBag<byte[]> network_bytes = new ConcurrentBag<byte[]>();
		public static System.Timers.Timer saveTimer;

		public NetworkDump()
		{
			saveTimer = new System.Timers.Timer(10000);
			saveTimer.Elapsed += SaveTimer_Elapsed;
			saveTimer.Start();
		}

		private void SaveTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
		{
			ZLog.LogWarning("Saving network data to disk..");
			Thread newThread = new Thread(new ThreadStart(save));
			newThread.Start();
		}

		static async Task _write(byte[] data, string filename, int offset = 0)
		{
			using (var stream = new FileStream(filename, FileMode.Append))
			{
				await stream.WriteAsync(data, offset, data.Length);
			}
		}

		public static void AddData(byte[] data)
		{
			mut.WaitOne();
			network_bytes.Add(data);
			//lock (network_bytes)
			//{
			//	network_bytes.Add(data);
			//}
			//ZLog.Log("Adding bytes " + data.Length);
			mut.ReleaseMutex();
		}

		public static List<byte[]> PopData()
		{
			mut.WaitOne();
			List<byte[]> datas = new List<byte[]>();

			try
			{
				List<Task> bagConsumeTasks = new List<Task>();
				while (!network_bytes.IsEmpty)
				{
					bagConsumeTasks.Add(Task.Run(() => {
						byte[] item;
						if (network_bytes.TryTake(out item))
						{
							datas.Add(item);
						}
					}));
				}
				Task.WaitAll(bagConsumeTasks.ToArray());
			}
			finally
			{
				mut.ReleaseMutex();
			}
			return datas;
		}

		private static void save()
		{
			List<byte[]> datas = PopData();
			ZLog.LogWarning("Saving " + datas.Count + "files..");
			foreach (byte[] data in datas)
			{
				_write(data, "C:\\Users\\potato\\Downloads\\VH\\" + Guid.NewGuid().ToString() + ".dat");
			}
		}

		public bool FeatureEnabled()
		{
			return true;
		}

		[HarmonyPatch(typeof(ZSteamSocket))]
		public static class ZSteamSocket_Patches
		{
			public static List<byte[]> buffer = new List<byte[]>();

			[HarmonyILManipulator]
			[HarmonyPatch(typeof(ZSteamSocket), "SendQueuedPackages")]
			public static void Transpile_SendQueuedPackages(ILContext il)
			{
				var cursor = new ILCursor(il);
				cursor
					.GotoNext(MoveType.After,
						i => i.MatchCallvirt(AccessTools.Method(typeof(Queue<byte[]>), "Peek"))
					)
					// Push "this" (ZSteamSocket instance) to call stack
					.Emit(OC.Ldarg_0)
					// 
					.EmitDelegate<Func<byte[], ZSteamSocket, byte[]>>(ProcessBytes)
				;
			}
			[HarmonyILManipulator]
			[HarmonyPatch(nameof(ZSteamSocket.Recv))]
			public static void Transpile_Recv(ILContext il)
			{
				var cursor = new ILCursor(il);

				int idxZpkg = 0;
				cursor
					.GotoNext(MoveType.After,
						i => i.MatchCall(AccessTools.Method(typeof(Marshal), nameof(Marshal.Copy), new Type[] { typeof(IntPtr), typeof(byte[]), typeof(int), typeof(int) })),
						i => i.MatchLdloc(out _),
						i => i.MatchNewobj<ZPackage>()
					)
					.GotoPrev(MoveType.After,
						i => i.MatchLdloc(out idxZpkg)
					)
					.Emit(OC.Ldarg_0)
					.EmitDelegate<Func<byte[], ZSteamSocket, byte[]>>(ProcessBytes)
				;
			}

			private static byte[] ProcessBytes(byte[] data, ZSteamSocket arg2)
			{
				AddData(data);
				return data;
			}
		}
	}
}


