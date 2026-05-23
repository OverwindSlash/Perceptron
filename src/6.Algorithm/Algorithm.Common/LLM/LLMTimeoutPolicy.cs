namespace Algorithm.Common.LLM;

public enum LLMTimeoutPolicy
{
    Drop,
    PublishTraditional,
    PublishUnknown,
    Retry
}
