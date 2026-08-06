using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using VideoTime;

// 复现用户报告：E:\ 查询 -> 过滤界面时长下限=0（无上限）-> 过滤 -> 点击清除 严重卡死。
// 通过反射驱动真实 Form1；自动关闭扫描后的模态弹窗（如“扫描完成，但存在缺失”），
// 避免扫描耗时受人工点击影响；看门狗基于“阶段持续时间”判断，避免误报。
internal static class ProbeFilterClearFreeze
{
    private static int _passed, _failed;

    private static void Check(bool cond, string name, string detail)
    {
        if (cond) { _passed++; Console.WriteLine("PASS: " + name); }
        else { _failed++; Console.WriteLine("FAIL: " + name + " :: " + detail); }
    }

    private static Form1 _f;
    private static Button _startBtn;
    private static TextBox _showTime;
    private static TextBox _txtDoc;
    private static TextBox _txtDurMin;
    private static Button _btnFilter;
    private static Button _btnClear;
    private static MethodInfo _startClick;
    private static MethodInfo _filterClick;
    private static MethodInfo _clearClick;
    private static System.Windows.Forms.Timer _pump;
    private static Stopwatch _sw;
    private static int _phase;
    private static long _phaseStartMs;
    private static System.Threading.Timer _watchdog;
    private static int _watchdogFired;
    private static long _applyMs = -1;
    private static long _clearMs = -1;

    private static void SetPhase(int p)
    {
        _phase = p;
        _phaseStartMs = _sw.ElapsedMilliseconds;
    }

    private static void DismissDialogs()
    {
        // 自动关闭非主窗体的模态弹窗（扫描完成提示等），避免人工点击干扰
        try
        {
            foreach (Form f in Application.OpenForms)
            {
                if (f != _f && f.Visible && f.Modal)
                    f.DialogResult = DialogResult.OK;
            }
        }
        catch { }
    }

    private static void Finish(int exitCode)
    {
        var wd = Interlocked.Exchange(ref _watchdog, null);
        if (wd != null) { try { wd.Dispose(); } catch { } }
        _pump.Stop();
        try { _f.Close(); } catch { }
        Application.Exit();
        Environment.ExitCode = exitCode;
    }

    private static void Tick(object sender, EventArgs e)
    {
        DismissDialogs();

        switch (_phase)
        {
            case 0: // 等待扫描完成（弹窗已自动关闭）
                if (_startBtn.Enabled && _showTime.Text.StartsWith("总时间"))
                {
                    Console.WriteLine("INFO: 扫描+建树完成 " + _sw.ElapsedMilliseconds + " ms，ShowTime=" + _showTime.Text);
                    _txtDurMin.Text = "0";
                    SetPhase(1);
                    _applyClick();
                    _applyMs = _phaseStartMs > 0 ? _sw.ElapsedMilliseconds : -1;
                    Console.WriteLine("INFO: ApplyFilter(下限0) 耗时 " + (_sw.ElapsedMilliseconds - _phaseStartMs) + " ms，ShowTime=" + _showTime.Text);
                    SetPhase(2);
                    _clearClick.Invoke(_f, new object[] { _btnClear, EventArgs.Empty });
                    _clearMs = _sw.ElapsedMilliseconds - _phaseStartMs;
                    Console.WriteLine("INFO: ClearFilter 耗时 " + _clearMs + " ms，ShowTime=" + _showTime.Text);
                    SetPhase(3);
                    Check(_applyMs >= 0, "过滤(下限0) 正常返回", "apply=" + _applyMs + " ms");
                    Check(_clearMs < 10000, "清除在 10s 内完成", "clear=" + _clearMs + " ms");
                    Console.WriteLine();
                    Console.WriteLine("Total: Passed " + _passed + ", Failed " + _failed);
                    Finish(_failed > 0 ? 1 : 0);
                }
                else if (_sw.ElapsedMilliseconds > 180000)
                {
                    Check(false, "扫描 E:\\ 180s 未完成", "ShowTime=" + _showTime.Text);
                    Console.WriteLine("Total: Passed " + _passed + ", Failed " + _failed);
                    Finish(1);
                }
                break;
        }
    }

    private static void _applyClick()
    {
        _filterClick.Invoke(_f, new object[] { _btnFilter, EventArgs.Empty });
    }

    private static void WatchdogTick(object state)
    {
        // 仅当“过滤/清除”阶段持续超过 10s 才判定卡死（避免阶段切换瞬间的误报）
        if ((_phase == 1 || _phase == 2) && _sw.ElapsedMilliseconds - _phaseStartMs > 10000
            && Interlocked.Exchange(ref _watchdogFired, 1) == 0)
        {
            Console.WriteLine("FAIL: UI 线程在过滤/清除阶段卡死超过 10s");
            Console.WriteLine("Total: Passed " + _passed + ", Failed " + _failed);
            Environment.Exit(1);
        }
    }

    [STAThread]
    private static void Main()
    {
        if (!Directory.Exists("E:\\"))
        {
            Console.WriteLine("SKIP: E:\\ 不存在，跳过");
            Console.WriteLine("Total: Passed 0, Failed 0");
            return;
        }

        _f = new Form1();
        _f.Load += (s, e) =>
        {
            var t = typeof(Form1);
            _startBtn = _f.Controls.Find("Start", true)[0] as Button;
            _showTime = _f.Controls.Find("ShowTime", true)[0] as TextBox;
            _txtDoc = _f.Controls.Find("TextBox_Doc", true)[0] as TextBox;
            _txtDurMin = _f.Controls.Find("txtDurMin", true)[0] as TextBox;
            _btnFilter = _f.Controls.Find("btnFilter", true)[0] as Button;
            _btnClear = _f.Controls.Find("btnFilterClear", true)[0] as Button;
            var cbSub = _f.Controls.Find("CbSubfolders", true)[0] as CheckBox;
            if (cbSub != null) cbSub.Checked = true;
            _startClick = t.GetMethod("Start_Click", BindingFlags.Instance | BindingFlags.NonPublic);
            _filterClick = t.GetMethod("BtnFilter_Click", BindingFlags.Instance | BindingFlags.NonPublic);
            _clearClick = t.GetMethod("BtnFilterClear_Click", BindingFlags.Instance | BindingFlags.NonPublic);
        };
        _f.Shown += (s, e) =>
        {
            _txtDoc.Text = "E:\\";
            _sw = Stopwatch.StartNew();
            SetPhase(0);
            _startClick.Invoke(_f, new object[] { _startBtn, EventArgs.Empty });
            _pump = new System.Windows.Forms.Timer();
            _pump.Interval = 20;
            _pump.Tick += Tick;
            _pump.Start();
            _watchdog = new System.Threading.Timer(WatchdogTick, null, 5000, 500);
        };

        Application.Run(_f);
    }
}
