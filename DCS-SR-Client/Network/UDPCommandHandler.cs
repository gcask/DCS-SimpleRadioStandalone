using Ciribob.DCS.SimpleRadio.Standalone.Client.UI.ClientWindow;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Utils;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Network;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Network.Singletons;
using NLog;
using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

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
        switch (message?.Command)
        {
            case UDPInterfaceCommand.UDPCommandType.FREQUENCY_DELTA:
                RadioHelper.UpdateRadioFrequency(message.Frequency, message.RadioId);
                break;
            case UDPInterfaceCommand.UDPCommandType.FREQUENCY_SET:
                RadioHelper.UpdateRadioFrequency(message.Frequency, message.RadioId, false);
                break;
            case UDPInterfaceCommand.UDPCommandType.ACTIVE_RADIO:
                RadioHelper.SelectRadio(message.RadioId);
                break;
            case UDPInterfaceCommand.UDPCommandType.TOGGLE_GUARD:
                RadioHelper.ToggleGuard(message.RadioId);
                break;
            case UDPInterfaceCommand.UDPCommandType.GUARD:
                RadioHelper.SetGuard(message.RadioId, message.Enabled);
                break;
            case UDPInterfaceCommand.UDPCommandType.CHANNEL_UP:
                RadioHelper.RadioChannelUp(message.RadioId);
                break;
            case UDPInterfaceCommand.UDPCommandType.CHANNEL_DOWN:
                RadioHelper.RadioChannelDown(message.RadioId);
                break;
            case UDPInterfaceCommand.UDPCommandType.SET_VOLUME:
                RadioHelper.SetRadioVolume(message.Volume, message.RadioId);
                break;
            case UDPInterfaceCommand.UDPCommandType.TRANSPONDER_POWER:
                TransponderHelper.SetPower(message.Enabled);
                break;
            case UDPInterfaceCommand.UDPCommandType.TRANSPONDER_M1_CODE:
                TransponderHelper.SetMode1(message.Code);
                break;
            case UDPInterfaceCommand.UDPCommandType.TRANSPONDER_M2_CODE:
                TransponderHelper.SetMode2(message.Code);
                break;
            case UDPInterfaceCommand.UDPCommandType.TRANSPONDER_M3_CODE:
                TransponderHelper.SetMode3(message.Code);
                break;
            case UDPInterfaceCommand.UDPCommandType.TRANSPONDER_M4:
                TransponderHelper.SetMode4(message.Enabled);
                break;
            case UDPInterfaceCommand.UDPCommandType.TRANSPONDER_IDENT:
                TransponderHelper.SetIdent(message.Enabled);
                break;
            case UDPInterfaceCommand.UDPCommandType.CONNECT:
                ExecuteConnect(message.Address);
                break;
            default:
                Logger.Error("Unknown UDP Command!");
                break;
        }
    }

    private void ExecuteConnect(string desired)
    {
        Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                desired = desired.Trim();
                var address = desired.Split(':');
                AutoConnectMessage message = null;
                if (desired.Contains(':'))
                {
                    message = new AutoConnectMessage()
                    {
                        Address = $"{address[0].Trim()}:{address[1].Trim()}"
                    };
                }
                else
                {
                    message = new AutoConnectMessage()
                    {
                        Address = $"{address[0].Trim()}:5002"
                    };
                }

                if (message != null)
                {
                    await EventBus.Instance.PublishOnUIThreadAsync(message);
                }
            
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Exception Parsing DCS AutoConnect Message");
            }

        }, DispatcherPriority.Background);
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