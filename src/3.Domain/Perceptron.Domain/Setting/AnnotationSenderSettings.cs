using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using Serilog;

namespace Perceptron.Domain.Setting;

public class AnnotationSenderSettings : ComponentSettings
{
    public const bool DefaultEnableAnnotationUdpSender = true;
    public const string DefaultAnnotationUdpDestinationHost = "127.0.0.1";
    public const int DefaultAnnotationUdpDestinationPort = 9999;

    public bool EnableAnnotationUdpSender { get; private set; } = DefaultEnableAnnotationUdpSender;
    public string AnnotationUdpDestinationHost { get; private set; } = DefaultAnnotationUdpDestinationHost;
    public int AnnotationUdpDestinationPort { get; private set; } = DefaultAnnotationUdpDestinationPort;

    public override void ParsePreferences()
    {
        EnableAnnotationUdpSender = ParseEnableAnnotationUdpSender(Preferences);
        AnnotationUdpDestinationHost = ParseAnnotationUdpDestinationHost(Preferences);
        AnnotationUdpDestinationPort = ParseAnnotationUdpDestinationPort(Preferences);
    }

    public static bool ParseEnableAnnotationUdpSender(Dictionary<string, string> preferences)
    {
        var value = PreferenceParser.ParseBoolValue(preferences, "EnableAnnotationUdpSender",
            DefaultEnableAnnotationUdpSender);

        return value;
    }

    public static string ParseAnnotationUdpDestinationHost(Dictionary<string, string> preferences)
    {
        var value = PreferenceParser.ParseStringValue(preferences, "AnnotationUdpDestinationHost",
            DefaultAnnotationUdpDestinationHost);

        // 判断是合法 IP 就返回, 否则返回默认值
        if (IPAddress.TryParse(value, out _))
        {
            return value;
        }

        Log.Warning($"AnnotationUdpDestinationHost is not a valid IP address. Reset to default: {DefaultAnnotationUdpDestinationHost}");        
        return DefaultAnnotationUdpDestinationHost;
    }

    public static int ParseAnnotationUdpDestinationPort(Dictionary<string, string> preferences)
    {
        var value = PreferenceParser.ParseIntValue(preferences, "AnnotationUdpDestinationPort",
            DefaultAnnotationUdpDestinationPort);

        // 判断是否在有效端口范围内
        if (value < IPEndPoint.MinPort || value > IPEndPoint.MaxPort)
        {
            Log.Warning($"AnnotationUdpDestinationPort is not in valid range. Reset to default: {DefaultAnnotationUdpDestinationPort}");
            return DefaultAnnotationUdpDestinationPort;
        }

        return value;
    }
}