using System;
using System.Threading;
using System.Windows.Forms;

namespace VideoTime
{
    internal static class Program
    {
        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length > 0)
            {
                CliRunner.TryRun(args, out int exitCode);
                Environment.Exit(exitCode);
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.ThreadException += Application_ThreadException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            Application.Run(new Form1());
        }

        private static void Application_ThreadException(object sender, ThreadExceptionEventArgs e)
        {
            HandleError(e.Exception, "界面线程发生未处理异常");
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            HandleError(e.ExceptionObject as Exception, "程序发生未处理异常");
        }

        private static void HandleError(Exception ex, string message)
        {
            string detail = ex == null ? "" : ex.ToString();
            try
            {
                Log.Append(message + ": " + detail, LogLevel.Error);
            }
            catch { }
            try
            {
                Dialogs.Show("错误", message + "：\n" + (ex == null ? "" : ex.Message), MessageBoxIcon.Error);
            }
            catch { }
        }
    }
}
