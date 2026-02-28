using Ciribob.DCS.SimpleRadio.Standalone.Common.Player;
using System.Text.Json.Serialization;

namespace Ciribob.DCS.SimpleRadio.Standalone.Common.Network.Client.Commands;

public enum CommandType : int
{
    FREQUENCY_DELTA ,
    ACTIVE_RADIO,
    TOGGLE_GUARD,
    CHANNEL_UP,
    CHANNEL_DOWN,
    SET_VOLUME,
    TRANSPONDER_POWER,
    TRANSPONDER_M1_CODE,
    TRANSPONDER_M3_CODE,
    TRANSPONDER_M4,
    TRANSPONDER_IDENT,
    GUARD, // SET guard
    FREQUENCY_SET,
    TRANSPONDER_M2_CODE,
    CONNECT,
    PLAYER_INFO,
}

public record SRSCommand
{
    public CommandType Command { get; set; }
    public int RadioId { get; set; }
    public double Frequency { get; set; }
    public bool Enabled { get; set; }
    public float Volume { get; set; }
    public int Code { get; set; }
    public string Address { get; set; }
    public DCSPlayerSideInfo PlayerInfo { get; set; }
}
