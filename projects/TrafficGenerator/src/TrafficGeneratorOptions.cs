namespace Pulsas.TrafficGenerator;

public record TrafficGeneratorOptions
{
    public string Endpoint { get; init; } = "ws://localhost:9001/mqtt";
    public string Region { get; init; } = "VNO";
    public int ObserverCount { get; init; } = 3;
    public int Rounds { get; init; } = 60;
    public int IntervalMilliseconds { get; init; } = 1000;
    public int Qos { get; init; } = 1;

    public bool IsValid() =>
        Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri)
        && uri.Scheme is "ws" or "wss"
        && string.IsNullOrEmpty(uri.UserInfo)
        && string.IsNullOrEmpty(uri.Fragment)
        && !string.IsNullOrWhiteSpace(Region)
        && Region == Region.Trim()
        && !Region.Any(c => char.IsControl(c) || c is '/' or '+' or '#')
        && ObserverCount > 0
        && Rounds > 0
        && IntervalMilliseconds >= 0
        && Qos is >= 0 and <= 2;
}
