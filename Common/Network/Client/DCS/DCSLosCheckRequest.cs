using Ciribob.DCS.SimpleRadio.Standalone.Common.Models.Player;

namespace Ciribob.DCS.SimpleRadio.Standalone.Common.Network.DCS;

public record DCSLosCheckRequest
{
    public string ID {get; init;}
    public LatLngPosition Position { get; init; }
}