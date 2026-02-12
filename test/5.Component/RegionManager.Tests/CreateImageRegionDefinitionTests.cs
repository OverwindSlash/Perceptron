using System.Diagnostics;
using Perceptron.Domain.Entity.RegionDefinition;
using Perceptron.Domain.Entity.RegionDefinition.Geometric;

namespace RegionManager.Tests;

public class CreateImageRegionDefinitionTests
{
    private const int TestImageWidth = 3632;
    private const int TestImageHeight = 1632;

    [Explicit]
    [Test]
    public void CreateImageRegionDefinition_FromOriginalCoordinates_ShouldNormalizeCorrectly()
    {
        // Arrange
        var definition = new ImageRegionDefinition
        {
            Name = "phq-analysis-zone",
            IsObjectAnalyzableRetain = false,
            IsDoubleLineCounting = false
        };

        // 使用指定图像长宽，并通过 OriginalX/OriginalY 设置点
        {
            var analysisArea = new AnalysisArea { Name = "AnalysisArea1" };
            analysisArea.Points = new List<NormalizedPoint>
            {
                new NormalizedPoint(TestImageWidth, TestImageHeight, 950, 611),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1427, 611),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1427, 948),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 950, 948)
            };

            analysisArea.SetImageSize(TestImageWidth, TestImageHeight);
            definition.AddAnalysisArea(analysisArea);
        }

        {
            var analysisArea = new AnalysisArea { Name = "AnalysisArea2" };
            analysisArea.Points = new List<NormalizedPoint>
            {
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1427, 609),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1925, 609),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1925, 999),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1427, 999)
            };

            analysisArea.SetImageSize(TestImageWidth, TestImageHeight);
            definition.AddAnalysisArea(analysisArea);
        }

        {
            var analysisArea = new AnalysisArea { Name = "AnalysisArea3" };
            analysisArea.Points = new List<NormalizedPoint>
            {
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1925, 629),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2453, 629),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2453, 999),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1925, 999)
            };

            analysisArea.SetImageSize(TestImageWidth, TestImageHeight);
            definition.AddAnalysisArea(analysisArea);
        }

        {
            var exclusionArea = new ExcludedArea() { Name = "ExclusionArea1" };
            exclusionArea.Points = new List<NormalizedPoint>
            {
                new NormalizedPoint(TestImageWidth, TestImageHeight, 897, 741),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1095, 741),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1095, 829),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 897, 829)
            };

            exclusionArea.SetImageSize(TestImageWidth, TestImageHeight);
            definition.AddExcludedArea(exclusionArea);
        }

        {
            var exclusionArea = new ExcludedArea() { Name = "ExclusionArea2" };
            exclusionArea.Points = new List<NormalizedPoint>
            {
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1199, 867),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1425, 867),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1425, 957),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1199, 957)
            };

            exclusionArea.SetImageSize(TestImageWidth, TestImageHeight);
            definition.AddExcludedArea(exclusionArea);
        }

        {
            var exclusionArea = new ExcludedArea() { Name = "ExclusionArea3" };
            exclusionArea.Points = new List<NormalizedPoint>
            {
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2411, 760),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2474, 760),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2474, 895),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2411, 895)
            };

            exclusionArea.SetImageSize(TestImageWidth, TestImageHeight);
            definition.AddExcludedArea(exclusionArea);
        }

        {
            var exclusionArea = new ExcludedArea() { Name = "ExclusionArea4" };
            exclusionArea.Points = new List<NormalizedPoint>
            {
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1490, 614),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1736, 614),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1736, 652),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1490, 652)
            };

            exclusionArea.SetImageSize(TestImageWidth, TestImageHeight);
            definition.AddExcludedArea(exclusionArea);
        }

        {
            var exclusionArea = new ExcludedArea() { Name = "ExclusionArea5" };
            exclusionArea.Points = new List<NormalizedPoint>
            {
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1088, 614),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1198, 614),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1198, 650),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1088, 650)
            };

            exclusionArea.SetImageSize(TestImageWidth, TestImageHeight);
            definition.AddExcludedArea(exclusionArea);
        }

        var interestArea = new InterestArea() { Name = "CountArea" };
        interestArea.Points = new List<NormalizedPoint>
        {
            new NormalizedPoint(TestImageWidth, TestImageHeight, 950, 608),
            new NormalizedPoint(TestImageWidth, TestImageHeight, 2455, 608),
            new NormalizedPoint(TestImageWidth, TestImageHeight, 2455, 999),
            new NormalizedPoint(TestImageWidth, TestImageHeight, 950, 999)

        };
        interestArea.SetImageSize(TestImageWidth, TestImageHeight);
        definition.AddInterestArea(interestArea);

        var tempFile = $"created-region-definition-original-{Guid.NewGuid()}.json";

        // open folder to check the created file
        Process.Start("explorer.exe", $"/select,\"{tempFile}\"");

        try
        {
            // Act
            ImageRegionDefinition.SaveToJson(tempFile, definition);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                //File.Delete(tempFile);
            }
        }
    }
}