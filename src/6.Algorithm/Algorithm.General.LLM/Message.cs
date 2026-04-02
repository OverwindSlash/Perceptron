namespace Algorithm.General.LLM;

public class Message
{
    public string role { get; set; }
    public Content[] content { get; set; }
}

public class Content
{
    public string type { get; set; }
    public Image_Url image_url { get; set; }
    public string text { get; set; }
}

public class Image_Url
{
    public string url { get; set; }
}

