namespace Ciribob.DCS.SimpleRadio.Standalone.Common.Network.DCS;

public record DCSLosCheckResult
{
    public string ID { get; set; }
    public float LoS { get; set; }

    public override string ToString()
    {
        return $"[id {ID} LOS {LoS}]";
    }
}