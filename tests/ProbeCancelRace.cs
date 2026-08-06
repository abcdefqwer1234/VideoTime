using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using VideoTime;

internal static class ProbeCancelRace
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

    // ---------- UI state ----------
    private static Form1 _f;
    private static Button _startBtn;
    private static Control _progressBar;
    private static TextBox _showTime;
    private static FieldInfo _scanCtsField;
    private static MethodInfo _startClick;
    private static System.Windows.Forms.Timer _pump;
    private static Stopwatch _sw;
    private static long _T;
    private static long _Tscan;
    private static int _phase;
    private static System.Threading.Timer _cancelTimer;
    private static int[] _sweepDelays;
    private static int _sweepIdx;
    private static int _sweepRound;
    private static int _total, _stuck, _canceledOutcome, _successOutcome;

    private static void CancelNow()
    {
        try { (_scanCtsField.GetValue(_f) as CancellationTokenSource)?.Cancel(); }
        catch { }
    }

    private static void ScheduleCancel(int delayMs)
    {
        var t = new System.Threading.Timer(_ => CancelNow(), null, delayMs, Timeout.Infinite);
        Interlocked.Exchange(ref _cancelTimer, t);
    }

    private static void DisarmCancel()
    {
        var t = Interlocked.Exchange(ref _cancelTimer, null);
        if (t != null) { try { t.Dispose(); } catch { } }
    }

    private static void StartScan()
    {
        _sw = Stopwatch.StartNew();
        _startClick.Invoke(_f, new object[] { _startBtn, EventArgs.Empty });
    }

    private static void Finish(int exitCode)
    {
        DisarmCancel();
        _pump.Stop();
        Application.Exit();
    }

    private static void Tick(object sender, EventArgs e)
    {
        switch (_phase)
        {
            // ---- 0: dry run ----
            case 0:
                _phase = 1;
                StartScan();
                break;
            case 1:
                if (_Tscan == 0 && _showTime.Text.StartsWith("总时间"))
                    _Tscan = _sw.ElapsedMilliseconds;
                if (_startBtn.Enabled)
                {
                    _T = _sw.ElapsedMilliseconds;
                    Console.WriteLine("INFO: 干跑耗时 " + _T + " ms，扫描阶段 " + _Tscan + " ms");
                    _phase = 2;
                }
                else if (_sw.ElapsedMilliseconds > 60000)
                {
                    Console.WriteLine("FAIL: 干跑 60s 未完成");
                    _failed++;
                    Finish(1);
                }
                break;

            // ---- 2/3: deterministic cancel (target mid-scan) ----
            case 2:
                _phase = 3;
                StartScan();
                ScheduleCancel((int)Math.Max(5, _Tscan * 0.4));
                break;
            case 3:
                if (_startBtn.Enabled)
                {
                    DisarmCancel();
                    Check(true, "正常取消: Start 重新启用", "enabled=" + _startBtn.Enabled);
                    Check(!_progressBar.Visible, "正常取消: 进度条已隐藏", "visible=" + _progressBar.Visible);
                    // 容忍竞态：取消定时若晚于扫描完成，则以成功收尾（严格的取消/成功判定由 4/5 竞态扫描负责）
                    bool canceled = _showTime.Text == "扫描已取消。";
                    bool reachedTerminal = canceled || _showTime.Text.StartsWith("总时间");
                    Check(reachedTerminal, "正常取消: 到达终态（取消或完成）", "got=" + _showTime.Text);
                    if (!canceled) Console.WriteLine("WARN: 取消定时晚于扫描完成，本次按完成处理");
                    _phase = 4;
                }
                else if (_sw.ElapsedMilliseconds > 60000)
                {
                    Check(false, "正常取消: 60s 未完成", "showTime=" + _showTime.Text + " progress=" + _progressBar.Visible);
                    Finish(1);
                }
                break;

            // ---- 4/5: race sweep ----
            case 4:
            {
                _phase = 5;
                long step = Math.Max(1, _Tscan / 50);
                long from = 0;
                long to = _Tscan + 60;
                var list = new System.Collections.Generic.List<int>();
                for (long d = from; d <= to; d += step)
                    list.Add((int)d);
                _sweepDelays = list.ToArray();
                _sweepIdx = 0;
                _sweepRound = 0;
                Console.WriteLine("INFO: 竞态扫描延迟 " + from + "~" + to + "ms，步进 " + step + "ms，共 " + _sweepDelays.Length + " 点/轮");
                StartScan();
                ScheduleCancel(_sweepDelays[0]);
                break;
            }
            case 5:
                if (_startBtn.Enabled)
                {
                    DisarmCancel();
                    _total++;
                    bool ok = !_progressBar.Visible && _showTime.Text != "正在取消…";
                    if (!ok) _stuck++;
                    else if (_showTime.Text == "扫描已取消。") _canceledOutcome++;
                    else _successOutcome++;
                    _sweepIdx++;
                    if (_sweepIdx >= _sweepDelays.Length)
                    {
                        _sweepRound++;
                        _sweepIdx = 0;
                    }
                    if (_sweepRound >= 2)
                    {
                        Check(_total > 0, "竞态扫描: 共执行 " + _total + " 次", "total=" + _total);
                        Check(_stuck == 0, "竞态扫描: 取消无卡死（stuck=" + _stuck + "）", "stuck=" + _stuck);
                        Check(_canceledOutcome > 0, "竞态扫描: 命中取消路径 " + _canceledOutcome + " 次 / 成功 " + _successOutcome + " 次", "canceled=" + _canceledOutcome + " success=" + _successOutcome);
                        Console.WriteLine();
                        Console.WriteLine("Total: Passed " + _passed + ", Failed " + _failed);
                        Environment.ExitCode = _failed > 0 ? 1 : 0;
                        Finish(Environment.ExitCode);
                    }
                    else
                    {
                        StartScan();
                        ScheduleCancel(_sweepDelays[_sweepIdx]);
                    }
                }
                else if (_sw.ElapsedMilliseconds > 60000)
                {
                    _total++;
                    _stuck++;
                    _sweepIdx++;
                    if (_sweepIdx >= _sweepDelays.Length)
                    {
                        _sweepRound++;
                        _sweepIdx = 0;
                    }
                    if (_sweepRound >= 2)
                    {
                        Check(false, "竞态扫描: 出现超时卡死", "total=" + _total + " stuck=" + _stuck);
                        Console.WriteLine();
                        Console.WriteLine("Total: Passed " + _passed + ", Failed " + _failed);
                        Environment.ExitCode = 1;
                        Finish(1);
                    }
                    else
                    {
                        StartScan();
                        ScheduleCancel(_sweepDelays[_sweepIdx]);
                    }
                }
                break;
        }
    }

    [STAThread]
    private static void Main()
    {
        string tmp = Path.Combine(Path.GetTempPath(), "vt_race_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        string dir = Path.Combine(tmp, "src");
        Directory.CreateDirectory(dir);
        const int fileCount = 1200;
        byte[] sample = MakeMp4(60000);
        for (int i = 0; i < fileCount; i++)
            File.WriteAllBytes(Path.Combine(dir, "f" + i + ".mp4"), sample);

        _f = new Form1();
        _f.Load += (s, e) =>
        {
            var t = _f.GetType();
            _startBtn = _f.Controls.Find("Start", true)[0] as Button;
            _progressBar = _f.Controls.Find("ProgressBar", true)[0];
            _showTime = _f.Controls.Find("ShowTime", true)[0] as TextBox;
            var textBoxDoc = _f.Controls.Find("TextBox_Doc", true)[0] as TextBox;
            _startClick = t.GetMethod("Start_Click", BindingFlags.Instance | BindingFlags.NonPublic);
            _scanCtsField = t.GetField("_scanCts", BindingFlags.Instance | BindingFlags.NonPublic);
            textBoxDoc.Text = dir;
            _pump = new System.Windows.Forms.Timer();
            _pump.Interval = 20;
            _pump.Tick += Tick;
        };
        _f.Shown += (s, e) => _pump.Start();

        Application.Run(_f);

        try { Directory.Delete(tmp, true); } catch { }
    }
}
