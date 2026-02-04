using ComponentCommon;
using Perceptron.Domain.Abstraction.Annotation;
using Perceptron.Domain.Entity.Annotation;
using Perceptron.Domain.Setting;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AnnotationSender.Udp;

public class AnnotationUdpSender : ComponentBase, IAnnotationSender, IDisposable
{
    public bool EnableAnnotationUdpSender { get; private set; }
    public string AnnotationUdpDestinationHost { get; private set; }
    public int AnnotationUdpDestinationPort { get; private set; }

    private readonly UdpClient _client;
    private readonly IPEndPoint _remote;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase
    };

    public AnnotationUdpSender(Dictionary<string, string>? preferences) 
        : base(preferences)
    {
        LoadPreferences(preferences);

        _client = new UdpClient();
        IPAddress ip;
        if (!IPAddress.TryParse(AnnotationUdpDestinationHost, out ip))
        {
            var addresses = Dns.GetHostAddresses(AnnotationUdpDestinationHost);
            ip = addresses.First(a => a.AddressFamily == AddressFamily.InterNetwork);
        }
        _remote = new IPEndPoint(ip, AnnotationUdpDestinationPort);
    }

    protected override void LoadPreferences(Dictionary<string, string>? preferences)
    {
        EnableAnnotationUdpSender = AnnotationSenderSettings.ParseEnableAnnotationUdpSender(preferences);
        AnnotationUdpDestinationHost = AnnotationSenderSettings.ParseAnnotationUdpDestinationHost(preferences);
        AnnotationUdpDestinationPort = AnnotationSenderSettings.ParseAnnotationUdpDestinationPort(preferences);
    }

    public async Task<int> SendAsync(VisualAnnotation annotation)
    {
        if (!EnableAnnotationUdpSender)
        {
            return 0;
        }
        
        var json = JsonSerializer.Serialize(annotation, _jsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        return await _client.SendAsync(bytes, bytes.Length, _remote);
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}