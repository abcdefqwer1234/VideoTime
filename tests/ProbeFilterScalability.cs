using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Forms;
using VideoTime;

// 回归探针：大型合成树 + 全展开 + 下限 0 过滤（全部匹配、无删除、文本不变）
// 旧实现会对全部节点无条件改 Text/Expand/IsExpanded 遍历（111 万节点 >300s 卡死），
// 修复后过滤与清除只做 O(N) 托管遍历 + 差异检查，应为亚秒级。
internal static class ProbeFilterScalability
{
    private static int Passed, Failed;
    private static void Assert(bool cond, string msg)
    {
        if (cond) { Passed++; }
        else { Failed++; Console.WriteLine("  FAIL: " + msg); }
    }

    private static void Build(TreeView tree, int top, int sub, int depth)
    {
        var stack = new List<(string p, int d)>();
        for (int i = 0; i < top; i++) stack.Add(("R" + i, 0));
        while (stack.Count > 0)
        {
            var (p, d) = stack[stack.Count - 1];
            stack.RemoveAt(stack.Count - 1);
            int videos = (d == depth) ? 2 : 0;
            double secs = (d == depth && videos > 0) ? ((p.GetHashCode() & 0x7FFFFFFF) % 3600) : 0;
            if (d < depth)
                for (int j = 0; j < sub; j++) stack.Add((p + "\\D" + j + "_" + d, d + 1));
            var n = new TreeNode(PathName(p) + "  " + VideoScanner.Format(secs) + "  [视频" + videos + "]");
            string parent = System.IO.Path.GetDirectoryName(p);
            if (parent != null && _map.TryGetValue(parent, out TreeNode pnode)) pnode.Nodes.Add(n);
            else tree.Nodes.Add(n);
            _map[p] = n;
        }
    }
    private static string PathName(string p) { int i = p.LastIndexOf('\\'); return i < 0 ? p : p.Substring(i + 1); }
    private static Dictionary<string, TreeNode> _map;

    private static void ExpandAll(TreeNodeCollection nodes)
    {
        foreach (TreeNode n in nodes)
        {
            n.Expand();
            if (n.Nodes.Count > 0) ExpandAll(n.Nodes);
        }
    }

    [STAThread]
    private static void Main()
    {
        Console.WriteLine("== 过滤+清除 大体积合成树（防回归） ==");

        var form = new Form { Width = 400, Height = 500, Opacity = 0, ShowInTaskbar = false };
        var tree = new TreeView { Dock = DockStyle.Fill };
        form.Controls.Add(tree);
        form.Show();

        _map = new Dictionary<string, TreeNode>();
        var sw = Stopwatch.StartNew();
        tree.BeginUpdate();
        try
        {
            Build(tree, 10, 10, 4);          // 约 11.1 万节点
            ExpandAll(tree.Nodes);            // 全展开，与过滤目标一致
        }
        finally { tree.EndUpdate(); }
        int buildMs = (int)sw.ElapsedMilliseconds;
        Console.WriteLine("build=" + buildMs + " ms");

        string beforeRoot = tree.Nodes[0].Text;
        int beforeRoots = tree.Nodes.Count;

        // 下限 0：所有叶子都匹配，无删除、文本不变 —— 旧实现会无谓地做全量原生改 Text/Expand
        FilterState st = null;
        sw.Restart();
        st = TreeFilter.ApplyFilter(tree, new FilterOptions { DurationMinHours = 0.0 }, st);
        int applyMs = (int)sw.ElapsedMilliseconds;

        sw.Restart();
        TreeFilter.ClearFilter(tree, st);
        int clearMs = (int)sw.ElapsedMilliseconds;

        Console.WriteLine("apply(min0)=" + applyMs + " ms, clear=" + clearMs + " ms");

        Assert(tree.Nodes.Count == beforeRoots, "清除后根数量还原, got " + tree.Nodes.Count);
        Assert(tree.Nodes[0].Text == beforeRoot, "清除后根文本还原, got " + tree.Nodes[0].Text);

        // 阈值远大于修复后的实际耗时（约 2s），旧实现在此规模需数十秒到数分钟
        Assert(applyMs + clearMs < 15000, "过滤+清除耗时 " + (applyMs + clearMs) + " ms (>= 15000，疑似无谓全量原生操作)");

        form.Close();
        Console.WriteLine();
        Console.WriteLine("Total: Passed " + Passed + ", Failed " + Failed);
        Environment.ExitCode = Failed > 0 ? 1 : 0;
    }
}
