using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace VideoTime
{
    public static class CliRunner
    {
        private const int ATTACH_PARENT_PROCESS = -1;
        private const int STD_OUTPUT_HANDLE = -11;
        private const int STD_ERROR_HANDLE = -12;

        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int dwProcessId);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        public static bool TryRun(string[] args, out int exitCode)
        {
            exitCode = 0;

            AttachConsoleForOutput();

            string folder = null;
            bool recursive = false;
            string output = null;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "-d" || a == "--folder")
                {
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
                        folder = VideoScanner.NormalizePath(args[++i]);
                    else
                    {
                        Console.Error.WriteLine("错误: 缺少 -d/--folder 的参数（文件夹路径）");
                        PrintUsage();
                        exitCode = 1;
                        return true;
                    }
                }
                else if (a == "-r" || a == "--recursive")
                {
                    recursive = true;
                }
                else if (a == "-o" || a == "--out")
                {
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
                        output = args[++i];
                    else
                    {
                        Console.Error.WriteLine("错误: 缺少 -o/--out 的参数（输出文件）");
                        PrintUsage();
                        exitCode = 1;
                        return true;
                    }
                }
                else if (a == "-h" || a == "--help")
                {
                    PrintUsage();
                    return true;
                }
                else
                {
                    Console.Error.WriteLine("未知参数: " + a);
                    PrintUsage();
                    exitCode = 1;
                    return true;
                }
            }

            if (string.IsNullOrEmpty(folder))
            {
                PrintUsage();
                exitCode = 1;
                return true;
            }

            if (!Directory.Exists(folder))
            {
                Console.Error.WriteLine("错误: 文件夹不存在: " + folder);
                exitCode = 1;
                return true;
            }

            try
            {
                ScanResult result = VideoScanner.Run(folder, recursive, System.Threading.CancellationToken.None);

                Console.WriteLine("总时间: " + VideoScanner.Format(result.TotalSeconds));
                int videoCount = result.TotalFileCount;
                Console.WriteLine("已统计视频: " + videoCount + " 个");
                if (result.FailCount > 0 || result.DirFail > 0 || result.DepthSkipped > 0)
                    Console.WriteLine("缺失: " + result.FailCount + " 个文件读取失败；" + result.DirFail + " 个目录无法访问；" + result.DepthSkipped + " 个超深目录省略");

                Log.Append("文件夹: " + folder + " | 总时间: " + VideoScanner.Format(result.TotalSeconds) + "（命令行模式）");

                if (!string.IsNullOrEmpty(output))
                {
                    string format = output.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ? "html" : "csv";
                    ReportExporter.Export(output, result, format);
                    Console.WriteLine("报表已保存: " + Path.GetFullPath(output));
                }
                else
                {
                    PrintTree(result);
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("错误: " + ex.Message);
                Log.Append("查询异常（命令行模式）: " + folder + " | " + ex.Message, LogLevel.Error);
                exitCode = 1;
                return true;
            }
        }

        private static void AttachConsoleForOutput()
        {
            try
            {
                if (!AttachConsole(ATTACH_PARENT_PROCESS)) return;

                IntPtr hOut = GetStdHandle(STD_OUTPUT_HANDLE);
                if (hOut != IntPtr.Zero && hOut != new IntPtr(-1))
                {
                    var outFs = new FileStream(new SafeFileHandle(hOut, false), FileAccess.Write);
                    var outSw = new StreamWriter(outFs, Encoding.Default) { AutoFlush = true };
                    Console.SetOut(outSw);
                }

                IntPtr hErr = GetStdHandle(STD_ERROR_HANDLE);
                if (hErr != IntPtr.Zero && hErr != new IntPtr(-1))
                {
                    var errFs = new FileStream(new SafeFileHandle(hErr, false), FileAccess.Write);
                    var errSw = new StreamWriter(errFs, Encoding.Default) { AutoFlush = true };
                    Console.SetError(errSw);
                }
            }
            catch { }
        }

        private static void PrintUsage()
        {
            Console.WriteLine("用法: VideoTime.exe -d <文件夹> [-r] [-o <输出文件>]");
            Console.WriteLine("  -d, --folder  要统计的文件夹路径（必填）");
            Console.WriteLine("  -r, --recursive  递归统计子文件夹（默认不递归）");
            Console.WriteLine("  -o, --out  输出报表文件（.csv 或 .html；不指定则在控制台打印结果树）");
            Console.WriteLine("  -h, --help  显示此帮助");
        }

        private static void PrintTree(ScanResult result)
        {
            foreach (var r in result.FolderResults)
            {
                int depth = VideoScanner.DepthOf(result, r.FolderPath);
                string indent = new string(' ', depth * 2);
                string name = Path.GetFileName(r.FolderPath);
                if (string.IsNullOrEmpty(name)) name = r.FolderPath;
                Console.WriteLine(indent + name + "  " + VideoScanner.Format(r.TotalSeconds) + "  [视频" + r.FileCount + "]");
            }
        }
    }
}
