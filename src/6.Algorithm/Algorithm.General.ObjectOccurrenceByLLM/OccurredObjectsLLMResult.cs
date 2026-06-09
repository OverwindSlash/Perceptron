namespace Algorithm.General.ObjectOccurrenceByLLM;

public record OccurredObjectsLLMResult
{
    public bool isObjOccurred { get; set; }
    public OccurredObject[] occurredObjects { get; set; }
}

public record OccurredObject
{
    public string type { get; set; }
    public float conf { get; set; }
    public int[] bbox_2d { get; set; }
}



