using System.Collections.Generic;
using RoR2;
using RoR2.Networking;
using UnityEngine;
using UnityEngine.Networking;

namespace WhereToDie
{
    // Host -> modded client sync of the zone markers, without sending anything a vanilla peer can't handle
    // (same scheme as KarmaLocking):
    //   client -> host: the "wheretodie_hello" console command, flagged ExecuteOnServer. A vanilla host
    //                   just logs "not a recognized ConCommand".
    //   host -> client: a custom message, only to connections that said hello. A vanilla client that receives
    //                   an unknown message ID drops the rest of that packet, so it never gets one.
    internal static class ZoneNetwork
    {
        // Vanilla uses 48-88; KarmaLocking uses 0x4B4C. "WD" = 0x5744.
        internal const short MarkersMessageType = 0x5744;
        private const byte ProtocolVersion = 1;

        private const byte FlagDies = 1;
        private const byte FlagStageWide = 2;
        private const byte FlagTube = 4;

        private static readonly HashSet<NetworkConnection> moddedConnections = new HashSet<NetworkConnection>();

        // Clients only: whether the host has the mod (and so whether markers come from it).
        internal static bool HostHasMod { get; private set; }

        internal static void Init()
        {
            NetworkUser.onPostNetworkUserStart += OnPostNetworkUserStart;
            NetworkManagerSystem.onStartClientGlobal += _ => ResetClientState();
            NetworkManagerSystem.onStopClientGlobal += ResetClientState;
            NetworkManagerSystem.onStartServerGlobal += moddedConnections.Clear;
            NetworkManagerSystem.onStopServerGlobal += moddedConnections.Clear;
            NetworkManagerSystem.onServerDisconnectGlobal += conn => moddedConnections.Remove(conn);
        }

        private static void ResetClientState()
        {
            HostHasMod = false;
            if (!NetworkServer.active)
            {
                ZoneTracker.SetMarkers(new List<ZoneMarker>());
            }
        }

        private static void OnPostNetworkUserStart(NetworkUser networkUser)
        {
            if (!NetworkServer.active && networkUser && networkUser.isLocalPlayer)
            {
                RoR2.Console.instance.SubmitCmd(networkUser, "wheretodie_hello");
            }
        }

        #region Host

        internal static void BroadcastMarkers()
        {
            if (!NetworkServer.active)
            {
                return;
            }
            moddedConnections.RemoveWhere(conn => conn == null || !conn.isConnected);
            foreach (var conn in moddedConnections)
            {
                SendMarkers(conn);
            }
        }

        private static void SendMarkers(NetworkConnection conn)
        {
            conn.SendWriter(WriteMarkers(ZoneTracker.Markers), QosChannelIndex.defaultReliable.intVal);
        }

        // The whole message (header included); also used by the test driver to check the round trip.
        internal static NetworkWriter WriteMarkers(List<ZoneMarker> markers)
        {
            var writer = new NetworkWriter();
            writer.StartMessage(MarkersMessageType);
            writer.Write(ProtocolVersion);
            writer.Write((byte)Mathf.Min(markers.Count, byte.MaxValue));
            for (int i = 0; i < markers.Count && i < byte.MaxValue; i++)
            {
                var marker = markers[i];
                byte flags = 0;
                if (marker.Dies)
                {
                    flags |= FlagDies;
                }
                if (marker.StageWide)
                {
                    flags |= FlagStageWide;
                }
                if (marker.Tube)
                {
                    flags |= FlagTube;
                }
                writer.Write(marker.Position);
                writer.Write(marker.Radius);
                writer.Write(flags);
            }
            writer.FinishMessage();
            return writer;
        }

        // The message body (after the header). Null if the host speaks another protocol version.
        internal static List<ZoneMarker> ReadMarkers(NetworkReader reader)
        {
            byte version = reader.ReadByte();
            if (version != ProtocolVersion)
            {
                WhereToDiePlugin.Log.LogWarning($"Host uses a different WhereToDie protocol ({version}, expected {ProtocolVersion}); update the mod.");
                return null;
            }
            int count = reader.ReadByte();
            var markers = new List<ZoneMarker>(count);
            for (int i = 0; i < count; i++)
            {
                var position = reader.ReadVector3();
                float radius = reader.ReadSingle();
                byte flags = reader.ReadByte();
                markers.Add(new ZoneMarker
                {
                    Position = position,
                    Radius = radius,
                    Dies = (flags & FlagDies) != 0,
                    StageWide = (flags & FlagStageWide) != 0,
                    Tube = (flags & FlagTube) != 0,
                });
            }
            return markers;
        }

        [ConCommand(commandName = "wheretodie_hello", flags = ConVarFlags.ExecuteOnServer,
            helpText = "Sent by clients with WhereToDie so the host shares where the Page of Reincarnation works.")]
        private static void CCHello(ConCommandArgs args)
        {
            if (!NetworkServer.active || !args.sender || args.sender.isLocalPlayer)
            {
                return;
            }
            var conn = args.sender.connectionToClient;
            if (conn == null || !conn.isConnected)
            {
                return;
            }
            if (moddedConnections.Add(conn))
            {
                WhereToDiePlugin.Log.LogInfo($"{args.sender.userName} has WhereToDie.");
            }
            SendMarkers(conn);
        }

        #endregion

        #region Client

        [NetworkMessageHandler(msgType = MarkersMessageType, client = true)]
        private static void HandleMarkers(NetworkMessage netMsg)
        {
            if (NetworkServer.active)
            {
                return;
            }
            var markers = ReadMarkers(netMsg.reader);
            if (markers == null)
            {
                return;
            }
            HostHasMod = true;
            ZoneTracker.SetMarkers(markers);
        }

        #endregion

        [ConCommand(commandName = "wheretodie_status", flags = ConVarFlags.None,
            helpText = "Prints the places where the Page of Reincarnation currently works.")]
        private static void CCStatus(ConCommandArgs args)
        {
            string role = NetworkServer.active ? "host" : NetworkClient.active ? "client" : "offline";
            Debug.Log($"[WhereToDie] role={role} hostHasMod={(NetworkServer.active || HostHasMod)} moddedClients={moddedConnections.Count} "
                + $"markers={ZoneTracker.Markers.Count} {PageFixes.StatusText()}");
            foreach (var marker in ZoneTracker.Markers)
            {
                Debug.Log($"[WhereToDie]   at {marker.Position} radius={marker.Radius:0.#} dies={marker.Dies} stageWide={marker.StageWide} tube={marker.Tube}");
            }
            if (NetworkServer.active)
            {
                foreach (var zone in ZoneTracker.DescribeZones())
                {
                    Debug.Log("[WhereToDie]   zone " + zone);
                }
            }
        }
    }
}
