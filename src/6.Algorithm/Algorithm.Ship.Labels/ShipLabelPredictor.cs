using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using System.Text.Json;

namespace Algorithm.Ship.Labels;

public class ShipLabelPredictor : IDisposable
{
    // ImageNet Normalization
    private static readonly float[] Mean = { 0.485f, 0.456f, 0.406f };
    private static readonly float[] Std = { 0.229f, 0.224f, 0.225f };
    private const int DefaultImageSize = 384;
    private const float ColorThreshold = 0.5f;

    private readonly string _modelPath;
    private readonly string _execProvider;
    private readonly int _deviceId;

    private InferenceSession _session;
    private readonly ModelInputInfo _inputInfo;

    public ShipLabelPredictor(string modelPath, string execProvider, int deviceId)
    {
        _modelPath = modelPath;
        _execProvider = execProvider;
        _deviceId = deviceId;
        // Initialize the ONNX model here using the provided parameters
        // For example, you can use YoloDotNet or any other ONNX runtime to load the model

        SessionOptions option = new SessionOptions();
        option.LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR;
        option.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
        switch (_execProvider.ToLower())
        {
            case "cpu":
                option.AppendExecutionProvider_CPU();
                break;
            case "cuda":
                option.AppendExecutionProvider_CUDA(_deviceId);
                break;
            default:
                option.AppendExecutionProvider_CPU();
                break;
        }

        _session = new InferenceSession(_modelPath, option);
        _inputInfo = GetInputInfo(_session);
    }

    public string Run(Mat image)
    {
        var inputTensor = PreprocessImage(
            image,
            _inputInfo.Width,
            _inputInfo.Height);

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(_inputInfo.Name, inputTensor)
        };

        using var results = _session.Run(inputs);

        var typeLogits = results.First(x => x.Name == "ship_type").AsTensor<float>();
        var detailLogits = results.First(x => x.Name == "ship_type_detail").AsTensor<float>();
        var colorLogits = results.First(x => x.Name == "ship_color").AsTensor<float>();

        var shipLabel = new ShipLabel();

        // 1. Ship Type Group (Argmax)
        int typeIdx = GetArgMax(typeLogits);
        shipLabel.ShipTypeGroup = ShipLabelConfigs.ShipTypes[typeIdx];

        // 2. Ship Type Detail (Softmax + compatibility constraint)
        int detailIdx = GetCompatibleDetailArgMax(
            detailLogits,
            shipLabel.ShipTypeGroup);
        shipLabel.ShipTypeDetail = ShipLabelConfigs.ShipTypeDetails[detailIdx];

        // 3. Ship Color (Sigmoid + Threshold)
        shipLabel.ShipColor = GetMultiLabel(colorLogits);

        var json = JsonSerializer.Serialize<ShipLabel>(shipLabel);

        return json;
    }

    private static ModelInputInfo GetInputInfo(InferenceSession session)
    {
        var input = session.InputMetadata.First();
        var shape = input.Value.Dimensions.ToArray();

        if (shape.Length != 4)
        {
            throw new InvalidOperationException(
                $"Expected model input to have 4 dimensions (NCHW), but got {shape.Length}.");
        }

        if (shape[1] > 0 && shape[1] != 3)
        {
            throw new InvalidOperationException(
                $"Expected model input to have 3 channels, but got {shape[1]}.");
        }

        int height = shape[2] > 0 ? shape[2] : DefaultImageSize;
        int width = shape[3] > 0 ? shape[3] : DefaultImageSize;

        return new ModelInputInfo(input.Key, shape, width, height);
    }

    private static DenseTensor<float> PreprocessImage(Mat image, int width, int height)
    {
        using var resized = new Mat();
        Cv2.Resize(image, resized, new Size(width, height), 0, 0, InterpolationFlags.Area);

        var denseTensor = new DenseTensor<float>(new[] { 1, 3, height, width });

        for (int y = 0; y < resized.Rows; y++)
        {
            for (int x = 0; x < resized.Cols; x++)
            {
                var bgr = resized.At<Vec3b>(y, x);
                var r = bgr.Item2 / 255f;
                var g = bgr.Item1 / 255f;
                var b = bgr.Item0 / 255f;

                denseTensor[0, 0, y, x] = (r - Mean[0]) / Std[0];
                denseTensor[0, 1, y, x] = (g - Mean[1]) / Std[1];
                denseTensor[0, 2, y, x] = (b - Mean[2]) / Std[2];
            }
        }

        return denseTensor;
    }

    private sealed record ModelInputInfo(string Name, int[] Shape, int Width, int Height);

    private static int GetArgMax(Tensor<float> logits)
    {
        int maxIdx = 0;
        float maxVal = logits[0, 0];

        // Assume batch size 1
        for (int i = 1; i < logits.Dimensions[1]; i++)
        {
            if (logits[0, i] > maxVal)
            {
                maxVal = logits[0, i];
                maxIdx = i;
            }
        }
        return maxIdx;
    }

    private static List<string> GetMultiLabel(Tensor<float> logits)
    {
        var colors = new List<string>();
        float maxScore = float.MinValue;
        int maxIdx = 0;

        // Sigmoid and Threshold
        for (int i = 0; i < logits.Dimensions[1]; i++)
        {
            float score = 1.0f / (1.0f + MathF.Exp(-logits[0, i]));

            if (score > maxScore)
            {
                maxScore = score;
                maxIdx = i;
            }

            if (score > ColorThreshold)
            {
                colors.Add(ShipLabelConfigs.ShipColors[i]);
            }
        }

        // Fallback: if no color exceeds threshold, return top 1
        if (colors.Count == 0)
        {
            colors.Add(ShipLabelConfigs.ShipColors[maxIdx]);
        }

        return colors;
    }

    private static int GetCompatibleDetailArgMax(
        Tensor<float> logits,
        string shipType)
    {
        if (!ShipLabelConfigs.ShipTypeDetailCompatibility.TryGetValue(
                shipType,
                out var allowedDetails) ||
            allowedDetails.Length == 0)
        {
            return GetArgMax(logits);
        }

        float maxScore = float.MinValue;
        int maxIdx = 0;

        foreach (var detail in allowedDetails)
        {
            int idx = Array.IndexOf(ShipLabelConfigs.ShipTypeDetails, detail);
            if (idx < 0)
            {
                continue;
            }

            float score = SoftmaxScore(logits, idx);
            if (score > maxScore)
            {
                maxScore = score;
                maxIdx = idx;
            }
        }

        return maxIdx;
    }

    private static float SoftmaxScore(Tensor<float> logits, int targetIdx)
    {
        float maxLogit = float.MinValue;
        for (int i = 0; i < logits.Dimensions[1]; i++)
        {
            if (logits[0, i] > maxLogit)
            {
                maxLogit = logits[0, i];
            }
        }

        float denominator = 0.0f;
        for (int i = 0; i < logits.Dimensions[1]; i++)
        {
            denominator += MathF.Exp(logits[0, i] - maxLogit);
        }

        return MathF.Exp(logits[0, targetIdx] - maxLogit) / denominator;
    }

    public void Dispose()
    {
        _session.Dispose();
    }
}
