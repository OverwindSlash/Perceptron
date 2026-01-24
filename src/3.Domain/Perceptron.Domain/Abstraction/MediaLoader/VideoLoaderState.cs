namespace Perceptron.Domain.Abstraction.MediaLoader;

public enum VideoLoaderState
{
    Idle,
    Opened,
    Running,
    Paused,
    Stopped,
    Closed,
    Error
}
