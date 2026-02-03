using ComponentCommon;
using Perceptron.Domain.Abstraction.MessagePoster;
using Perceptron.Domain.Event;
using Perceptron.Domain.Setting;
using System.Collections.Concurrent;
using System.Text;

namespace MessagePoster.RestfulJson;

public class MessagePoster : ComponentBase, IMessagePoster
{
    public bool WillPostMessage { get; private set; }
    public string DestinationUrl { get; private set; }
    public bool CheckDuplicateEvent { get; private set; }
    public Dictionary<string, int> EventSuppressionIntervals { get; private set; }

    private readonly ConcurrentDictionary<string, DateTime> _lastEventPostTimes = new();

    public bool Initialized => !string.IsNullOrEmpty(DestinationUrl);

    public MessagePoster(Dictionary<string, string> preferences = null)
        : base(preferences)
    {
        LoadPreferences(preferences);
    }

    protected override void LoadPreferences(Dictionary<string, string>? preferences)
    {
        WillPostMessage = MessagePosterSettings.ParseWillPostMessage(preferences);
        DestinationUrl = MessagePosterSettings.ParseDestinationUrl(preferences);
        CheckDuplicateEvent = MessagePosterSettings.ParseCheckDuplicateEvent(preferences);
        EventSuppressionIntervals = MessagePosterSettings.ParseEventSuppressionIntervals(preferences);
        
        // Clear cache when preferences reload as settings might have changed logic
        _lastEventPostTimes.Clear();
    }

    public void PostDomainEventMessage(DomainEvent @event)
    {
        if (!WillPostMessage) return;

        if (CheckDuplicateEvent)
        {
            if (ShouldSuppressEvent(@event))
            {
                return;
            }
        }

        var jsonMsg = @event.GenerateJsonContent();

        var content = new StringContent(jsonMsg, Encoding.UTF8, "application/json");

        Task.Run(async () =>
        {
            using var client = new HttpClient();
            try 
            {
                HttpResponseMessage response = await client.PostAsync(DestinationUrl, content);

                if (response.IsSuccessStatusCode)
                {
                    //Console.WriteLine("Send message successful！");
                    string result = await response.Content.ReadAsStringAsync();
                    //Console.WriteLine("response：" + result);
                }
                else
                {
                    //Console.WriteLine("Send message failed！" + response.StatusCode);
                }
            }
            catch
            {
                // Ignore network errors
            }
        });
    }

    private bool ShouldSuppressEvent(DomainEvent @event)
    {
        if (EventSuppressionIntervals == null || !EventSuppressionIntervals.TryGetValue(@event.EventName, out var intervalMs))
        {
            // If no interval configured for this event type, do not suppress
            return false;
        }

        var key = $"{@event.SourceId}_{@event.EventName}";
        var now = DateTime.Now;

        if (_lastEventPostTimes.TryGetValue(key, out var lastTime))
        {
            if ((now - lastTime).TotalMilliseconds < intervalMs)
            {
                return true;
            }
        }

        _lastEventPostTimes[key] = now;
        return false;
    }
}