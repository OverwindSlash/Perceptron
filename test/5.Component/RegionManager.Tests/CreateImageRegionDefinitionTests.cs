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
    public void CreateImageRegionDefinition_PHQ()
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
                new NormalizedPoint(TestImageWidth, TestImageHeight, 768, 639),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 986, 639),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 986, 772),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 768, 772)
            };

            analysisArea.SetImageSize(TestImageWidth, TestImageHeight);
            definition.AddAnalysisArea(analysisArea);
        }

        {
            var analysisArea = new AnalysisArea { Name = "AnalysisArea2" };
            analysisArea.Points = new List<NormalizedPoint>
            {
                new NormalizedPoint(TestImageWidth, TestImageHeight, 986, 621),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1239, 621),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1239, 742),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 986, 742)
            };

            analysisArea.SetImageSize(TestImageWidth, TestImageHeight);
            definition.AddAnalysisArea(analysisArea);
        }

        {
            var analysisArea = new AnalysisArea { Name = "AnalysisArea3" };
            analysisArea.Points = new List<NormalizedPoint>
            {
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1239, 607),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1482, 607),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1482, 707),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1239, 707)
            };

            analysisArea.SetImageSize(TestImageWidth, TestImageHeight);
            definition.AddAnalysisArea(analysisArea);
        }

        {
            var analysisArea = new AnalysisArea { Name = "AnalysisArea4" };
            analysisArea.Points = new List<NormalizedPoint>
            {
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2445, 669),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2601, 669),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2601, 805),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2445, 805)
            };

            analysisArea.SetImageSize(TestImageWidth, TestImageHeight);
            definition.AddAnalysisArea(analysisArea);
        }

        {
            var analysisArea = new AnalysisArea { Name = "AnalysisArea5" };
            analysisArea.Points = new List<NormalizedPoint>
            {
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2602, 688),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2759, 688),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2759, 839),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2602, 839)
            };

            analysisArea.SetImageSize(TestImageWidth, TestImageHeight);
            definition.AddAnalysisArea(analysisArea);
        }

        {
            var exclusionArea = new ExcludedArea() { Name = "ExclusionArea1" };
            exclusionArea.Points = new List<NormalizedPoint>
            {
                new NormalizedPoint(TestImageWidth, TestImageHeight, 840, 754),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 876, 745),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 901, 750),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 925, 746),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 939, 754),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 967, 749),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 988, 757),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1020, 751),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1035, 759),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1072, 754),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1085, 766),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1094, 784),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1093, 801),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1075, 823),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1061, 828),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 976, 832)
            };

            exclusionArea.SetImageSize(TestImageWidth, TestImageHeight);
            definition.AddExcludedArea(exclusionArea);
        }

        {
            var exclusionArea = new ExcludedArea() { Name = "ExclusionArea2" };
            exclusionArea.Points = new List<NormalizedPoint>
            {
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1134, 634),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1112, 636),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1093, 643),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1087, 652),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1096, 656),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1135, 656)
            };

            exclusionArea.SetImageSize(TestImageWidth, TestImageHeight);
            definition.AddExcludedArea(exclusionArea);
        }

        {
            var exclusionArea = new ExcludedArea() { Name = "ExclusionArea3" };
            exclusionArea.Points = new List<NormalizedPoint>
            {
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1202, 909),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1201, 885),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1229, 871),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1276, 871),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1376, 886),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1432, 899),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1433, 935),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1488, 939),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1561, 943),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1561, 980),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1201, 978)
            };

            exclusionArea.SetImageSize(TestImageWidth, TestImageHeight);
            definition.AddExcludedArea(exclusionArea);
        }

        {
            var exclusionArea = new ExcludedArea() { Name = "ExclusionArea4" };
            exclusionArea.Points = new List<NormalizedPoint>
            {
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2419, 840),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2420, 769),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2458, 767),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2550, 802),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2585, 807),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2628, 813),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2699, 836),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2734, 867),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2736, 898),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2422, 901)
            };

            exclusionArea.SetImageSize(TestImageWidth, TestImageHeight);
            definition.AddExcludedArea(exclusionArea);
        }

        {
            var exclusionArea = new ExcludedArea() { Name = "ExclusionArea5" };
            exclusionArea.Points = new List<NormalizedPoint>
            {
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2421, 672),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2482, 671),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2591, 675),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2927, 817),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2929, 662),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2727, 641),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2559, 639),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2418, 643)
            };

            exclusionArea.SetImageSize(TestImageWidth, TestImageHeight);
            definition.AddExcludedArea(exclusionArea);
        }

        {
            var exclusionArea = new ExcludedArea() { Name = "ExclusionArea6" };
            exclusionArea.Points = new List<NormalizedPoint>
            {
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1553, 620),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1539, 634),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1498, 639),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1482, 651),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1492, 664),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1526, 660),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1554, 659)
            };

            exclusionArea.SetImageSize(TestImageWidth, TestImageHeight);
            definition.AddExcludedArea(exclusionArea);
        }

        {
            var exclusionArea = new ExcludedArea() { Name = "ExclusionArea7" };
            exclusionArea.Points = new List<NormalizedPoint>
            {
                new NormalizedPoint(TestImageWidth, TestImageHeight, 805, 748),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1547, 651),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2415, 670),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2736, 868),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2296, 1000),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1002, 961)
            };

            exclusionArea.SetImageSize(TestImageWidth, TestImageHeight);
            definition.AddExcludedArea(exclusionArea);
        }

        {
            var exclusionArea = new ExcludedArea() { Name = "ExclusionArea8" };
            exclusionArea.Points = new List<NormalizedPoint>
            {
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1094, 652),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1561, 611),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1559, 573),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1109, 614),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 769, 633),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 769, 744)
            };

            exclusionArea.SetImageSize(TestImageWidth, TestImageHeight);
            definition.AddExcludedArea(exclusionArea);
        }

        {
            var interestArea = new InterestArea() { Name = "CountArea" };
            interestArea.Points = new List<NormalizedPoint>
            {
                new NormalizedPoint(TestImageWidth, TestImageHeight, 821, 595),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2896, 595),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2896, 990),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 821, 990)
            };
            interestArea.SetImageSize(TestImageWidth, TestImageHeight);
            definition.AddInterestArea(interestArea);
        }

        {
            var lanArea = new Lane() { Name = "ia1" };
            lanArea.Points = new List<NormalizedPoint>
            {
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1092, 651),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 767, 743),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 800, 748),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1495, 659),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1557, 631),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1557, 612)
            };
            lanArea.SetImageSize(TestImageWidth, TestImageHeight);
            definition.AddLane(lanArea);
        }

        {
            var lanArea = new Lane() { Name = "ia2" };
            lanArea.Points = new List<NormalizedPoint>
            {
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2414, 672),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2737, 868),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2884, 801),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2582, 670)
            };
            lanArea.SetImageSize(TestImageWidth, TestImageHeight);
            definition.AddLane(lanArea);
        }


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

    [Explicit]
    [Test]
    public void CreateImageRegionDefinition_POC()
    {
        // Arrange
        var definition = new ImageRegionDefinition
        {
            Name = "poc-analysis-zone",
            IsObjectAnalyzableRetain = false,
            IsDoubleLineCounting = false
        };

        // 使用指定图像长宽，并通过 OriginalX/OriginalY 设置点
        {
            var analysisArea = new AnalysisArea { Name = "AnalysisArea1" };
            analysisArea.Points = new List<NormalizedPoint>
            {
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1842, 556),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2221, 556),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2221, 714),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1842, 714)
            };

            analysisArea.SetImageSize(TestImageWidth, TestImageHeight);
            definition.AddAnalysisArea(analysisArea);
        }

        {
            var analysisArea = new AnalysisArea { Name = "AnalysisArea2" };
            analysisArea.Points = new List<NormalizedPoint>
            {
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2518, 579),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2834, 579),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2834, 739),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2518, 739)
            };

            analysisArea.SetImageSize(TestImageWidth, TestImageHeight);
            definition.AddAnalysisArea(analysisArea);
        }

        {
            var exclusionArea = new ExcludedArea() { Name = "ExclusionArea1" };
            exclusionArea.Points = new List<NormalizedPoint>
            {
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1865, 595),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1921, 616),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1983, 628),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2069, 636),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2135, 604),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2190, 611),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2249, 595),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2351, 579),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2423, 600),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2483, 644),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2563, 679),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2616, 653),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2690, 667),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2756, 686),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2811, 697),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2844, 693),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2844, 793),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1818, 800)
            };
        
            exclusionArea.SetImageSize(TestImageWidth, TestImageHeight);
            definition.AddExcludedArea(exclusionArea);
        }

        {
            var interestArea = new InterestArea() { Name = "CountArea" };
            interestArea.Points = new List<NormalizedPoint>
            {
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1828, 486),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2851, 486),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2851, 756),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1828, 756)

            };
            interestArea.SetImageSize(TestImageWidth, TestImageHeight);
            definition.AddInterestArea(interestArea);
        }

        {
            var lanArea = new Lane() { Name = "ia1" };
            lanArea.Points = new List<NormalizedPoint>
            {
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1832, 671),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 1855, 567),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2457, 585),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2832, 653),
                new NormalizedPoint(TestImageWidth, TestImageHeight, 2829, 725)
            };
            lanArea.SetImageSize(TestImageWidth, TestImageHeight);
            definition.AddLane(lanArea);
        }



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