using Algorithm.Ship.Labels;
using OpenCvSharp;

namespace ShipLables.Tests;

[TestFixture]
public class ShipLabelsTests
{
    [Test]
    public void LoadLocalImageFileAndInfernce()
    {
        var imRead = Cv2.ImRead("Images/Container_MILDPEONY_01.jpg");

        var predictor = new ShipLabelPredictor(
            modelPath: "Models/ship_labels_codex_enhanced.onnx",
            execProvider: "cuda",
            deviceId: 0);

        var result = predictor.Run(imRead);

        Assert.That(result, Is.EqualTo("{\"ShipTypeGroup\":\"Cargo\",\"ShipColor\":[\"White\",\"Blue\",\"Red\"],\"ShipDraught\":\"Deep\",\"ShipViewAngle\":\"ObliqueFront\",\"Confidence\":0}"));
    }
}