using Ciribob.DCS.SimpleRadio.Standalone.Common.Network.DCS;
using System;
using System.Collections.Generic;
using System.Text;

namespace Ciribob.DCS.SimpleRadio.Standalone.Common.Models.EventMessages
{
    public class LoSResultMessage
    {
        public DCSLosCheckResult Result { get; init; }
    }
}
