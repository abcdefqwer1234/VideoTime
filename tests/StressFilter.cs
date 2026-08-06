using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using VideoTime;

// 诊断：ApplyFilter 各子阶段耗时（内联复刻，避免修改生产代码）
internal static class StressFilter
{
    private static void ExpandToDepth(TreeNode node, int depth)
    {
        node.Expand();
        if (depth >= 1) return;
        foreach (TreeNode child in node.Nodes)
            ExpandToDepth(child, depth + 1);
    }

    private static void BuildSynthetic(TreeView tree, int topCount, int subCount, int depth)
    {
        var result = new List<FolderResult>();
        var stack = new List<(string path, int d)>();
        for (int i = 0; i < topCount; i++) stack.Add(("R" + i, 0));
        while (stack.Count > 0)
        {
            var (path, d) = stack[stack.Count - 1];
            stack.RemoveAt(stack.Count - 1);
            int videos = 0;
            double secs = 0;
            if (d < depth)
            {
                for (int j = 0; j < subCount; j++)
                    stack.Add((path + "\\D" + j + "_" + d, d + 1));
            }
            else
            {
                videos = (path.GetHashCode() & 1) == 0 ? 2 : 0;
                if (videos > 0) secs = 1800 + (path.Length * 37 % 7000);
            }
            result.Add(new FolderResult { FolderPath = path, TotalSeconds = secs, FileCount = videos });
        }

        tree.BeginUpdate();
        try
        {
            var nodeMap = new Dictionary<string, TreeNode>();
            foreach (var r in result)
            {
                string folderName = Path.GetFileName(r.FolderPath);
                if (string.IsNullOrEmpty(folderName)) folderName = r.FolderPath;
                TreeNode node = new TreeNode($"{folderName}  {VideoScanner.Format(r.TotalSeconds)}  [视频{r.FileCount}]");
                string parentDir = Path.GetDirectoryName(r.FolderPath);
                if (parentDir != null && nodeMap.TryGetValue(parentDir, out TreeNode parent))
                    parent.Nodes.Add(node);
                else
                    tree.Nodes.Add(node);
                nodeMap[r.FolderPath] = node;
            }
            foreach (TreeNode node in tree.Nodes)
                ExpandToDepth(node, 0);
        }
        finally { tree.EndUpdate(); }
    }

    private static void PhaseTime(string label, Action a)
    {
        var sw = Stopwatch.StartNew();
        a();
        sw.Stop();
        Console.WriteLine("    " + label + " = " + sw.ElapsedMilliseconds + " ms");
    }

    private static void ApplyInstrumented(TreeView tree, FilterOptions opt)
    {
        var state = new FilterState();
        PhaseTime("SaveExpandState", () => SaveExpand(tree.Nodes, state.ExpandState));
        PhaseTime("SaveOriginalTexts", () => SaveTexts(tree.Nodes, state.OriginalTexts));

        var visible = new HashSet<TreeNode>();
        PhaseTime("CollectVisibleNodes", () => CollectVisible(tree.Nodes, opt, visible, false));

        tree.BeginUpdate();
        try
        {
            PhaseTime("RecomputeBranchTexts", () => Recompute(tree.Nodes, opt, visible));
            var toRemove = new List<TreeNode>();
            PhaseTime("FindNonVisibleNodes", () => FindNon(tree.Nodes, visible, toRemove));
            PhaseTime("Record indices", () =>
            {
                foreach (TreeNode n in toRemove)
                {
                    TreeNode parent = n.Parent;
                    int index = parent != null ? parent.Nodes.IndexOf(n) : tree.Nodes.IndexOf(n);
                    state.AddRemoved(n, parent, index);
                }
            });
            PhaseTime("Remove nodes", () =>
            {
                foreach (TreeNode n in toRemove)
                {
                    if (n.Parent != null) n.Parent.Nodes.Remove(n);
                    else tree.Nodes.Remove(n);
                }
            });
            PhaseTime("ExpandMatchingAncestors", () =>
            {
                foreach (TreeNode root in tree.Nodes)
                    ExpandMatching(root);
            });
        }
        finally { tree.EndUpdate(); }

        // clear phases
        PhaseTime("[clear] Reinsert", () =>
        {
            for (int i = 0; i < state.Removed.Count; i++)
            {
                var r = state.Removed[i];
                TreeNodeCollection coll = r.Parent != null ? r.Parent.Nodes : tree.Nodes;
                int idx = Math.Min(r.Index, coll.Count);
                coll.Insert(idx, r.Node);
            }
        });
        PhaseTime("[clear] RestoreOriginalTexts", () => RestoreTexts(tree.Nodes, state.OriginalTexts));
        PhaseTime("[clear] RestoreExpandState", () => RestoreExpand(tree.Nodes, state.ExpandState));
    }

