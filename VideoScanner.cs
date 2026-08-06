using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace VideoTime
{
    public class ScanProgress
    {
        public string Phase;
        public int Processed;
        public int Total;
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
        public int TotalFileCount;
        internal HashSet<string> FolderSet;
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

        public const string LabelFileFailed = "文件读取失败";
        public const string LabelDirFailed = "目录无法访问";

        public static string DepthSkippedLabel(int maxDepth)
        {
            return "超过" + maxDepth + "层目录已省略";
        }

        /// <summary>统一路径分隔符（'/' 归一化为 '\\'），并去掉首尾空白与包围的引号，使两种写法等价、可混合使用。</summary>
        public static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;
            return path.Trim().Trim('"').Replace('/', '\\');
        }

        internal static void EnsureFolderSet(ScanResult result)
        {
            if (result.FolderSet != null) return;
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in result.FolderResults)
                set.Add(NormalizePath(r.FolderPath).TrimEnd('\\'));
            result.FolderSet = set;
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

            result.TotalFileCount = files.Count;

            progress?.Report(new ScanProgress { Phase = "parse", Processed = 0, Total = files.Count });

            int processed = 0;
            var throttle = new ProgressThrottle();
            Action<string> fileDone = path =>
            {
                int n = Interlocked.Increment(ref processed);
                if (throttle.ShouldReport() || n >= files.Count)
                    progress?.Report(new ScanProgress { Phase = "parse", Processed = n, Total = files.Count });
            };

            int threads = Math.Max(2, Environment.ProcessorCount);
            Dictionary<string, double> perFile = DurationParser.ReadAll(files, out int fail, out List<FailureRecord> failed, threads, ct, fileDone);
            result.FailCount = fail;
            result.FailedFiles = failed;

            Aggregate(items, perFile, result, ct);

            return result;
        }

        public static ScanResult RunMultiple(string[] roots, bool recursive, CancellationToken ct, IProgress<ScanProgress> progress = null)
        {
            if (roots == null || roots.Length == 0)
                return new ScanResult();

            if (roots.Length == 1)
                return Run(roots[0], recursive, ct, progress);

            var results = new ScanResult[roots.Length];
            var perRootItems = new List<FolderItem>[roots.Length];
            int totalFiles = 0;

            progress?.Report(new ScanProgress { Phase = "collect", Processed = 0, Total = roots.Length });

            for (int i = 0; i < roots.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                var items = new List<FolderItem>();
                var tempResult = new ScanResult();
                CollectFoldersRecursive(roots[i], recursive, 0, items, tempResult, ct);
                perRootItems[i] = items;
                results[i] = tempResult;
                int count = 0;
                foreach (var it in items)
                    count += it.Files.Length;
                totalFiles += count;
                progress?.Report(new ScanProgress { Phase = "collect", Processed = i + 1, Total = roots.Length });
            }

            ct.ThrowIfCancellationRequested();

            progress?.Report(new ScanProgress { Phase = "parse", Processed = 0, Total = totalFiles });

            int processed = 0;
            var throttle = new ProgressThrottle();
            Action<string> fileDone = path =>
            {
                int n = Interlocked.Increment(ref processed);
                if (throttle.ShouldReport() || n >= totalFiles)
                    progress?.Report(new ScanProgress { Phase = "parse", Processed = n, Total = totalFiles });
            };

            // 收敛并行度：外层 root 与内层文件读取并发相乘，避免 threads² 过度并发
            int threads = Math.Max(2, Environment.ProcessorCount);
            int outerThreads = Math.Max(1, Math.Min(roots.Length, Math.Max(2, threads / 2)));
            int innerThreads = Math.Max(2, threads / outerThreads);

            Parallel.For(0, roots.Length, new ParallelOptions { MaxDegreeOfParallelism = outerThreads, CancellationToken = ct }, rootIdx =>
            {
                ct.ThrowIfCancellationRequested();
                var items = perRootItems[rootIdx];
                var tempResult = results[rootIdx];

                var files = new List<string>();
                foreach (var it in items) files.AddRange(it.Files);

                Dictionary<string, double> perFile = DurationParser.ReadAll(files, out int fail, out List<FailureRecord> failed, innerThreads, ct, fileDone);

                Aggregate(items, perFile, tempResult, ct);

                tempResult.FailCount = fail;
                tempResult.FailedFiles = failed;
            });

            var merged = new ScanResult();
            merged.TotalFileCount = totalFiles;
            foreach (var r in results)
            {
                if (r == null) continue;
                merged.FolderResults.AddRange(r.FolderResults);
                merged.FailedFiles.AddRange(r.FailedFiles);
                merged.FailedDirs.AddRange(r.FailedDirs);
                merged.SkippedDirs.AddRange(r.SkippedDirs);
                merged.FailCount += r.FailCount;
                merged.DirFail += r.DirFail;
                merged.DepthSkipped += r.DepthSkipped;
                merged.TotalSeconds += r.TotalSeconds;
            }

            return merged;
        }

        private static void CollectFoldersRecursive(string path, bool recursive, int depth, List<FolderItem> items, ScanResult result, CancellationToken ct)
        {
            path = NormalizePath(path);
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
            if (result == null || result.FolderResults.Count == 0) return 0;

            EnsureFolderSet(result);
            string path = NormalizePath(folderPath).TrimEnd('\\');
            string node = path;
            string ownerRoot = null;

            while (node != null)
            {
                if (result.FolderSet.Contains(node))
                {
                    string parent = Path.GetDirectoryName(node);
                    bool parentInSet = parent != null && result.FolderSet.Contains(parent.TrimEnd('\\'));
                    if (!parentInSet)
                    {
                        ownerRoot = node;
                        break;
                    }
                }
                node = Path.GetDirectoryName(node);
                if (node != null) node = node.TrimEnd('\\');
            }

            if (ownerRoot == null)
            {
                string root = NormalizePath(result.FolderResults[0].FolderPath).TrimEnd('\\');
                return RelativeDepth(path, root);
            }
            return RelativeDepth(path, ownerRoot);
        }

        private static int RelativeDepth(string path, string root)
        {
            if (path.Length <= root.Length) return 0;
            string rel = path.Substring(root.Length).TrimStart('\\', '/');
            return rel.Length == 0 ? 0 : rel.Split('\\', '/').Length;
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
