using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace VideoTime
{
    public class FilterOptions
    {
        public string Name { get; set; } = "";
        public double? DurationMinHours { get; set; }
        public double? DurationMaxHours { get; set; }
        public int? CountMin { get; set; }
        public int? CountMax { get; set; }

        public bool IsActive =>
            !string.IsNullOrEmpty(Name) ||
            DurationMinHours.HasValue || DurationMaxHours.HasValue ||
            CountMin.HasValue || CountMax.HasValue;
    }

    public class FilterState
    {
        public Dictionary<TreeNode, string> OriginalTexts { get; } = new Dictionary<TreeNode, string>();

        private readonly List<RemovedNode> _removed = new List<RemovedNode>();
        public IReadOnlyList<RemovedNode> Removed => _removed;

        /// <summary>过滤过程中被展开的枝干（原为折叠态）。清除时仅需反向折叠这些节点。</summary>
        public List<TreeNode> ExpandedByFilter { get; } = new List<TreeNode>();

        /// <summary>被移除子树根节点移除前的展开状态，清除后按原状恢复。</summary>
        public Dictionary<TreeNode, bool> RemovedExpanded { get; } = new Dictionary<TreeNode, bool>();

        public class RemovedNode
        {
            public TreeNode Node;
            public TreeNode Parent;
            public int Index;
        }

        public void AddRemoved(TreeNode node, TreeNode parent, int index)
        {
            _removed.Add(new RemovedNode { Node = node, Parent = parent, Index = index });
        }

        public void ClearRemoved() => _removed.Clear();
    }

    public static class TreeFilter
    {
        public static bool Matches(string folderName, double totalSeconds, int fileCount, FilterOptions opt)
        {
            return Matches(folderName, totalSeconds, fileCount, true, opt);
        }

        public static bool Matches(string folderName, double totalSeconds, int fileCount, bool isLeaf, FilterOptions opt)
        {
            if (opt == null || !opt.IsActive) return true;

            if (!string.IsNullOrEmpty(opt.Name))
            {
                if (folderName.IndexOf(opt.Name, StringComparison.OrdinalIgnoreCase) < 0)
                    return false;
            }

            // 时长/数量条件只作用于叶子节点，枝干仅按名称与子节点过滤
            if (!isLeaf) return true;

            return MatchesDurationCount(totalSeconds, fileCount, opt);
        }

        private static bool MatchesDurationCount(double totalSeconds, int fileCount, FilterOptions opt)
        {
            // 使用时长过滤时，无论上下限，视频数为零的文件夹一律过滤掉
            if ((opt.DurationMinHours.HasValue || opt.DurationMaxHours.HasValue) && fileCount == 0)
                return false;

            double hours = totalSeconds / 3600.0;
            if (opt.DurationMinHours.HasValue && hours < opt.DurationMinHours.Value)
                return false;
            if (opt.DurationMaxHours.HasValue && hours > opt.DurationMaxHours.Value)
                return false;

            if (opt.CountMin.HasValue && fileCount < opt.CountMin.Value)
                return false;
            if (opt.CountMax.HasValue && fileCount > opt.CountMax.Value)
                return false;

            return true;
        }

        public static FilterState ApplyFilter(TreeView tree, FilterOptions opt, FilterState state)
        {
            if (opt == null || !opt.IsActive)
            {
                ClearFilter(tree, state);
                return null;
            }

            // 再次过滤前先完整还原（节点、原文本、展开状态），再基于原始树重新过滤
            if (state != null)
                ClearFilter(tree, state);

            state = new FilterState();
            SaveOriginalTexts(tree.Nodes, state.OriginalTexts);

            HashSet<TreeNode> visible = new HashSet<TreeNode>();
            CollectVisibleNodes(tree.Nodes, opt, visible, false);

            // 树挂载在可见窗体上时，逐个改 Text/Remove/Expand 会触发海量重排重绘，
            // 必须用 BeginUpdate 挂起布局，一次性应用后再恢复。
            tree.BeginUpdate();
            try
            {
                RecomputeBranchTexts(tree.Nodes, opt, visible);

                List<TreeNode> toRemove = new List<TreeNode>();
                FindNonVisibleNodes(tree.Nodes, visible, toRemove);

                foreach (TreeNode n in toRemove)
                {
                    TreeNode parent = n.Parent;
                    int index = parent != null ? parent.Nodes.IndexOf(n) : tree.Nodes.IndexOf(n);
                    state.AddRemoved(n, parent, index);
                    state.RemovedExpanded[n] = n.IsExpanded;
                }
                foreach (TreeNode n in toRemove)
                {
                    if (n.Parent != null) n.Parent.Nodes.Remove(n);
                    else tree.Nodes.Remove(n);
                }

                foreach (TreeNode root in tree.Nodes)
                    ExpandMatchingAncestors(root, state.ExpandedByFilter);
            }
            finally
            {
                tree.EndUpdate();
            }

            return state;
        }

        public static void ClearFilter(TreeView tree, FilterState state)
        {
            if (state == null) return;

            tree.BeginUpdate();
            try
            {
                for (int i = 0; i < state.Removed.Count; i++)
                {
                    var r = state.Removed[i];
                    TreeNodeCollection coll = r.Parent != null ? r.Parent.Nodes : tree.Nodes;
                    int idx = Math.Min(r.Index, coll.Count);
                    coll.Insert(idx, r.Node);
                    // 还原被移除子树根节点原有的展开状态
                    if (state.RemovedExpanded.TryGetValue(r.Node, out bool wasExpanded) && wasExpanded)
                        r.Node.Expand();
                }
                state.ClearRemoved();
                state.RemovedExpanded.Clear();

                RestoreOriginalTexts(tree.Nodes, state.OriginalTexts);
                state.OriginalTexts.Clear();

                // 仅反向折叠过滤过程中新展开的枝干，不再遍历整棵树的展开状态
                for (int i = 0; i < state.ExpandedByFilter.Count; i++)
                {
                    TreeNode n = state.ExpandedByFilter[i];
                    if (n.IsExpanded)
                        n.Collapse();
                }
                state.ExpandedByFilter.Clear();
            }
            finally
            {
                tree.EndUpdate();
            }
        }

        public static List<int> FindSubstringPositions(string text, string sub)
        {
            var positions = new List<int>();
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(sub))
                return positions;

            int idx = 0;
            while (idx <= text.Length - sub.Length)
            {
                int found = text.IndexOf(sub, idx, StringComparison.OrdinalIgnoreCase);
                if (found < 0) break;
                positions.Add(found);
                idx = found + 1;
            }
            return positions;
        }

        /// <summary>累计过滤后树中所有可见叶子节点的合计（含名称命中而继承选中的子叶）。</summary>
        public static void CollectFilteredStats(TreeNodeCollection nodes,
            ref double totalSeconds, ref int totalCount)
        {
            foreach (TreeNode node in nodes)
            {
                if (node.Nodes.Count > 0)
                {
                    CollectFilteredStats(node.Nodes, ref totalSeconds, ref totalCount);
                    continue;
                }

                totalSeconds += ExtractSeconds(node.Text);
                totalCount += ExtractCount(node.Text);
            }
        }

        private static bool CollectVisibleNodes(TreeNodeCollection nodes, FilterOptions opt,
            HashSet<TreeNode> visible, bool inheritedNameMatch)
        {
            bool anyVisibleChild = false;
            bool durationActive = opt.DurationMinHours.HasValue || opt.DurationMaxHours.HasValue;
            bool hasNameFilter = !string.IsNullOrEmpty(opt.Name);

            foreach (TreeNode node in nodes)
            {
                string name = ExtractName(node.Text);
                double seconds = ExtractSeconds(node.Text);
                int count = ExtractCount(node.Text);
                bool isLeaf = node.Nodes.Count == 0;

                // 名称命中（含来自祖先的继承）只视为通过名称条件；时长/数量仍须正常过滤
                bool ownNameMatch = hasNameFilter &&
                    name.IndexOf(opt.Name, StringComparison.OrdinalIgnoreCase) >= 0;
                bool nameMatched = hasNameFilter && (inheritedNameMatch || ownNameMatch);
                bool nameOk = !hasNameFilter || nameMatched;

                bool childVisible = false;
                if (node.Nodes.Count > 0)
                    childVisible = CollectVisibleNodes(node.Nodes, opt, visible, nameMatched);

                bool selfMatch;
                if (isLeaf)
                    selfMatch = nameOk && MatchesDurationCount(seconds, count, opt);
                else if (durationActive)
                    selfMatch = count > 0;
                else
                    selfMatch = nameOk;

                if (selfMatch || childVisible)
                {
                    visible.Add(node);
                    anyVisibleChild = true;
                }
            }

            return anyVisibleChild;
        }

        /// <summary>重算枝干节点文本：其下所有可见叶子的时长/数量之和。名称命中的枝干重算后即其真实内容。</summary>
        private static void RecomputeBranchTexts(TreeNodeCollection nodes, FilterOptions opt,
            HashSet<TreeNode> visible)
        {
            foreach (TreeNode node in nodes)
            {
                if (node.Nodes.Count == 0) continue;

                RecomputeBranchTexts(node.Nodes, opt, visible);

                double sumSeconds = 0;
                int sumCount = 0;
                SumVisibleLeaves(node, visible, ref sumSeconds, ref sumCount);

                string name = ExtractName(node.Text);
                string newText = name + "  " + VideoScanner.Format(sumSeconds) + "  [视频" + sumCount + "]";
                // 值未变化时跳过，避免对原生 TreeView 发送无谓的 TVM_SETITEM（大数据量下开销巨大）
                if (node.Text != newText)
                    node.Text = newText;
            }
        }

        private static void SumVisibleLeaves(TreeNode node, HashSet<TreeNode> visible,
            ref double sumSeconds, ref int sumCount)
        {
            foreach (TreeNode child in node.Nodes)
            {
                if (child.Nodes.Count == 0)
                {
                    if (visible.Contains(child))
                    {
                        sumSeconds += ExtractSeconds(child.Text);
                        sumCount += ExtractCount(child.Text);
                    }
                }
                else
                {
                    SumVisibleLeaves(child, visible, ref sumSeconds, ref sumCount);
                }
            }
        }

        private static void FindNonVisibleNodes(TreeNodeCollection nodes, HashSet<TreeNode> visible, List<TreeNode> toRemove)
        {
            foreach (TreeNode node in nodes)
            {
                if (!visible.Contains(node))
                    toRemove.Add(node);
                else if (node.Nodes.Count > 0)
                    FindNonVisibleNodes(node.Nodes, visible, toRemove);
            }
        }

        private static void ExpandMatchingAncestors(TreeNode node, List<TreeNode> expandedByFilter)
        {
            if (node.Nodes.Count > 0)
            {
                // 已是展开态则跳过，避免大数据量下对全部枝干重复发送原生 Expand
                if (!node.IsExpanded)
                {
                    node.Expand();
                    expandedByFilter.Add(node);
                }
                foreach (TreeNode child in node.Nodes)
                    ExpandMatchingAncestors(child, expandedByFilter);
            }
        }

        private static void SaveOriginalTexts(TreeNodeCollection nodes, Dictionary<TreeNode, string> texts)
        {
            foreach (TreeNode node in nodes)
            {
                texts[node] = node.Text;
                if (node.Nodes.Count > 0)
                    SaveOriginalTexts(node.Nodes, texts);
            }
        }

        private static void RestoreOriginalTexts(TreeNodeCollection nodes, Dictionary<TreeNode, string> texts)
        {
            foreach (TreeNode node in nodes)
            {
                if (texts.TryGetValue(node, out string text) && node.Text != text)
                    node.Text = text;
                if (node.Nodes.Count > 0)
                    RestoreOriginalTexts(node.Nodes, texts);
            }
        }

        public static string ExtractName(string nodeText)
        {
            if (string.IsNullOrEmpty(nodeText)) return "";
            int marker = nodeText.LastIndexOf("  [视频", StringComparison.Ordinal);
            if (marker < 0) return nodeText;
            int sep = nodeText.LastIndexOf("  ", marker - 1);
            if (sep < 0) return nodeText;
            return nodeText.Substring(0, sep).TrimEnd();
        }

        public static double ExtractSeconds(string nodeText)
        {
            string fmt = ExtractFormattedTime(nodeText);
            return ParseFormattedTime(fmt);
        }

        public static int ExtractCount(string nodeText)
        {
            if (string.IsNullOrEmpty(nodeText)) return 0;
            int bracketStart = nodeText.LastIndexOf("[视频");
            if (bracketStart < 0) return 0;
            int numStart = bracketStart + 3;
            int bracketEnd = nodeText.IndexOf(']', numStart);
            if (bracketEnd < 0) return 0;
            string numStr = nodeText.Substring(numStart, bracketEnd - numStart);
            if (int.TryParse(numStr, out int val)) return val;
            return 0;
        }

        private static string ExtractFormattedTime(string nodeText)
        {
            if (string.IsNullOrEmpty(nodeText)) return "";
            int marker = nodeText.LastIndexOf("  [视频", StringComparison.Ordinal);
            if (marker < 0) return "";
            int sep = nodeText.LastIndexOf("  ", marker - 1);
            if (sep < 0) return "";
            return nodeText.Substring(sep + 2, marker - sep - 2);
        }

        private static double ParseFormattedTime(string fmt)
        {
            if (string.IsNullOrEmpty(fmt)) return 0;
            long totalSec = 0;
            int hIdx = fmt.IndexOf('时');
            int mIdx = fmt.IndexOf('分');
            int sIdx = fmt.IndexOf('秒');

            if (hIdx > 0 && long.TryParse(fmt.Substring(0, hIdx), out long h))
                totalSec += h * 3600;
            if (mIdx > hIdx && long.TryParse(fmt.Substring(hIdx + 1, mIdx - hIdx - 1), out long m))
                totalSec += m * 60;
            if (sIdx > mIdx && long.TryParse(fmt.Substring(mIdx + 1, sIdx - mIdx - 1), out long s))
                totalSec += s;

            return totalSec;
        }
    }
}
