using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace VideoTime
{
    public static class ReportExporter
    {
        public static void Export(string path, ScanResult result, string format)
        {
            string content = (format ?? "csv").ToLowerInvariant() == "html" ? BuildHtml(result) : BuildCsv(result);
            File.WriteAllText(path, content, new UTF8Encoding(true));
        }

        public static string BuildCsv(ScanResult result)
        {
            var sb = new StringBuilder();
            sb.AppendLine("文件夹,总时长,时长(秒),视频数");
            foreach (var r in result.FolderResults)
            {
                int depth = VideoScanner.DepthOf(result, r.FolderPath);
                string label = new string(' ', depth * 2) + r.FolderPath;
                sb.Append(CsvField(label)).Append(',')
                  .Append(CsvField(VideoScanner.Format(r.TotalSeconds))).Append(',')
                  .Append(r.TotalSeconds.ToString("0.###")).Append(',')
                  .Append(r.FileCount).AppendLine();
            }

            sb.AppendLine();
            AppendFailuresCsv(sb, result);
            return sb.ToString();
        }

        public static string BuildHtml(ScanResult result)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"zh-CN\"><head><meta charset=\"utf-8\">");
            sb.AppendLine("<title>时间统计报表</title>");
            sb.AppendLine("<style>");
            sb.AppendLine("body{font-family:'Microsoft YaHei',sans-serif;margin:24px;color:#222}");
            sb.AppendLine("h1{font-size:20px} h2{font-size:16px;margin-top:28px}");
            sb.AppendLine("table{border-collapse:collapse;width:100%;font-size:13px}");
            sb.AppendLine("th,td{border:1px solid #ccc;padding:6px 10px;text-align:left;white-space:nowrap}");
            sb.AppendLine("th{background:#f0f0f0}");
            sb.AppendLine(".num{text-align:right}");
            sb.AppendLine(".fail{color:#b00020}");
            sb.AppendLine("</style></head><body>");
            sb.AppendLine("<h1>视频时长统计报表</h1>");
            sb.AppendLine("<p>总时间: <b>" + HtmlEscape(VideoScanner.Format(result.TotalSeconds)) + "</b>"
                        + "（" + result.TotalSeconds.ToString("0.###") + " 秒）"
                        + "，共 " + (result.FolderResults.Count > 0 ? result.FolderResults[0].FileCount : 0) + " 个视频</p>");
            sb.AppendLine("<table><tr><th>文件夹</th><th>总时长</th><th class=\"num\">时长(秒)</th><th class=\"num\">视频数</th></tr>");
            foreach (var r in result.FolderResults)
            {
                int depth = VideoScanner.DepthOf(result, r.FolderPath);
                string indent = new string('&', depth) + "&nbsp;&nbsp;";
                sb.AppendLine("<tr><td>" + indent + HtmlEscape(r.FolderPath) + "</td>"
                            + "<td>" + HtmlEscape(VideoScanner.Format(r.TotalSeconds)) + "</td>"
                            + "<td class=\"num\">" + r.TotalSeconds.ToString("0.###") + "</td>"
                            + "<td class=\"num\">" + r.FileCount + "</td></tr>");
            }
            sb.AppendLine("</table>");

            AppendFailuresHtml(sb, result);

            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        private static void AppendFailuresCsv(StringBuilder sb, ScanResult result)
        {
            bool any = result.FailedFiles.Count > 0 || result.FailedDirs.Count > 0 || result.SkippedDirs.Count > 0;
            sb.AppendLine("失败明细");
            if (!any)
            {
                sb.AppendLine("（无缺失记录）");
                return;
            }
            sb.AppendLine("类型,路径,原因");
            foreach (var it in result.FailedFiles)
                sb.AppendLine(VideoScanner.LabelFileFailed + "," + CsvField(it.Path) + "," + CsvField(it.Reason));
            foreach (var it in result.FailedDirs)
                sb.AppendLine(VideoScanner.LabelDirFailed + "," + CsvField(it.Path) + "," + CsvField(it.Reason));
            foreach (var d in result.SkippedDirs)
                sb.AppendLine(VideoScanner.DepthSkippedLabel(VideoScanner.MaxDepth) + "," + CsvField(d) + ",");
        }

        private static void AppendFailuresHtml(StringBuilder sb, ScanResult result)
        {
            bool any = result.FailedFiles.Count > 0 || result.FailedDirs.Count > 0 || result.SkippedDirs.Count > 0;
            sb.AppendLine("<h2>失败明细</h2>");
            if (!any)
            {
                sb.AppendLine("<p>（无缺失记录）</p>");
                return;
            }
            sb.AppendLine("<table><tr><th>类型</th><th>路径</th><th>原因</th></tr>");
            foreach (var it in result.FailedFiles)
                sb.AppendLine("<tr class=\"fail\"><td>" + VideoScanner.LabelFileFailed + "</td><td>" + HtmlEscape(it.Path) + "</td><td>" + HtmlEscape(it.Reason) + "</td></tr>");
            foreach (var it in result.FailedDirs)
                sb.AppendLine("<tr class=\"fail\"><td>" + VideoScanner.LabelDirFailed + "</td><td>" + HtmlEscape(it.Path) + "</td><td>" + HtmlEscape(it.Reason) + "</td></tr>");
            foreach (var d in result.SkippedDirs)
                sb.AppendLine("<tr class=\"fail\"><td>" + VideoScanner.DepthSkippedLabel(VideoScanner.MaxDepth) + "</td><td>" + HtmlEscape(d) + "</td><td></td></tr>");
            sb.AppendLine("</table>");
        }

        private static readonly char[] CsvSpecialChars = { ',', '"', '\r', '\n' };

        private static string CsvField(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            bool needQuote = value.IndexOfAny(CsvSpecialChars) >= 0;
            if (!needQuote) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static string HtmlEscape(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }
    }
}
