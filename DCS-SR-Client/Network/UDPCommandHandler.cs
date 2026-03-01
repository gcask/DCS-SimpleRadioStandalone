using Caliburn.Micro;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Network.DCS;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Singletons;
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI.ClientWindow;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Utils;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Models.EventMessages;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Network.Client.Commands;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Network.DCS;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Network.DCS.Models.DCSState;
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

public class UDPCommandHandler : IHandle<LoSRequestMessage>
{
    private static readonly Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    NamedPipeClientStream _pipe;
    readonly Lock _lock = new();

    public void Start(CancellationToken token)
    {
        StartUDPCommandListener(token);
    }

    public async Task HandleAsync(LoSRequestMessage message, CancellationToken token)
    {
        var command = new SRSCommand
        {
            Command = CommandType.LOS_REQUEST,
            LOSRequest = message.Request
        };
        var json = JsonSerializer.Serialize(command);
        await SendAsync(Encoding.UTF8.GetBytes(json), token);
    }

    ValueTask SendAsync(ReadOnlyMemory<byte> message, CancellationToken token)
    {
        if (message.IsEmpty)
        {
            return ValueTask.CompletedTask;
        }

        lock (_lock)
        {
            if (_pipe == null || !_pipe.IsConnected)
            {
                return default;
            }

            return _pipe.WriteAsync(message, token);
        }
    }

    private async void ApplyCommand(SRSCommand message)
    {
        switch (message?.Command)
        {
            case CommandType.FREQUENCY_DELTA:
                RadioHelper.UpdateRadioFrequency(message.Frequency, message.RadioId);
                break;
            case CommandType.FREQUENCY_SET:
                RadioHelper.UpdateRadioFrequency(message.Frequency, message.RadioId, false);
                break;
            case CommandType.ACTIVE_RADIO:
                RadioHelper.SelectRadio(message.RadioId);
                break;
            case CommandType.TOGGLE_GUARD:
                RadioHelper.ToggleGuard(message.RadioId);
                break;
            case CommandType.GUARD:
                RadioHelper.SetGuard(message.RadioId, message.Enabled);
                break;
            case CommandType.CHANNEL_UP:
                RadioHelper.RadioChannelUp(message.RadioId);
                break;
            case CommandType.CHANNEL_DOWN:
                RadioHelper.RadioChannelDown(message.RadioId);
                break;
            case CommandType.SET_VOLUME:
                RadioHelper.SetRadioVolume(message.Volume, message.RadioId);
                break;
            case CommandType.TRANSPONDER_POWER:
                TransponderHelper.SetPower(message.Enabled);
                break;
            case CommandType.TRANSPONDER_M1_CODE:
                TransponderHelper.SetMode1(message.Code);
                break;
            case CommandType.TRANSPONDER_M2_CODE:
                TransponderHelper.SetMode2(message.Code);
                break;
            case CommandType.TRANSPONDER_M3_CODE:
                TransponderHelper.SetMode3(message.Code);
                break;
            case CommandType.TRANSPONDER_M4:
                TransponderHelper.SetMode4(message.Enabled);
                break;
            case CommandType.TRANSPONDER_IDENT:
                TransponderHelper.SetIdent(message.Enabled);
                break;
            case CommandType.CONNECT:
                await ExecuteConnectAsync(message.Address);
                break;
            case CommandType.PLAYER_INFO:
                await ClientStateSingleton.Instance.UpdatePlayerInfoAsync(message.PlayerInfo);
                break;
            case CommandType.RADIO_INFO:
                await PublishRadioInfoAsync(message.RadioInfo);
                break;
            case CommandType.LOS_RESULT:
                await PublishLoSResultAsync(message.LOSResult);
                break;
            default:
                Logger.Error("Unknown UDP Command!");
                break;
        }
    }

    async Task PublishLoSResultAsync(DCSLosCheckResult result)
    {
        await EventBus.Instance.PublishOnBackgroundThreadAsync(new LoSResultMessage()
        {
            Result = result
        });
    }

    async Task PublishRadioInfoAsync(DCSPlayerRadioInfo info)
    {
        await EventBus.Instance.PublishOnBackgroundThreadAsync(new RadioUpdateMessage()
        {
            RadioInfo = info
        });
    }

    private async Task ExecuteConnectAsync(string desired)
    {
        await Application.Current.Dispatcher.InvokeAsync(async () =>
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
        EventBus.Instance.SubscribeOnBackgroundThread(this);
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
                    lock (_lock)
                    {
                        _pipe = client;
                    }
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
                            var message = JsonSerializer.Deserialize<SRSCommand>(bytes, serializerOptions);
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
                finally
                {
                    lock (_lock)
                    {
                        _pipe = null;
                    }
                }
            }
           
        }, token);
    }

    public void Stop()
    {
        EventBus.Instance.Unsubscribe(this);
    }
}