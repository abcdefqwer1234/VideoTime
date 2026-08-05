using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using VideoTime;

internal static class ProbeCsvInvariant
{
    private static int _passed, _failed;

    private static void Check(bool cond, string name, string detail)
    {
        if (cond) { _passed++; Console.WriteLine("PASS: " + name); }
        else { _failed++; Console.WriteLine("FAIL: " + name + " :: " + detail); }
    }

    private static ScanResult MakeResult()
    {
        var result = new ScanResult { TotalSeconds = 1.5 };
        result.FolderResults.Add(new FolderResult { FolderPath = @"C:\video", TotalSeconds = 1.5, FileCount = 3 });
        result.FolderResults.Add(new FolderResult { FolderPath = @"C:\video\sub", TotalSeconds = 0.25, FileCount = 1 });
        return result;
    }

    [STAThread]
    private static void Main()
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        var de = CultureInfo.GetCultureInfo("de-DE");
        CultureInfo.CurrentCulture = de;
        Thread.CurrentThread.CurrentCulture = de;
        try
        {
            Console.WriteLine("当前区域性: " + CultureInfo.CurrentCulture.Name + "，小数分隔符: '" + de.NumberFormat.NumberDecimalSeparator + "'");
            ScanResult result = MakeResult();

            string csv = ReportExporter.BuildCsv(result);
            Console.WriteLine("--- CSV 片段 ---");
            Console.WriteLine(csv);
            Check(csv.Contains("1.5"), "CSV 秒数列使用 '.' 分隔", "de-DE 下 1.5 应为 '1.5'");
            Check(!csv.Contains("1,5"), "CSV 不包含 ',' 小数", "不应出现 '1,5'");

            string html = ReportExporter.BuildHtml(result);
            Console.WriteLine("--- HTML 片段 ---");
            Console.WriteLine(html);
            Check(html.Contains("1.5"), "HTML 秒数列使用 '.' 分隔", "de-DE 下 1.5 应为 '1.5'");
            Check(!html.Contains("1,5"), "HTML 不包含 ',' 小数", "不应出现 '1,5'");
            Check(html.Contains("0.25"), "HTML 保留三位小数 0.25", "0.### 应输出 0.25");

            string path = Path.Combine(Path.GetTempPath(), "vt_csv_" + Guid.NewGuid().ToString("N") + ".csv");
            try
            {
                ReportExporter.Export(path, result, "csv");
                string onDisk = File.ReadAllText(path, Encoding.UTF8);
                Check(onDisk.Contains("1.5"), "Export 落盘 CSV 使用 '.' 分隔", "文件内容应含 '1.5'");
                Check(!onDisk.Contains("1,5"), "Export 落盘 CSV 不包含 ',' 小数", "文件不应含 '1,5'");
            }
            finally { try { File.Delete(path); } catch { } }
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
            Thread.CurrentThread.CurrentCulture = saved;
        }

        Console.WriteLine();
        Console.WriteLine("Total: Passed " + _passed + ", Failed " + _failed);
        Environment.ExitCode = _failed > 0 ? 1 : 0;
    }
}
