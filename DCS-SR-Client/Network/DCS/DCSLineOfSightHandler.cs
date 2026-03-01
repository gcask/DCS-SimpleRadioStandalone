using Caliburn.Micro;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Singletons;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Helpers;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Models.EventMessages;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Models.Player;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Network.DCS;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Network.Singletons;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Settings;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Settings.Setting;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Network.DCS;

public class DCSLineOfSightHandler : IHandle<LoSResultMessage>
{
    private static readonly Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private readonly ConnectedClientsSingleton _clients = ConnectedClientsSingleton.Instance;
    private readonly string _guid;
    private readonly SyncedServerSettings _serverSettings = SyncedServerSettings.Instance;

    public DCSLineOfSightHandler(string guid)
    {
        _guid = guid;
    }

    public async Task HandleAsync(LoSResultMessage message, CancellationToken cancellationToken)
    {
        if (_clients.TryGetValue(message.Result.ID, out var client))
        {
            client.LineOfSightLoss = message.Result.LoS;
        }
    }

    public async Task Start(CancellationToken token)
    {
        await StartDCSLOSSender(token);
    }

    private async Task StartDCSLOSSender(CancellationToken token)
    {
        EventBus.Instance.SubscribeOnBackgroundThread(this);
        await Task.Run(async () =>
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var requests = GenerateDcsLosCheckRequests();
                    foreach (var request in requests)
                    {
                        await EventBus.Instance.PublishOnBackgroundThreadAsync(new LoSRequestMessage
                        {
                            Request = request
                        }, token);
                    }
                }
                catch (Exception e)
                {
                    Logger.Error(e, "Error building LOS Requests");
                    // Don't rethrow, just keep trying until we're cancelled.
                }

                await Task.Delay(TimeSpan.FromMilliseconds(250), token);

            }
        });
    }

    private List<DCSLosCheckRequest> GenerateDcsLosCheckRequests()
    {
        var clients = _clients.Values.ToList();

        var requests = new List<DCSLosCheckRequest>();

        var playerLocation = ClientStateSingleton.Instance.PlayerCoaltionLocationMetadata;

        if (_serverSettings.GetSettingAsBool(ServerSettingsKeys.LOS_ENABLED)
#if !DEBUG
            && playerLocation != null
            && playerLocation.LngLngPosition != null
            && playerLocation.LngLngPosition.IsValid()
#endif
            )
            foreach (var client in clients)
                //only check if its worth it
                if (client.LatLngPosition != null
                    && client.LatLngPosition.IsValid()
                    && client.ClientGuid != _guid
                   )
                {
                    var latLng = client.LatLngPosition;

                    requests.Add(new DCSLosCheckRequest
                    {
                        ID = client.ClientGuid,
                        Position = latLng,
                    });
                }

        return requests;
    }

    internal void Stop()
    {
        EventBus.Instance.Unsubscribe(this);
    }
}