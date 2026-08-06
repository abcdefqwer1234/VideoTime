using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using VideoTime;

// 性能探针：合成 ~2000 个合法 MP4 做整目录扫描（并行解析吞吐）+ 大结果（5000 文件夹）导出耗时。
// 阈值取实际耗时的 10x+ 余量（扫描通常 <5s、导出 <1s），只在明显回归时失败。
internal static class ProbeScannerThroughput
{
    private static int _passed, _failed;

    private static void Check(bool cond, string name, string detail)
    {
        if (cond) { _passed++; Console.WriteLine("PASS: " + name); }
        else { _failed++; Console.WriteLine("FAIL: " + name + " :: " + detail); }
    }

    private static void WriteBE32(byte[] b, int off, uint v)
    {
        b[off] = (byte)(v >> 24); b[off + 1] = (byte)(v >> 16); b[off + 2] = (byte)(v >> 8); b[off + 3] = (byte)v;
    }

    private static byte[] MakeMp4(uint durMs)
    {
        byte[] ftyp = { 0x00, 0x00, 0x00, 0x14, 0x66, 0x74, 0x79, 0x70, 0x69, 0x73, 0x6F, 0x6D, 0x00, 0x00, 0x00, 0x00, 0x69, 0x73, 0x6F, 0x6D };
        byte[] mvhd = new byte[108];
        WriteBE32(mvhd, 0, 108);
        mvhd[4] = (byte)'m'; mvhd[5] = (byte)'v'; mvhd[6] = (byte)'h'; mvhd[7] = (byte)'d';
        WriteBE32(mvhd, 20, 1000);
        WriteBE32(mvhd, 24, durMs);
        byte[] moov = new byte[8 + mvhd.Length];
        WriteBE32(moov, 0, (uint)moov.Length);
        moov[4] = (byte)'m'; moov[5] = (byte)'o'; moov[6] = (byte)'o'; moov[7] = (byte)'v';
        Buffer.BlockCopy(mvhd, 0, moov, 8, mvhd.Length);
        byte[] all = new byte[ftyp.Length + moov.Length];
        Buffer.BlockCopy(ftyp, 0, all, 0, ftyp.Length);
        Buffer.BlockCopy(moov, 0, all, ftyp.Length, moov.Length);
        return all;
    }

    private static ScanResult MakeBigResult(int folders, out double totalSecs)
    {
        var r = new ScanResult();
        var rnd = new Random(5);
        totalSecs = 0;
        for (int i = 0; i < folders; i++)
        {
            double s = 30 + rnd.NextDouble() * 3600;
            r.FolderResults.Add(new FolderResult { FolderPath = @"C:\video\folder" + i, TotalSeconds = s, FileCount = 1 + (i % 7) });
            totalSecs += s;
        }
        r.TotalSeconds = totalSecs;
        r.TotalFileCount = folders * 4;
        return r;
    }

    [STAThread]
    private static void Main()
    {
        string tmp = Path.Combine(Path.GetTempPath(), "vt_thr_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(tmp);
        const int fileCount = 2000;
        try
        {
            Console.WriteLine("== 生成 " + fileCount + " 个合成 MP4 ==");
            byte[] sample = MakeMp4(60000);
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < fileCount; i++)
                File.WriteAllBytes(Path.Combine(tmp, "f" + i + ".mp4"), sample);
            sw.Stop();
            Console.WriteLine("生成耗时 " + sw.ElapsedMilliseconds + " ms");

            Console.WriteLine("== 整目录扫描吞吐（" + fileCount + " 文件） ==");
            sw.Restart();
            var r = VideoScanner.Run(tmp, false, CancellationToken.None);
            sw.Stop();
            int scanMs = (int)sw.ElapsedMilliseconds;
            Console.WriteLine("扫描耗时 " + scanMs + " ms，TotalSeconds=" + r.TotalSeconds + "，TotalFileCount=" + r.TotalFileCount);
            Check(r.TotalFileCount == fileCount, "TotalFileCount = " + fileCount, "got " + r.TotalFileCount);
            Check(Math.Abs(r.TotalSeconds - fileCount * 60.0) <= 1.0, "TotalSeconds = " + (fileCount * 60), "got " + r.TotalSeconds);
            Check(scanMs < 60000, "扫描吞吐 < 60s（实测 " + scanMs + " ms）", "got " + scanMs + " ms");

            Console.WriteLine("== 大结果导出吞吐（5000 文件夹） ==");
            var big = MakeBigResult(5000, out double bigSecs);
            sw.Restart();
            string csv = ReportExporter.BuildCsv(big);
            sw.Stop();
            int csvMs = (int)sw.ElapsedMilliseconds;
            sw.Restart();
            string html = ReportExporter.BuildHtml(big);
            sw.Stop();
            int htmlMs = (int)sw.ElapsedMilliseconds;
            Console.WriteLine("CSV 耗时 " + csvMs + " ms（" + csv.Length + " 字符），HTML 耗时 " + htmlMs + " ms（" + html.Length + " 字符）");
            Check(bigSecs > 0, "构造数据有效（总时长 " + bigSecs + "）", "got " + bigSecs);
            Check(csvMs < 10000, "大结果 CSV 导出 < 10s（实测 " + csvMs + " ms）", "got " + csvMs + " ms");
            Check(htmlMs < 10000, "大结果 HTML 导出 < 10s（实测 " + htmlMs + " ms）", "got " + htmlMs + " ms");
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { }
        }

        Console.WriteLine();
        Console.WriteLine("Total: Passed " + _passed + ", Failed " + _failed);
        Environment.ExitCode = _failed > 0 ? 1 : 0;
    }
}
