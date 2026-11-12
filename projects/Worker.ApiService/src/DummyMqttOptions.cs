namespace Pulsas.Worker.ApiService;

public record DummyMqttOptions
{
    public string Endpoint { get; init; } = "ws://localhost:9001/mqtt";
    public string Region { get; init; } = "VNO";

    public bool IsValid() =>
        Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri)
        && uri.Scheme is "ws" or "wss"
        && string.IsNullOrEmpty(uri.UserInfo)
        && string.IsNullOrEmpty(uri.Fragment)
        && Region is { Length: 3 }
        && Region.All(c => c is >= 'A' and <= 'Z');
}