    // ---- inline copies of TreeFilter private helpers ----
    private static void SaveExpand(TreeNodeCollection nodes, Dictionary<TreeNode, bool> state)
    {
        foreach (TreeNode node in nodes)
        {
            state[node] = node.IsExpanded;
            if (node.Nodes.Count > 0) SaveExpand(node.Nodes, state);
        }
    }
    private static void SaveTexts(TreeNodeCollection nodes, Dictionary<TreeNode, string> texts)
    {
        foreach (TreeNode node in nodes)
        {
            texts[node] = node.Text;
            if (node.Nodes.Count > 0) SaveTexts(node.Nodes, texts);
        }
    }
    private static bool CollectVisible(TreeNodeCollection nodes, FilterOptions opt, HashSet<TreeNode> visible, bool inherited)
    {
        bool any = false;
        bool durationActive = opt.DurationMinHours.HasValue || opt.DurationMaxHours.HasValue;
        bool hasName = !string.IsNullOrEmpty(opt.Name);
        foreach (TreeNode node in nodes)
        {
            string name = TreeFilter.ExtractName(node.Text);
            double seconds = TreeFilter.ExtractSeconds(node.Text);
            int count = TreeFilter.ExtractCount(node.Text);
            bool isLeaf = node.Nodes.Count == 0;
            bool nameOk = !hasName;
            bool childVisible = false;
            if (node.Nodes.Count > 0)
                childVisible = CollectVisible(node.Nodes, opt, visible, nameOk);
            bool selfMatch;
            if (isLeaf)
                selfMatch = nameOk && DurationCount(seconds, count, opt);
            else if (durationActive)
                selfMatch = count > 0;
            else
                selfMatch = nameOk;
            if (selfMatch || childVisible) { visible.Add(node); any = true; }
        }
        return any;
    }
    private static bool DurationCount(double seconds, int count, FilterOptions opt)
    {
        if ((opt.DurationMinHours.HasValue || opt.DurationMaxHours.HasValue) && count == 0) return false;
        double hours = seconds / 3600.0;
        if (opt.DurationMinHours.HasValue && hours < opt.DurationMinHours.Value) return false;
        if (opt.DurationMaxHours.HasValue && hours > opt.DurationMaxHours.Value) return false;
        if (opt.CountMin.HasValue && count < opt.CountMin.Value) return false;
        if (opt.CountMax.HasValue && count > opt.CountMax.Value) return false;
        return true;
    }
    private static void Recompute(TreeNodeCollection nodes, FilterOptions opt, HashSet<TreeNode> visible)
    {
        foreach (TreeNode node in nodes)
        {
            if (node.Nodes.Count == 0) continue;
            Recompute(node.Nodes, opt, visible);
            double sumSeconds = 0; int sumCount = 0;
            SumVisible(node, visible, ref sumSeconds, ref sumCount);
            string name = TreeFilter.ExtractName(node.Text);
            node.Text = name + "  " + VideoScanner.Format(sumSeconds) + "  [视频" + sumCount + "]";
        }
    }
    private static void SumVisible(TreeNode node, HashSet<TreeNode> visible, ref double s, ref int c)
    {
        foreach (TreeNode child in node.Nodes)
        {
            if (child.Nodes.Count == 0)
            {
                if (visible.Contains(child)) { s += TreeFilter.ExtractSeconds(child.Text); c += TreeFilter.ExtractCount(child.Text); }
            }
            else SumVisible(child, visible, ref s, ref c);
        }
    }
    private static void FindNon(TreeNodeCollection nodes, HashSet<TreeNode> visible, List<TreeNode> toRemove)
    {
        foreach (TreeNode node in nodes)
        {
            if (!visible.Contains(node)) toRemove.Add(node);
            else if (node.Nodes.Count > 0) FindNon(node.Nodes, visible, toRemove);
        }
    }
    private static void ExpandMatching(TreeNode node)
    {
        if (node.Nodes.Count > 0)
        {
            node.Expand();
            foreach (TreeNode child in node.Nodes)
                ExpandMatching(child);
        }
    }
    private static void RestoreTexts(TreeNodeCollection nodes, Dictionary<TreeNode, string> texts)
    {
        foreach (TreeNode node in nodes)
        {
            if (texts.TryGetValue(node, out string text)) node.Text = text;
            if (node.Nodes.Count > 0) RestoreTexts(node.Nodes, texts);
        }
    }
    private static void RestoreExpand(TreeNodeCollection nodes, Dictionary<TreeNode, bool> state)
    {
        foreach (TreeNode node in nodes)
        {
            if (state.TryGetValue(node, out bool expanded) && expanded) node.Expand();
            else node.Collapse();
            if (node.Nodes.Count > 0) RestoreExpand(node.Nodes, state);
        }
    }

    [STAThread]
    private static void Main()
    {
        Console.WriteLine("== 111k 结构（10 扇出 × 深度 5，≈111 万节点）==");
        var form = new Form { Width = 400, Height = 500, Opacity = 0, ShowInTaskbar = false };
        var tree = new TreeView { Dock = DockStyle.Fill };
        form.Controls.Add(tree);
        form.Show();
        var sw = Stopwatch.StartNew();
        BuildSynthetic(tree, 10, 10, 5);
        sw.Stop();
        Console.WriteLine("build = " + sw.ElapsedMilliseconds + " ms");
        Console.WriteLine("tree nodes = " + CountAll(tree.Nodes));
        ApplyInstrumented(tree, new FilterOptions { DurationMinHours = 0.0 });
        form.Close();
    }

    private static int CountAll(TreeNodeCollection nodes)
    {
        int n = 0;
        foreach (TreeNode node in nodes)
        {
            n++;
            if (node.Nodes.Count > 0) n += CountAll(node.Nodes);
        }
        return n;
    }
}
