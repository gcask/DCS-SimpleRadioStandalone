using Ciribob.DCS.SimpleRadio.Standalone.Common.Helpers;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Network.Client.Commands;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Network.DCS;
using Microsoft.Win32;
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
    record PlayerInfo
    {
        public string Name { get; set; }
        public int Side { get; set; }
        public int Seat { get; set; }
        public string Slot { get; set; }
    }

    [JsonSerializable(typeof(SRSCommand))]
    internal partial class SourceGenerationContext : JsonSerializerContext { }
    public sealed class SRS
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        sealed class Client
        {
            public static readonly Encoding Encoding = Encoding.UTF8;
            readonly Lock _lock = new();
            readonly UdpClient client = new();

            public int Send(string message, IPEndPoint dest)
            {
                Logger.Debug("Sending {message} to {dest}", message, dest);
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
                // TO DCS-SRS-OverlayGameGUI.lua
                RadioUpdate = 7080,
            }

            public static readonly IPEndPoint RadioUpdate = new IPEndPoint(IPAddress.Loopback, (int)Ports.RadioUpdate);
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
        internal readonly ConcurrentQueue<DCSLosCheckRequest> losRequests = new();
        #endregion LOS


        SRS()
        {
            NLog.LogManager.Configuration = new NLog.Config.XmlLoggingConfiguration(Path.Combine([GetSRSPath(), "Client", "NLog.config"]));
            _commandService = new(@"command", _cts.Token);
            radioUpdatesWorker = Task.Run(RadioUpdateHandler);
            Logger.Info("SRS Lua Library loaded.");
        }

        public static readonly SRS Instance = new();

        static Native.Register[] registry = [
            State.CreateRegister("start_srs", Start_SRS),
            State.CreateRegister("get_srs_path", Get_SRS_Path),
            State.CreateRegister("is_running", Is_Running),
            State.CreateRegister("update_player_info", Update_Player_Info),
            State.CreateRegister("get_player_info", Get_Player_Info),
            State.CreateRegister("send_command", Send_Command),
            State.CreateRegister("get_radio_update", Get_Radio_Update),
            State.CreateRegister("pop_los_request", Pop_LOS_Request),
            State.CreateRegister("push_los_result", Push_LOS_Result),
            new()
        ];

        [UnmanagedCallersOnly(EntryPoint = "luaopen_srs")]
        public static int LuaOpen(IntPtr stateIn)
        {
            var lua = new State(stateIn);
            try
            {
                Logger.Debug("SRS LuaOpen for state {stateIn}", stateIn);
                lua.Register("srs", registry);

                // push constants.
                lua.Push(UpdaterChecker.VERSION);
                lua.SetField(-2, "VERSION");

                // Push UDP commands named constants.
                var names = Enum.GetNames(typeof(CommandType));
                // not an array, as many entries as we have in the list.
                using (var builder = lua.CreateTable(0, names.Length))
                {
                    foreach (var name in names)
                    {
                        builder.AddField(name, (int)Enum.Parse<CommandType>(name));
                    }
                }

                // Register the table on the main one.
                lua.SetField(-2, "commands");

            }
            catch (Exception e)
            {
                Logger.Error(e, "Error in open");
                lua.Push(e.Message);
                return Native.lua_error(lua.Handle);
            }
            return 1;
        }

        static string GetSRSPath()
        {
            return Registry.GetValue(@"HKEY_CURRENT_USER\SOFTWARE\DCS-SR-Standalone", "SRPathStandalone", "")?.ToString();
        }
        static int Start_SRS(IntPtr state)
        {
            var lua = new State(state);
            try
            {
                var host = lua.CheckString(1);
                Logger.Trace("start_srs({host})", host);
                if (IsRunning())
                {
                    Logger.Debug("SRS already running.");
                    lua.Push(false);
                    return 1;
                }


                var path = Path.Combine([GetSRSPath(), "Client"]);
                Logger.Info("Launching SRS at {path}", path);
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
                Logger.Error(e, "start_srs");
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
                Logger.Trace("get_srs_path()");
                var srsPath = GetSRSPath();
                Logger.Trace(srsPath);
                lua.Push(srsPath);
            }
            catch (Exception e)
            {
                Logger.Error(e, "get_srs_path");
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
                Logger.Trace("is_running()");
                lua.Push(IsRunning());
            }
            catch (Exception e)
            {
                Logger.Error(e, "is_running");
                lua.Push(e.Message);
                return Native.lua_error(lua.Handle);
            }
            
            return 1;
        }

        async ValueTask SendCommandAsync(SRSCommand command)
        {
            try
            {
                Logger.Debug("Sending command {type}", command.Command);
                string asJson = JsonSerializer.Serialize(command, SourceGenerationContext.Default.SRSCommand);
                await _commandService.SendAsync(asJson);
            }
            catch (Exception e)
            {
                Logger.Warn(e, "Error sending command");
            }
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
                    Name = lua.CheckString(-4),
                    Slot = lua.CheckString(-3),
                    Side = lua.CheckInteger(-2),
                    Seat = lua.CheckInteger(-1),
                };

                Task.Run(async () => await Instance.SendCommandAsync(new()
                {
                    Command = CommandType.PLAYER_INFO,
                    PlayerInfo = new()
                    {
                        Name = update.Name,
                        Seat = update.Seat,
                        Side = update.Side,
                    }
                }));

                Instance.Info = update;
            }
            catch (Exception e)
            {
                Logger.Error(e, "update_player_info");
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
                    builder.AddField("name", info.Name);
                    builder.AddField("slot", info.Slot);
                    builder.AddField("side", info.Side);
                    builder.AddField("seat", info.Seat);
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, "get_player_info");
                lua.Push(e.Message);
                return Native.lua_error(lua.Handle);
            }
            

            return 1;
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
                Logger.Error(e, "Forward message to lua");
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
                var command = lua.CheckString(1);
                Logger.Debug("Sending command {command}", command);
                Instance._commandService.SendAsync(command);
            }
            catch (Exception e)
            {
                Logger.Error(e, "send_command");
                lua.Push(e.Message);
                return Native.lua_error(lua.Handle);
            }

            return 0;
        }

        static int Get_Radio_Update(IntPtr state)
        {
            Logger.Trace("get_radio_update");
            var result = ForwardMessage(state, Instance.LastRadioUpdate);
            Instance.LastRadioUpdate = null;
            return result;
        }

        static int Pop_LOS_Request(IntPtr state)
        {
            Logger.Trace("pop_los_request()");
            var lua = new State(state);
            try
            {
                if (Instance.losRequests.TryDequeue(out var request))
                {
                    using (var luaRequest = lua.CreateTable(0, 2))
                    {
                        luaRequest.AddField("id", request.ID);
                        lua.Push("position");
                        using (var pos = lua.CreateTable(0, 3))
                        {
                            pos.AddField("lat", request.Position.lat);
                            pos.AddField("lng", request.Position.lng);
                            pos.AddField("alt", request.Position.alt);
                        }
                        Native.lua_settable(lua.Handle, -3);
                    }

                    return 1;
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, "send_command");
                lua.Push(e.Message);
                return Native.lua_error(lua.Handle);
            }

            return 0;
        }

        static int Push_LOS_Result(IntPtr state)
        {
            Logger.Trace("push_los_request()");
            var lua = new State(state);
            try
            {
                if (!lua.IsTable(1))
                {
                    Logger.Warn("Expected table, got something else.");
                    lua.TypError(1, "table");
                    return 0;
                }

                lua.GetField(1, "id"); // -2
                lua.GetField(1, "los"); // -1

                DCSLosCheckResult update = new()
                {
                    ID = lua.CheckString(-2),
                    LoS = (float)lua.CheckNumber(-1),
                };

                Task.Run(async () => await Instance.SendCommandAsync(new()
                {
                    Command = CommandType.LOS_RESULT,
                    LOSResult = update,
                }));
            }
            catch (Exception e)
            {
                Logger.Error(e, "push_los_request");
                lua.Push(e.Message);
                return Native.lua_error(lua.Handle);
            }
            return 0;
        }
    }
}
