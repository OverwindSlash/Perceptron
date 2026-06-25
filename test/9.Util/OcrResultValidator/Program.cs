// See https://aka.ms/new-console-template for more information

using OpenCvSharp;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models.Local;

var _paddleOcrAll = new PaddleOcrAll(LocalFullModels.ChineseV5)
{
    AllowRotateDetection = true,
    Enable180Classification = false,
};

int LevenshteinDistance(string s, string t)
{
    if (string.IsNullOrEmpty(s))
    {
        return string.IsNullOrEmpty(t) ? 0 : t.Length;
    }

    if (string.IsNullOrEmpty(t))
    {
        return s.Length;
    }

    int n = s.Length;
    int m = t.Length;
    int[,] d = new int[n + 1, m + 1];

    // 初始化第一行和第一列
    for (int i = 0; i <= n; i++) d[i, 0] = i;
    for (int j = 0; j <= m; j++) d[0, j] = j;

    // 填充其余部分
    for (int i = 1; i <= n; i++)
    {
        for (int j = 1; j <= m; j++)
        {
            int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;

            d[i, j] = Math.Min(
                Math.Min(d[i - 1, j] + 1,    // 删除
                    d[i, j - 1] + 1),   // 插入
                d[i - 1, j - 1] + cost); // 替换
        }
    }

    return d[n, m];
}

double GetSimilarity(string s, string t)
{
    if (string.IsNullOrEmpty(s) && string.IsNullOrEmpty(t))
        return 1.0;

    int distance = LevenshteinDistance(s.ToLower(), t.ToLower());
    int maxLen = Math.Max(s.Length, t.Length);

    if (maxLen == 0)
        return 1.0;

    return 1.0 - ((double)distance / maxLen);
}

void ShowImage(string title, Mat image)
{
    //Cv2.ImShow(title, image);
    //Cv2.WaitKey(0);
    //Cv2.DestroyAllWindows();
}

double ShowOcrResult(string title, Mat image, string gtResult)
{
    Console.WriteLine($"{title} result:");
    PaddleOcrResult result = _paddleOcrAll.Run(image);

    double highestScore = 0;

    foreach (PaddleOcrResultRegion region in result.Regions)
    {
        var similarity = GetSimilarity(region.Text, gtResult);
        Console.WriteLine($"Text: {region.Text}, Score: {region.Score}, GT: {gtResult}, Similarity: {similarity}");
        if (similarity > highestScore)
        {
            highestScore = similarity;
        }
    }
    Console.WriteLine($"Highest similarity: {highestScore}");
    Console.WriteLine("----------");

    return highestScore;
}


string path = @"C:\workspace\train\ship\3.OCR\plate_val2";

string[] jpgFiles = null;
if (File.Exists(path))
{
    jpgFiles = new string[] { path };
}
else
{
    jpgFiles = Directory.GetFiles(path, "*.jpg");
}

double sumGray = 0;
double sumDenoised = 0;
double sumEqualized = 0;
double sumGraySharpened = 0;
double sumDenoisedSharpened = 0;
double sumEqualizedSharpened = 0;

foreach (string jpgFile in jpgFiles)
{
    FileInfo fi = new FileInfo(jpgFile);
    string filename = fi.Name.Split(".")[0].Trim();
    string gtResult = filename.Split("-")[0].Trim();

    using Mat src = Cv2.ImRead(jpgFile);

    // 1. 转换为灰度图
    using Mat gray = new Mat();
    Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);
    ShowImage("gray", gray);
    sumGray += ShowOcrResult("gray", gray, gtResult);

    // 2. 去除噪声
    using Mat denoised = new Mat();
    Cv2.GaussianBlur(gray, denoised, new Size(3, 3), 0);
    ShowImage("denoised", denoised);
    sumDenoised += ShowOcrResult("denoised", denoised, gtResult);

    // 3. 对比度增强（直方图均衡化）
    using Mat equalized = new Mat();
    Cv2.EqualizeHist(denoised, equalized);
    ShowImage("equalized", equalized);
    sumEqualized += ShowOcrResult("equalized", equalized, gtResult);

    using Mat kernelSharp = Mat.FromPixelData(3, 3, MatType.CV_32F, new float[,]
    {
        { 0, -1, 0 },
        { -1, 5, -1 },
        { 0, -1, 0 }
    });

    // 4. gray 图像锐化
    using Mat graySharpened = new Mat();
    Cv2.Filter2D(gray, graySharpened, -1, kernelSharp);
    ShowImage("gray sharpened", graySharpened);
    sumGraySharpened += ShowOcrResult("gray sharpened", graySharpened, gtResult);

    // 5. denoised 图像锐化
    using Mat denoisedSharpened = new Mat();
    Cv2.Filter2D(denoised, denoisedSharpened, -1, kernelSharp);
    ShowImage("denoised sharpened", denoisedSharpened);
    sumDenoisedSharpened += ShowOcrResult("denoised sharpened", denoisedSharpened, gtResult);

    // 6. denoised 图像锐化
    using Mat equalizedSharpened = new Mat();
    Cv2.Filter2D(equalized, equalizedSharpened, -1, kernelSharp);
    ShowImage("equalized sharpened", equalizedSharpened);
    sumEqualizedSharpened += ShowOcrResult("equalized sharpened", equalizedSharpened, gtResult);
}

Console.WriteLine($"Average similarity of gray: {sumGray / jpgFiles.Length}");
Console.WriteLine($"Average similarity of denoised: {sumDenoised / jpgFiles.Length}");
Console.WriteLine($"Average similarity of equalized: {sumEqualized / jpgFiles.Length}");
Console.WriteLine($"Average similarity of gray sharpened: {sumGraySharpened / jpgFiles.Length}");
Console.WriteLine($"Average similarity of denoised sharpened: {sumDenoisedSharpened / jpgFiles.Length}");
Console.WriteLine($"Average similarity of equalized sharpened: {sumEqualizedSharpened / jpgFiles.Length}");