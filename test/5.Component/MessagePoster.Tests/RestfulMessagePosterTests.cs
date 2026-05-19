using Perceptron.Domain.Event;
using System.Reflection;

namespace MessagePoster.Tests;

[TestFixture]
public class RestfulMessagePosterTests
{
    [Test]
    public void Constructor_WithNullPreferences_ShouldUseDefaults()
    {
        var poster = new MessagePoster.RestfulJson.MessagePoster(null);
            
        Assert.That(poster.WillPostMessage, Is.True);
        Assert.That(poster.DestinationUrl, Is.EqualTo("http://127.0.0.1/perceptron-event"));
        Assert.That(poster.CheckDuplicateEvent, Is.False);
        Assert.That(poster.EventSuppressionIntervals, Is.Empty);
        Assert.That(poster.Initialized, Is.True);
    }

    [Test]
    public void Constructor_WithCustomPreferences_ShouldParseCorrectly()
    {
        var prefs = new Dictionary<string, string>
        {
            { "WillPostMessage", "false" },
            { "DestinationUrl", "http://example.com/api" },
            { "CheckDuplicateEvent", "true" },
            { "EventSuppressionIntervals", "EventA:10,EventB:20" }
        };

        var poster = new MessagePoster.RestfulJson.MessagePoster(prefs);

        Assert.That(poster.WillPostMessage, Is.False);
        Assert.That(poster.DestinationUrl, Is.EqualTo("http://example.com/api"));
        Assert.That(poster.CheckDuplicateEvent, Is.True);
        Assert.That(poster.EventSuppressionIntervals, Has.Count.EqualTo(2));
        Assert.That(poster.EventSuppressionIntervals["EventA"], Is.EqualTo(10));
        Assert.That(poster.EventSuppressionIntervals["EventB"], Is.EqualTo(20));
    }

    [Test]
    public void Constructor_WithInvalidJsonIntervals_ShouldUseEmptyDictionary()
    {
        var prefs = new Dictionary<string, string>
        {
            { "EventSuppressionIntervals", "invalid-json" }
        };

        var poster = new MessagePoster.RestfulJson.MessagePoster(prefs);

        Assert.That(poster.EventSuppressionIntervals, Is.Empty);
    }
        
    [Test]
    public void Initialized_ShouldBeTrue_WhenDestinationUrlIsEmpty_DueToDefaultFallback()
    {
        var prefs = new Dictionary<string, string>
        {
            { "DestinationUrl", "" }
        };
        var poster = new MessagePoster.RestfulJson.MessagePoster(prefs);
            
        // PreferenceParser falls back to default if value is empty
        Assert.That(poster.DestinationUrl, Is.EqualTo("http://127.0.0.1/perceptron-event"));
        Assert.That(poster.Initialized, Is.True);
    }

    [Test]
    public void ShouldSuppressEvent_ShouldReturnFalse_WhenNoIntervalConfigured()
    {
        var prefs = new Dictionary<string, string>
        {
            { "CheckDuplicateEvent", "true" },
            { "EventSuppressionIntervals", "{}" }
        };
        var poster = new MessagePoster.RestfulJson.MessagePoster(prefs);
        var evt = new TestDomainEvent("source1", "type", "EventA", "algo");

        var method = typeof(MessagePoster.RestfulJson.MessagePoster).GetMethod("ShouldSuppressEvent", BindingFlags.NonPublic | BindingFlags.Instance);
        var result = (bool)method!.Invoke(poster, new object[] { evt })!;

        Assert.That(result, Is.False);
    }

    [Test]
    public void ShouldSuppressEvent_ShouldReturnFalse_FirstTime()
    {
        var prefs = new Dictionary<string, string>
        {
            { "CheckDuplicateEvent", "true" },
            { "EventSuppressionIntervals", "{\"EventA\": 10}" }
        };
        var poster = new MessagePoster.RestfulJson.MessagePoster(prefs);
        var evt = new TestDomainEvent("source1", "type", "EventA", "algo");

        var method = typeof(MessagePoster.RestfulJson.MessagePoster).GetMethod("ShouldSuppressEvent", BindingFlags.NonPublic | BindingFlags.Instance);
        var result = (bool)method!.Invoke(poster, new object[] { evt })!;

        Assert.That(result, Is.False);
    }

    [Test]
    public void ShouldSuppressEvent_ShouldReturnTrue_WhenCalledTooSoon()
    {
        var prefs = new Dictionary<string, string>
        {
            { "CheckDuplicateEvent", "true" },
            { "EventSuppressionIntervals", "EventA:10" }
        };
        var poster = new MessagePoster.RestfulJson.MessagePoster(prefs);
        var evt = new TestDomainEvent("source1", "type", "EventA", "algo");

        var method = typeof(MessagePoster.RestfulJson.MessagePoster).GetMethod("ShouldSuppressEvent", BindingFlags.NonPublic | BindingFlags.Instance);
            
        // First call - should not suppress
        method!.Invoke(poster, new object[] { evt });

        // Second call immediately - should suppress
        var result = (bool)method.Invoke(poster, new object[] { evt })!;

        Assert.That(result, Is.True);
    }
}

public class TestDomainEvent : DomainEvent
{
    public TestDomainEvent(string sourceId, string eventType, string eventName, string algorithmName) 
        : base(sourceId, eventType, eventName, algorithmName)
    {
    }

    public override string GenerateJsonContent()
    {
        return "{}";
    }

    public override string GenerateLogContent()
    {
        return "log";
    }
}