using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace VideoTime
{
    public class ScanProgress
    {
        public string Phase;
        public int Processed;
        public int Total;
        public string CurrentFile;
    }

    public class ScanResult
    {
        public double TotalSeconds;
        public List<FolderResult> FolderResults = new List<FolderResult>();
        public List<FailureRecord> FailedFiles = new List<FailureRecord>();
        public List<FailureRecord> FailedDirs = new List<FailureRecord>();
        public List<string> SkippedDirs = new List<string>();
        public int FailCount;
        public int DirFail;
        public int DepthSkipped;
    }

    public class FolderResult
    {
        public string FolderPath { get; set; }
        public double TotalSeconds { get; set; }
        public int FileCount { get; set; }
    }

    public class FailureRecord
    {
        public string Path;
        public string Reason;
    }

    internal class FolderItem
    {
        public string FolderPath { get; set; }
        public string[] Files { get; set; }
        public string[] SubDirs { get; set; }
    }

    public static class VideoScanner
    {
        public const int MaxDepth = 50;
        private const int MaxDetailLines = 200;

        public const string LabelFileFailed = "文件读取失败";
        public const string LabelDirFailed = "目录无法访问";

        public static string DepthSkippedLabel(int maxDepth)
        {
            return "超过" + maxDepth + "层目录已省略";
        }

        public static ScanResult Run(string root, bool recursive, CancellationToken ct, IProgress<ScanProgress> progress = null)
        {
            var result = new ScanResult();
            progress?.Report(new ScanProgress { Phase = "collect" });

            var items = new List<FolderItem>();
            CollectFoldersRecursive(root, recursive, 0, items, result, ct);

            ct.ThrowIfCancellationRequested();

            var files = new List<string>();
            foreach (var it in items) files.AddRange(it.Files);

            progress?.Report(new ScanProgress { Phase = "parse", Processed = 0, Total = files.Count });

            int processed = 0;
            var throttle = new ProgressThrottle();
            Action<string> fileDone = path =>
            {
                int n = Interlocked.Increment(ref processed);
                if (throttle.ShouldReport() || n >= files.Count)
                    progress?.Report(new ScanProgress { Phase = "parse", Processed = n, Total = files.Count, CurrentFile = path });
            };

            int threads = Math.Max(2, Environment.ProcessorCount);
            Dictionary<string, double> perFile = DurationParser.ReadAll(files, out int fail, out List<FailureRecord> failed, threads, ct, fileDone);
            result.FailCount = fail;
            result.FailedFiles = failed;

            Aggregate(items, perFile, result, ct);

            return result;
        }

        private static void CollectFoldersRecursive(string path, bool recursive, int depth, List<FolderItem> items, ScanResult result, CancellationToken ct)
        {
            if (ct.IsCancellationRequested) return;
            if (depth > MaxDepth)
            {
                Interlocked.Increment(ref result.DepthSkipped);
                result.SkippedDirs.Add(path);
                return;
            }

            try
            {
                string[] files = DurationParser.GetVideoFiles(path);
                string[] subDirs = recursive ? SafeGetDirectories(path) : new string[0];

                items.Add(new FolderItem
                {
                    FolderPath = path,
                    Files = files,
                    SubDirs = subDirs
                });

                foreach (string dir in subDirs)
                    CollectFoldersRecursive(dir, recursive, depth + 1, items, result, ct);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref result.DirFail);
                result.FailedDirs.Add(new FailureRecord { Path = path, Reason = DurationParser.ShortReason(ex) });
            }
        }

        private static string[] SafeGetDirectories(string path)
        {
            string[] dirs = Directory.GetDirectories(path);
            if (dirs.Length == 0) return dirs;
            return Array.FindAll(dirs, d => !IsReparsePoint(d));
        }

        private static bool IsReparsePoint(string path)
        {
            try
            {
                FileAttributes attr = File.GetAttributes(path);
                return (attr & FileAttributes.ReparsePoint) != 0;
            }
            catch { return true; }
        }

        private static void Aggregate(List<FolderItem> items, Dictionary<string, double> perFile, ScanResult result, CancellationToken ct)
        {
            var totals = new Dictionary<string, double>();
            var counts = new Dictionary<string, int>();

            for (int i = items.Count - 1; i >= 0; i--)
            {
                ct.ThrowIfCancellationRequested();
                var item = items[i];

                double localTotal = 0;
                foreach (string file in item.Files)
                {
                    if (perFile.TryGetValue(file, out double sec))
                        localTotal += sec;
                }

                double subTotal = 0;
                int subCount = 0;
                foreach (string subDir in item.SubDirs)
                {
                    if (totals.TryGetValue(subDir, out double st))
                        subTotal += st;
                    if (counts.TryGetValue(subDir, out int sc))
                        subCount += sc;
                }

                double grandTotal = localTotal + subTotal;
                int grandCount = item.Files.Length + subCount;
                totals[item.FolderPath] = grandTotal;
                counts[item.FolderPath] = grandCount;

                result.FolderResults.Add(new FolderResult
                {
                    FolderPath = item.FolderPath,
                    TotalSeconds = grandTotal,
                    FileCount = grandCount
                });
            }

            result.FolderResults.Reverse();
            result.TotalSeconds = items.Count > 0 ? totals[items[0].FolderPath] : 0;
        }

        public static string Format(double totalSeconds)
        {
            long sec = (long)Math.Floor(totalSeconds);
            long h = sec / 3600;
            long m = (sec % 3600) / 60;
            long s = sec % 60;
            return h + "时" + m + "分" + s + "秒";
        }

        public static int DepthOf(ScanResult result, string folderPath)
        {
            string root = result.FolderResults.Count > 0 ? result.FolderResults[0].FolderPath : "";
            if (string.IsNullOrEmpty(root) || folderPath.Length <= root.Length) return 0;
            string rel = folderPath.Substring(root.Length).TrimStart('\\', '/');
            if (rel.Length == 0) return 0;
            return rel.Split('\\', '/').Length;
        }

        private class ProgressThrottle
        {
            private long _lastMs = 0;
            private const long IntervalMs = 100;

            public bool ShouldReport()
            {
                long now = Environment.TickCount;
                long last = Interlocked.Read(ref _lastMs);
                if (last == 0 || now - last >= IntervalMs)
                {
                    Interlocked.Exchange(ref _lastMs, now);
                    return true;
                }
                return false;
            }
        }
    }
}
