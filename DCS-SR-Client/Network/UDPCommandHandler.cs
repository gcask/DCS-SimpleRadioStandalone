using Ciribob.DCS.SimpleRadio.Standalone.Client.Utils;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Network;
using NLog;
using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Network;

public class UDPCommandHandler
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public void Start(CancellationToken token)
    {
        StartUDPCommandListener(token);
    }

    private void ApplyCommand(UDPInterfaceCommand message)
    {
        if (message?.Command == UDPInterfaceCommand.UDPCommandType.FREQUENCY_DELTA)
            RadioHelper.UpdateRadioFrequency(message.Frequency, message.RadioId);
        else if (message?.Command == UDPInterfaceCommand.UDPCommandType.FREQUENCY_SET)
            RadioHelper.UpdateRadioFrequency(message.Frequency, message.RadioId, false);
        else if (message?.Command == UDPInterfaceCommand.UDPCommandType.ACTIVE_RADIO)
            RadioHelper.SelectRadio(message.RadioId);
        else if (message?.Command == UDPInterfaceCommand.UDPCommandType.TOGGLE_GUARD)
            RadioHelper.ToggleGuard(message.RadioId);
        else if (message?.Command == UDPInterfaceCommand.UDPCommandType.GUARD)
            RadioHelper.SetGuard(message.RadioId, message.Enabled);
        else if (message?.Command == UDPInterfaceCommand.UDPCommandType.CHANNEL_UP)
            RadioHelper.RadioChannelUp(message.RadioId);
        else if (message?.Command == UDPInterfaceCommand.UDPCommandType.CHANNEL_DOWN)
            RadioHelper.RadioChannelDown(message.RadioId);
        else if (message?.Command == UDPInterfaceCommand.UDPCommandType.SET_VOLUME)
            RadioHelper.SetRadioVolume(message.Volume, message.RadioId);
        else if (message?.Command == UDPInterfaceCommand.UDPCommandType.TRANSPONDER_POWER)
            TransponderHelper.SetPower(message.Enabled);
        else if (message?.Command == UDPInterfaceCommand.UDPCommandType.TRANSPONDER_M1_CODE)
            TransponderHelper.SetMode1(message.Code);
        else if (message?.Command == UDPInterfaceCommand.UDPCommandType.TRANSPONDER_M2_CODE)
            TransponderHelper.SetMode2(message.Code);
        else if (message?.Command == UDPInterfaceCommand.UDPCommandType.TRANSPONDER_M3_CODE)
            TransponderHelper.SetMode3(message.Code);
        else if (message?.Command == UDPInterfaceCommand.UDPCommandType.TRANSPONDER_M4)
            TransponderHelper.SetMode4(message.Enabled);
        else if (message?.Command == UDPInterfaceCommand.UDPCommandType.TRANSPONDER_IDENT)
            TransponderHelper.SetIdent(message.Enabled);
        else
            Logger.Error("Unknown UDP Command!");
    }

    private void StartUDPCommandListener(CancellationToken token)
    {
        Task.Run(async () =>
        {
            while (true)
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    using var client = new NamedPipeClientStream(@".", @"dcssimpleradiostandalone\command", PipeAccessRights.ReadData | PipeAccessRights.WriteData | PipeAccessRights.WriteAttributes,
                             PipeOptions.Asynchronous,
                             System.Security.Principal.TokenImpersonationLevel.None,
                             HandleInheritability.None);
                    Logger.Info("Command pipe client set up - awaiting connection.");
                    await client.ConnectAsync(token);
                    client.ReadMode = PipeTransmissionMode.Message;
                    Logger.Info("Command pipe client connected.");
                    var buffer = new byte[1024];
                    var serializerOptions = new JsonSerializerOptions() { IncludeFields = true, PropertyNameCaseInsensitive = true, };
                    while (client.IsConnected)
                    {
                        using (var stream = new MemoryStream())
                        {
                            do
                            {
                                var read = await client.ReadAsync(buffer, token);
                                await stream.WriteAsync(buffer.AsMemory(0, read), token);
                            } while (!client.IsMessageComplete);

                            var bytes = Encoding.UTF8.GetString(stream.GetBuffer().AsSpan(0, (int)stream.Length));
                            var message = JsonSerializer.Deserialize<UDPInterfaceCommand>(bytes, serializerOptions);
                            ApplyCommand(message);
                        }
                    }
                }
                catch (OperationCanceledException e)
                {
                    if (e.CancellationToken != token)
                    {
                        Logger.Error(e, "Exception handling Named pipe");
                    }
                    else
                    {
                        throw;
                    }
                }
                catch (Exception e)
                {
                    Logger.Error(e, "Exception handling Named pipe");
                }
            }
           
        }, token);
    }

    public void Stop()
    {
    }
}