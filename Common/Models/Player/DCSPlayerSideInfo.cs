using Ciribob.DCS.SimpleRadio.Standalone.Common.Models.Player;
using System;

namespace Ciribob.DCS.SimpleRadio.Standalone.Common.Player;

public class DCSPlayerSideInfo
{
    public string Name { get; set; } = "";
    public int Seat; // 0 is front / normal - 1 is back seat
    public int Side;

    public LatLngPosition LngLngPosition { get; set; } = new();

    public override bool Equals(object obj)
    {
        if (obj == null) return false;

        return obj is DCSPlayerSideInfo info &&
               Name == info.Name &&
               Side == info.Side &&
               Seat == info.Seat;
    }

    public override int GetHashCode() => HashCode.Combine(Name, Side, Seat);

    public void Reset()
    {
        Name = "";
        Side = 0;
        Seat = 0;
        LngLngPosition = new();
    }
}