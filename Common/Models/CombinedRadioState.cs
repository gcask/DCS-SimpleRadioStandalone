using Ciribob.DCS.SimpleRadio.Standalone.Common.Models;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Network.DCS.Models.DCSState;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Network.Models;

namespace Ciribob.DCS.SimpleRadio.Standalone.Common.Network.DCS.Models;

public struct CombinedRadioState
{
    public DCSPlayerRadioInfo RadioInfo;

    public RadioSendingState RadioSendingState;

    public RadioReceivingState[] RadioReceivingState;

    public int ClientCountConnected;

    public int[] TunedClients;
}