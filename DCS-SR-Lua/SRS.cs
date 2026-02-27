using Ciribob.DCS.SimpleRadio.Standalone.Common.Helpers;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Network;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Network.Client;
﻿using Microsoft.Win32;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ciribob.DCS.SimpleRadio.Standalone.Lua
{
    struct PlayerInfo
    {
        public string name;
        public int side;
        public int seat;
        public string slot;
    }

    [JsonSerializable(typeof(PlayerInfo))]
    internal partial class SourceGenerationContext : JsonSerializerContext { }
    public sealed class SRS
    {
        sealed class Client
        {
            public static readonly Encoding Encoding = Encoding.UTF8;
            readonly Lock _lock = new();
            readonly UdpClient client = new();

            public int Send(string message, IPEndPoint dest)
            {
                if (string.IsNullOrEmpty(message))
                {
                    return 0;
                }    

                // Uses a line-based protocol.
                var asutf8 = Encoding.GetBytes(message + "\n");
                return Send(asutf8, dest);
            }

            public int Send(ReadOnlySpan<byte> datagram, IPEndPoint dest)
            {
                lock (_lock)
                {
                    return client.Send(datagram, dest);
                }
            }
        }

        readonly Client client = new();
        readonly CancellationTokenSource _cts = new();
        readonly CommandService _commandService;

        sealed class EndPoints
        {
            enum Ports
            {
                // FROM DCS-SRSGameGUI.lua
                PlayerUpdate = 5068,
                Connect = 5069,
                // TO DCS-SRS-OverlayGameGUI.lua
                RadioUpdate = 7080,
                LOSRequests = 9086,
                LOSResults = 9085,
                Export = 9084
            }

            public static readonly IPEndPoint PlayerUpdate = new IPEndPoint(IPAddress.Loopback, (int)Ports.PlayerUpdate);
            public static readonly IPEndPoint Connect = new IPEndPoint(IPAddress.Loopback, (int)Ports.Connect);
            public static readonly IPEndPoint RadioUpdate = new IPEndPoint(IPAddress.Loopback, (int)Ports.RadioUpdate);
            public static readonly IPEndPoint LOSResults = new IPEndPoint(IPAddress.Loopback, (int)Ports.LOSResults);
            public static readonly IPEndPoint LOSRequests = new IPEndPoint(IPAddress.Loopback, (int)Ports.LOSRequests);
            public static readonly IPEndPoint Export = new IPEndPoint(IPAddress.Loopback, (int)Ports.Export);
        }

        #region Radio Updates
        // Radio updates are pushed constantly - we can keep only the last received state.
        readonly Task radioUpdatesWorker;
        readonly ReaderWriterLockSlim _radioUpdateLock = new();
        string LastRadioUpdate
        {
            get
            {
                try
                {
                    _radioUpdateLock.EnterReadLock();
                    return field;
                }
                finally
                {
                    _radioUpdateLock.ExitReadLock();
                }
            }
            set
            {
                try
                {
                    _radioUpdateLock.EnterWriteLock();
                    field = value;
                }
                finally
                {
                    _radioUpdateLock.ExitWriteLock();
                }
            }
        }

        async void RadioUpdateHandler()
        {
            try
            {
                using var client = new UdpClient(EndPoints.RadioUpdate);
                while (true)
                {
                    var update = await client.ReceiveAsync(default);
                    if (update.Buffer.Length > 0)
                    {
                        LastRadioUpdate = Client.Encoding.GetString(update.Buffer);
                    }
                }
            }
            catch (Exception) { /* just eat. TODO: log. */ }
        }
        #endregion Radio Updates

        #region LOS
        // LOS work is a request/response mechanism.
        // They are queued, and the queries are batched and expected to be processed as such.
        readonly Task losRequestsWorker;
        readonly ConcurrentQueue<string> losRequests = new();
        async void LOSRequestHandler()
        {
            try
            {
                using var client = new UdpClient(EndPoints.LOSRequests);
                while (true)
                {
                    var requests = await client.ReceiveAsync(default);
                    if (requests.Buffer.Length > 0)
                    {
                        losRequests.Enqueue(Client.Encoding.GetString(requests.Buffer));
                    }
                }
            }
            catch (Exception) { /* just eat. TODO: log. */ }
        }
        #endregion LOS
        

        SRS()
        {
            _commandService = new(@"command", _cts.Token);
            radioUpdatesWorker = Task.Run(RadioUpdateHandler);
            losRequestsWorker = Task.Run(LOSRequestHandler);
        }

        public static readonly SRS Instance = new();

        static Native.Register[] registry = [
            State.CreateRegister("start_srs", Start_SRS),
            State.CreateRegister("get_srs_path", Get_SRS_Path),
            State.CreateRegister("is_running", Is_Running),
            State.CreateRegister("update_player_info", Update_Player_Info),
            State.CreateRegister("get_player_info", Get_Player_Info),
            State.CreateRegister("send_command", Send_Command),
            State.CreateRegister("send_connect", Send_Connect),
            State.CreateRegister("get_radio_update", Get_Radio_Update),
            State.CreateRegister("get_los_requests", Get_LOS_Requests),
            State.CreateRegister("send_los_results", Send_LOS_Results),
            State.CreateRegister("update_export", Update_Export),
            new()
        ];

        [UnmanagedCallersOnly(EntryPoint = "luaopen_srs")]
        public static int LuaOpen(IntPtr stateIn)
        {
            var lua = new State(stateIn);
            try
            {
                lua.Register("srs", registry);

                // push constants.
                lua.Push(UpdaterChecker.VERSION);
                lua.SetField(-2, "VERSION");

                // Push UDP commands named constants.
                var names = Enum.GetNames(typeof(UDPInterfaceCommand.UDPCommandType));
                // not an array, as many entries as we have in the list.
                using (var builder = lua.CreateTable(0, names.Length))
                {
                    foreach (var name in names)
                    {
                        builder.AddField(name, (int)Enum.Parse<UDPInterfaceCommand.UDPCommandType>(name));
                    }
                }

                // Register the table on the main one.
                lua.SetField(-2, "commands");

            }
            catch (Exception e)
            {
                lua.Push(e.Message);
                return Native.lua_error(lua.Handle);
            }
            return 1;
        }

        static string GetSRSPath()
        {
            return Registry.GetValue("HKEY_CURRENT_USER\\SOFTWARE\\DCS-SR-Standalone", "SRPathStandalone", "")?.ToString();
        }
        static int Start_SRS(IntPtr state)
        {
            var lua = new State(state);
            try
            {
                if (IsRunning())
                {
                    lua.Push(false);
                    return 1;
                }

                var host = lua.CheckString(1);

                var path = Path.Combine([GetSRSPath(), "Client"]);
                using var proc = Process.Start(new ProcessStartInfo
                {
                    WorkingDirectory = path,
                    FileName = Path.Combine([path, "SR-ClientRadio"]),
                    Arguments = $"-host={host}"
                });

                lua.Push(proc.StartTime.Ticks > 0);
            }
            catch (Exception e)
            {
                lua.Push(e.Message);
                return Native.lua_error(lua.Handle);
            }
            
            return 1;
        }

        static int Get_SRS_Path(IntPtr state)
        {
            var lua = new State(state);
            try
            {
                var srsPath = GetSRSPath();
                lua.Push(srsPath);
            }
            catch (Exception e)
            {
                lua.Push(e.Message);
                return Native.lua_error(lua.Handle);
            }

            return 1;
        }

        static bool IsRunning()
        {
            var instances = Process.GetProcessesByName("sr-clientradio");
            // Immediately dispose or we leak
            // https://github.com/mono/mono/issues/10143
            foreach (var instance in instances)
            {
                instance.Dispose();
            }
            return instances.Length > 0;
        }

        static int Is_Running(IntPtr state)
        {
            var lua = new State(state);
            try
            {
                lua.Push(IsRunning());
            }
            catch (Exception e)
            {
                lua.Push(e.Message);
                return Native.lua_error(lua.Handle);
            }
            
            return 1;
        }

        static int Update_Player_Info(IntPtr state)
        {
            var lua = new State(state);
            try
            {
                // Expected arg: {name:str, side:int, seat:int, slot:str}
                // https://www.codingwiththomas.com/blog/a-lua-c-api-cheat-sheet#example-8
                if (!lua.IsTable(1))
                {
                    lua.TypError(1, "table");
                    return 0;
                }

                // https://stackoverflow.com/a/18479566
                // Push keys on the stack.
                lua.GetField(1, "name"); // -4
                lua.GetField(1, "slot"); // -3
                lua.GetField(1, "side"); // -2
                lua.GetField(1, "seat"); // -1

                PlayerInfo update = new()
                {
                    name = lua.CheckString(-4),
                    slot = lua.CheckString(-3),
                    side = lua.CheckInteger(-2),
                    seat = lua.CheckInteger(-1),
                };

                string asJson = JsonSerializer.Serialize(
        update!, SourceGenerationContext.Default.PlayerInfo);

                Instance.Info = update;
                Instance.client.Send(asJson, EndPoints.PlayerUpdate);
            }
            catch (Exception e)
            {
                lua.Push(e.Message);
                return Native.lua_error(lua.Handle);
            }
            

            return 0;
        }

        readonly ReaderWriterLockSlim _infoLock = new();
        PlayerInfo Info
        {
            get
            {
                try
                {
                    _infoLock.EnterReadLock();
                    return field;
                }
                finally { _infoLock.ExitReadLock(); }
            }

            set
            {
                try
                {
                    _infoLock.EnterWriteLock();
                    field = value;
                }
                finally { _infoLock.ExitWriteLock(); }
            }
        } = new();

        static int Get_Player_Info(IntPtr state)
        {
            var lua = new State(state);
            try
            {
                PlayerInfo info = Instance.Info;
                // not an array, 4 key-based entries.
                using (var builder = lua.CreateTable(0, 4))
                {
                    builder.AddField("name", info.name);
                    builder.AddField("slot", info.slot);
                    builder.AddField("side", info.side);
                    builder.AddField("seat", info.seat);
                }
            }
            catch (Exception e)
            {
                lua.Push(e.Message);
                return Native.lua_error(lua.Handle);
            }
            

            return 1;
        }

        static int ForwardMessage(IntPtr state, IPEndPoint dest)
        {
            var lua = new State(state);
            try
            {
                Instance.client.Send(lua.CheckString(1), dest);
            }
            catch (Exception e)
            {
                lua.Push(e.Message);
                return Native.lua_error(lua.Handle);
            }

            return 0;
        }

        static int ForwardMessage(IntPtr state, string message)
        {
            var lua = new State(state);
            try
            {
                lua.Push(message);
            }
            catch (Exception e)
            {
                lua.Push(e.Message);
                return Native.lua_error(lua.Handle);
            }

            return 1;
        }

        static int Send_Command(IntPtr state)
        {
            var lua = new State(state);
            try
            {
                Instance._commandService.SendAsync(lua.CheckString(1));
            }
            catch (Exception e)
            {
                lua.Push(e.Message);
                return Native.lua_error(lua.Handle);
            }

            return 0;
        }

        static int Send_Connect(IntPtr state)
        {
            return ForwardMessage(state, EndPoints.Connect);
        }

        static int Get_Radio_Update(IntPtr state)
        {
            var result = ForwardMessage(state, Instance.LastRadioUpdate);
            Instance.LastRadioUpdate = null;
            return result;
        }

        static int Get_LOS_Requests(IntPtr state)
        {
            string requests = null;
            Instance.losRequests.TryDequeue(out requests);
            var result = ForwardMessage(state, requests);
            return result;
        }

        static int Send_LOS_Results(IntPtr state)
        {
            return ForwardMessage(state, EndPoints.LOSResults);
        }

        static int Update_Export(IntPtr state)
        {
            return ForwardMessage(state, EndPoints.Export);
        }
    }
}
