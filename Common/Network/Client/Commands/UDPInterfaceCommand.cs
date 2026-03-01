using Ciribob.DCS.SimpleRadio.Standalone.Common.Network.DCS;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Network.DCS.Models;
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
    RADIO_INFO,
    LOS_REQUEST,
    LOS_RESULT
}

public record SRSCommand
{
    public CommandType Command { get; set; }
    public int RadioId { get; set; }
    public double Frequency { get; set; }
    public bool Enabled { get; set; }
    public float Volume { get; set; }
    public int Code { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Address { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DCSPlayerSideInfo PlayerInfo { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CombinedRadioState RadioInfo { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DCSLosCheckRequest LOSRequest { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DCSLosCheckResult LOSResult { get; set; }
}
