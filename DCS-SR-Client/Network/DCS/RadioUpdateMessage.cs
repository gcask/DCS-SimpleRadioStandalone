using Ciribob.DCS.SimpleRadio.Standalone.Common.Network.DCS.Models.DCSState;
using System;
using System.Collections.Generic;
using System.Text;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Network.DCS
{
    public class RadioUpdateMessage
    {
        public DCSPlayerRadioInfo RadioInfo { get; init; }
    }
}
