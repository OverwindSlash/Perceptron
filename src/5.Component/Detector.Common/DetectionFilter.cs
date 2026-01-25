using Perceptron.Domain.Entity.ObjectDetection;

namespace Detector.Common;

public class DetectionFilter
{
    public static List<DetectedObject> FilterObjectTypes(List<DetectedObject> detectedObjects, List<string> targetTypes)
    {
        return detectedObjects.Where(x => targetTypes.Contains(x.Label.ToLower())).ToList();
    }

    public static List<DetectedObject> FilterLargeObjects(List<DetectedObject> detectedObjects, 
        int maxBboxWidth, int maxBboxHeight)
    {
        return detectedObjects.Where(x => x.Bbox.Width <= maxBboxWidth && x.Bbox.Height <= maxBboxHeight).ToList();
    }

    public static List<DetectedObject> FilterSmallObjects(List<DetectedObject> detectedObjects, 
        int minBboxWidth, int minBboxHeight)
    {
        return detectedObjects.Where(x => x.Bbox.Width >= minBboxWidth && x.Bbox.Height >= minBboxHeight).ToList();
    }

    /// <summary>
    /// 过滤掉被同类型对象包含的DetectedObject
    /// </summary>
    /// <param name="detectedObjects">待过滤的对象列表</param>
    /// <returns>过滤后的对象列表</returns>
    public static List<DetectedObject> FilterInnerSameObjects(List<DetectedObject> detectedObjects, float innerObjectOverlapRatio)
    {
        var filteredObjects = new List<DetectedObject>();

        // 标记需要移除的对象索引
        var toRemove = new HashSet<int>();

        for (int i = 0; i < detectedObjects.Count; i++)
        {
            if (toRemove.Contains(i)) continue;

            var currentObject = detectedObjects[i];

            // 检查当前对象与其他同类型对象的重叠关系
            for (int j = i + 1; j < detectedObjects.Count; j++)
            {
                if (toRemove.Contains(j)) continue;

                var otherObject = detectedObjects[j];

                // 只检查同类型的对象
                if (currentObject.LabelId == otherObject.LabelId)
                {
                    // 使用重叠百分比判断，当重叠大于设定阈值时进行过滤
                    float overlapPercentage = currentObject.Bbox.OverlapPercentage(otherObject.Bbox);
                    if (overlapPercentage > innerObjectOverlapRatio)
                    {
                        // 保留面积更大的边界框，移除面积较小的
                        if (currentObject.Bbox.Area >= otherObject.Bbox.Area)
                        {
                            toRemove.Add(j);
                        }
                        else
                        {
                            toRemove.Add(i);
                            break; // 当前对象被标记移除，跳出内层循环
                        }
                    }
                }
            }
        }

        // 添加未被标记移除的对象
        for (int i = 0; i < detectedObjects.Count; i++)
        {
            if (!toRemove.Contains(i))
            {
                filteredObjects.Add(detectedObjects[i]);
            }
        }

        return filteredObjects;
    }

    public static List<DetectedObject> MapObjectType(List<DetectedObject> detectedObjects, 
        List<string> sourceTypeNames, int mappedId, string destinationTypeName)
    {
        foreach (var detectedObject in detectedObjects)
        {
            if (sourceTypeNames.Contains(detectedObject.Label))
            {
                if (mappedId >= 0)
                {
                    detectedObject.LabelId = mappedId;
                }
                detectedObject.Label = destinationTypeName;
            }
        }

        return detectedObjects;
    }
}