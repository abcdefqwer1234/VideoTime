using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using VideoTime;

public class MultiRootFilterTest
{
    public static int Passed = 0;
    public static int Failed = 0;

    public static void Assert(bool condition, string msg)
    {
        if (condition)
        {
            Passed++;
            Console.WriteLine("  PASS: " + msg);
        }
        else
        {
            Failed++;
            Console.WriteLine("  FAIL: " + msg);
        }
    }

    public static void TestTreeFilterMatches()
    {
        Console.WriteLine("== TreeFilter.Matches ==");

        var opt = new FilterOptions { Name = "test" };
        Assert(TreeFilter.Matches("test_folder", 3600, 10, opt), "Name match: test_folder contains 'test'");
        Assert(!TreeFilter.Matches("other_folder", 3600, 10, opt), "Name no match: other_folder doesn't contain 'test'");
        Assert(TreeFilter.Matches("Test_Folder", 3600, 10, opt), "Name case insensitive: Test_Folder matches 'test'");

        var optDur = new FilterOptions { DurationMinHours = 1.0, DurationMaxHours = 5.0 };
        Assert(TreeFilter.Matches("a", 7200, 1, optDur), "Duration 2h in range [1,5]");
        Assert(!TreeFilter.Matches("a", 1800, 1, optDur), "Duration 0.5h below min 1");
        Assert(!TreeFilter.Matches("a", 21600, 1, optDur), "Duration 6h above max 5");
        Assert(TreeFilter.Matches("a", 3600, 1, optDur), "Duration 1h at min boundary");
        Assert(TreeFilter.Matches("a", 18000, 1, optDur), "Duration 5h at max boundary");

        var optCount = new FilterOptions { CountMin = 5, CountMax = 20 };
        Assert(TreeFilter.Matches("a", 100, 10, optCount), "Count 10 in range [5,20]");
        Assert(!TreeFilter.Matches("a", 100, 3, optCount), "Count 3 below min 5");
        Assert(!TreeFilter.Matches("a", 100, 25, optCount), "Count 25 above max 20");

        var optAll = new FilterOptions { Name = "vid", DurationMinHours = 0.5, CountMin = 3 };
        Assert(TreeFilter.Matches("video_dir", 3600, 5, optAll), "Combined: name+dur+count all match");
        Assert(!TreeFilter.Matches("video_dir", 100, 5, optAll), "Combined: dur too low");
        Assert(!TreeFilter.Matches("other", 3600, 5, optAll), "Combined: name doesn't match");

        var optEmpty = new FilterOptions();
        Assert(TreeFilter.Matches("anything", 0, 0, optEmpty), "Empty filter matches everything");
        Assert(TreeFilter.Matches("anything", 0, 0, null), "Null filter matches everything");

        Console.WriteLine("== TreeFilter.Matches 时长过滤 + 零视频叶子 ==");
        var optDurOnly = new FilterOptions { DurationMinHours = 1.0 };
        Assert(!TreeFilter.Matches("a", 3600, 0, true, optDurOnly), "时长过滤: 零视频叶子被过滤 (0 视频)");
        Assert(!TreeFilter.Matches("a", 0, 0, true, optDurOnly), "时长过滤: 零视频叶子被过滤 (0 时长)");
        Assert(TreeFilter.Matches("a", 3600, 1, true, optDurOnly), "时长过滤: 有视频叶子保留");

        var optDurMaxOnly = new FilterOptions { DurationMaxHours = 10.0 };
        Assert(!TreeFilter.Matches("a", 3600, 0, true, optDurMaxOnly), "仅时长上限: 零视频叶子仍被过滤");

        var optCountNoDur = new FilterOptions { CountMax = 100 };
        Assert(TreeFilter.Matches("a", 3600, 0, true, optCountNoDur), "无时长过滤时零视频叶子不受零视频门控影响");

        Console.WriteLine("== TreeFilter.Matches 枝干忽略时长/数量 ==");
        var optDurBranch = new FilterOptions { DurationMinHours = 5.0 };
        Assert(TreeFilter.Matches("branch", 1800, 1, false, optDurBranch), "枝干忽略时长下限 (0.5h < 5h 仍匹配)");
        Assert(TreeFilter.Matches("branch", 0, 0, false, optDurBranch), "枝干忽略时长过滤的零视频门控");
        var optCountBranch = new FilterOptions { CountMin = 50 };
        Assert(TreeFilter.Matches("branch", 100, 1, false, optCountBranch), "枝干忽略数量下限 (1 < 50 仍匹配)");
        Assert(!TreeFilter.Matches("branch", 100, 1, false, new FilterOptions { Name = "zzz" }), "枝干仍受名称条件约束");
    }

    public static void TestFilterTreeApply()
    {
        Console.WriteLine("== TreeFilter.ApplyFilter 树级行为 ==");

        var tree = new System.Windows.Forms.TreeView();

        var root = new System.Windows.Forms.TreeNode("Root  5时0分0秒  [视频40]");
        var a = new System.Windows.Forms.TreeNode("A  0时30分0秒  [视频2]");
        var b = new System.Windows.Forms.TreeNode("B  3时0分0秒  [视频10]");
        var c = new System.Windows.Forms.TreeNode("C  6时0分0秒  [视频30]");
        var c1 = new System.Windows.Forms.TreeNode("C1  2时0分0秒  [视频5]");
        var c2 = new System.Windows.Forms.TreeNode("C2  4时0分0秒  [视频25]");
        c.Nodes.Add(c1);
        c.Nodes.Add(c2);
        root.Nodes.Add(a);
        root.Nodes.Add(b);
        root.Nodes.Add(c);
        tree.Nodes.Add(root);

        // 需求2+需求3: 时长过滤只删叶子; 枝干保留并重算
        FilterState st = TreeFilter.ApplyFilter(tree, new FilterOptions { DurationMinHours = 2.0 }, null);

        Assert(tree.Nodes.Count == 1, "过滤后根仍保留");
        Assert(a.Parent == null, "叶子 A (0.5h<2h) 被移除");
        Assert(b.Parent == root, "叶子 B (3h) 保留");
        Assert(c.Parent == root, "枝干 C 保留 (不过滤母文件夹)");
        Assert(c1.Parent == c && c2.Parent == c, "C 的合格子叶子保留");
        Assert(root.Text == "Root  9时0分0秒  [视频40]", "根重算 = B3h+C1 2h+C2 4h, got " + root.Text);
        Assert(c.Text == "C  6时0分0秒  [视频30]", "枝干 C 重算 = C1 2h+C2 4h, got " + c.Text);
        Assert(b.Text == "B  3时0分0秒  [视频10]", "叶子 B 文本不变");

        // 需求1: 时长过滤 + 零视频叶子被移除
        tree.Nodes.Clear();
        var root2 = new System.Windows.Forms.TreeNode("Root2  1时0分0秒  [视频1]");
        var zero = new System.Windows.Forms.TreeNode("Zero  0时0分0秒  [视频0]");
        root2.Nodes.Add(zero);
        tree.Nodes.Add(root2);
        TreeFilter.ApplyFilter(tree, new FilterOptions { DurationMinHours = 0.0 }, null);
        Assert(zero.Parent == null, "时长过滤下零视频叶子被移除");
        Assert(root2.Text == "Root2  0时0分0秒  [视频0]", "零视频被移除后根重算为 0");

        // 需求5: 换参数再次过滤，先还原再按叶子过滤
        tree.Nodes.Clear();
        var root3 = new System.Windows.Forms.TreeNode("Root3  6时0分0秒  [视频40]");
        var a3 = new System.Windows.Forms.TreeNode("A3  0时30分0秒  [视频2]");
        var b3 = new System.Windows.Forms.TreeNode("B3  3时0分0秒  [视频10]");
        var c3 = new System.Windows.Forms.TreeNode("C3  6时0分0秒  [视频30]");
        var c31 = new System.Windows.Forms.TreeNode("C31  2时0分0秒  [视频5]");
        var c32 = new System.Windows.Forms.TreeNode("C32  4时0分0秒  [视频25]");
        c3.Nodes.Add(c31);
        c3.Nodes.Add(c32);
        root3.Nodes.Add(a3);
        root3.Nodes.Add(b3);
        root3.Nodes.Add(c3);
        tree.Nodes.Add(root3);

        FilterState st2 = TreeFilter.ApplyFilter(tree, new FilterOptions { DurationMinHours = 2.0 }, null);
        Assert(a3.Parent == null && b3.Parent == root3 && c3.Parent == root3, "首次过滤: 仅 A3 被移除");

        // 换参数再次过滤 (state != null)
        st2 = TreeFilter.ApplyFilter(tree, new FilterOptions { CountMin = 20 }, st2);
        Assert(a3.Parent == null && b3.Parent == null, "再次过滤: 先还原后 B3/A3 被移除 (count<20)");
        Assert(c3.Parent == root3 && c32.Parent == c3, "再次过滤: C3/C32 保留 (count=25)");
        Assert(c31.Parent == null, "再次过滤: C31 (count=5) 被移除");
        Assert(c3.Text == "C3  4时0分0秒  [视频25]", "再次过滤后 C3 重算 = C32 4h, got " + c3.Text);
        Assert(root3.Text == "Root3  4时0分0秒  [视频25]", "再次过滤后根重算 = C32 4h, got " + root3.Text);

        // 需求4(树级): 清除后完整还原文本与结构
        TreeFilter.ClearFilter(tree, st2);
        Assert(root3.Nodes.Count == 3, "清除后 3 个子节点还原, got " + root3.Nodes.Count);
        Assert(a3.Parent == root3 && b3.Parent == root3 && c3.Parent == root3, "清除后 A3/B3/C3 全部还原");
        Assert(c3.Nodes.Count == 2 && c31.Parent == c3 && c32.Parent == c3, "清除后 C3 的子节点还原");
        Assert(root3.Text == "Root3  6时0分0秒  [视频40]", "清除后根文本还原, got " + root3.Text);
        Assert(a3.Text == "A3  0时30分0秒  [视频2]", "清除后叶子 A3 文本还原");
        Assert(c3.Text == "C3  6时0分0秒  [视频30]", "清除后枝干 C3 文本还原");

        // 时间筛选: 枝干只要有视频就保留（子叶全被滤除也显示 0），真正无视频的枝干才移除
        tree.Nodes.Clear();
        var root4 = new System.Windows.Forms.TreeNode("Root4  3时0分0秒  [视频10]");
        var keepBranch = new System.Windows.Forms.TreeNode("KeepBranch  1时0分0秒  [视频5]");
        var leaf5 = new System.Windows.Forms.TreeNode("Leaf5  1时0分0秒  [视频5]");
        var emptyBranch = new System.Windows.Forms.TreeNode("EmptyBranch  0时0分0秒  [视频0]");
        var leaf0 = new System.Windows.Forms.TreeNode("Leaf0  0时0分0秒  [视频0]");
        keepBranch.Nodes.Add(leaf5);
        emptyBranch.Nodes.Add(leaf0);
        root4.Nodes.Add(keepBranch);
        root4.Nodes.Add(emptyBranch);
        tree.Nodes.Add(root4);
        TreeFilter.ApplyFilter(tree, new FilterOptions { DurationMinHours = 5.0 }, null);
        Assert(tree.Nodes.Count == 1 && tree.Nodes[0] == root4, "时间筛选: 根有视频则保留");
        Assert(root4.Nodes.Count == 1, "时间筛选: 根下只剩有视频的 KeepBranch, got " + root4.Nodes.Count);
        Assert(keepBranch.Parent == root4, "时间筛选: 有视频的枝干保留（子叶全被滤除）");
        Assert(keepBranch.Text == "KeepBranch  0时0分0秒  [视频0]", "时间筛选: 子叶全滤除后枝干显示 0 视频, got " + keepBranch.Text);
        Assert(leaf5.Parent == null, "时间筛选: 未达时长的子叶被滤除");
        Assert(emptyBranch.Parent == null, "时间筛选: 真正无视频的枝干被移除");
        Assert(root4.Text == "Root4  0时0分0秒  [视频0]", "时间筛选: 根重算为 0, got " + root4.Text);

        // 时间筛选 + 名称不匹配: 有视频的枝干仍保留（仅无视频时才被滤除）
        tree.Nodes.Clear();
        var root5 = new System.Windows.Forms.TreeNode("Root5  1时0分0秒  [视频5]");
        var noMatchBranch = new System.Windows.Forms.TreeNode("NoMatchBranch  1时0分0秒  [视频5]");
        var nmLeaf = new System.Windows.Forms.TreeNode("NMLeaf  1时0分0秒  [视频5]");
        noMatchBranch.Nodes.Add(nmLeaf);
        root5.Nodes.Add(noMatchBranch);
        tree.Nodes.Add(root5);
        TreeFilter.ApplyFilter(tree, new FilterOptions { Name = "zzz", DurationMinHours = 5.0 }, null);
        Assert(noMatchBranch.Parent == root5, "时间筛选: 名称不匹配但有视频的枝干仍保留");
        Assert(nmLeaf.Parent == null, "时间筛选: 其未达时长的子叶被滤除");
        Assert(root5.Text == "Root5  0时0分0秒  [视频0]", "时间筛选: 根重算为 0, got " + root5.Text);

        // 名称命中: 整个子树都被选中（纯名称过滤）
        tree.Nodes.Clear();
        var root6 = new System.Windows.Forms.TreeNode("Root6  6时0分0秒  [视频30]");
        var math = new System.Windows.Forms.TreeNode("Math  6时0分0秒  [视频30]");
        var m1 = new System.Windows.Forms.TreeNode("M1  2时0分0秒  [视频10]");
        var m2 = new System.Windows.Forms.TreeNode("M2  4时0分0秒  [视频20]");
        math.Nodes.Add(m1);
        math.Nodes.Add(m2);
        root6.Nodes.Add(math);
        tree.Nodes.Add(root6);
        TreeFilter.ApplyFilter(tree, new FilterOptions { Name = "math" }, null);
        Assert(math.Parent == root6, "名称命中: Math 枝干保留");
        Assert(m1.Parent == math && m2.Parent == math, "名称命中: 子文件夹全部被选中 (M1/M2)");
        Assert(math.Text == "Math  6时0分0秒  [视频30]", "名称命中: Math 重算为真实内容, got " + math.Text);
        Assert(root6.Text == "Root6  6时0分0秒  [视频30]", "名称命中: 根重算为真实内容, got " + root6.Text);

        // 名称命中叶子保留; 未命名的兄弟枝干被移除
        tree.Nodes.Clear();
        var root7 = new System.Windows.Forms.TreeNode("Root7  4时0分0秒  [视频20]");
        var branchA = new System.Windows.Forms.TreeNode("A  2时0分0秒  [视频10]");
        var a1 = new System.Windows.Forms.TreeNode("A1  2时0分0秒  [视频10]");
        var mathLeaf = new System.Windows.Forms.TreeNode("MathL  2时0分0秒  [视频10]");
        branchA.Nodes.Add(a1);
        root7.Nodes.Add(branchA);
        root7.Nodes.Add(mathLeaf);
        tree.Nodes.Add(root7);
        TreeFilter.ApplyFilter(tree, new FilterOptions { Name = "math" }, null);
        Assert(mathLeaf.Parent == root7, "名称命中: 叶子 MathL 保留");
        Assert(branchA.Parent == null, "名称未命中: 枝干 A 被移除");
        Assert(a1.Parent == branchA, "名称未命中: A 的子叶 A1 随枝干一并移除");
        Assert(root7.Text == "Root7  2时0分0秒  [视频10]", "名称命中: 根重算仅含命中叶子, got " + root7.Text);

        // 名称命中 + 时长: 子文件夹视为名称命中，但仍须通过时长过滤才被选中
        tree.Nodes.Clear();
        var root8 = new System.Windows.Forms.TreeNode("Root8  9时0分0秒  [视频40]");
        var math2 = new System.Windows.Forms.TreeNode("Math2  6时0分0秒  [视频30]");
        var s1 = new System.Windows.Forms.TreeNode("S1  1时0分0秒  [视频10]");
        var s2 = new System.Windows.Forms.TreeNode("S2  4时0分0秒  [视频20]");
        var other = new System.Windows.Forms.TreeNode("Other  3时0分0秒  [视频10]");
        math2.Nodes.Add(s1);
        math2.Nodes.Add(s2);
        root8.Nodes.Add(math2);
        root8.Nodes.Add(other);
        tree.Nodes.Add(root8);
        TreeFilter.ApplyFilter(tree, new FilterOptions { Name = "math", DurationMinHours = 2.0 }, null);
        Assert(math2.Parent == root8, "名称+时长: Math2 枝干保留 (有视频)");
        Assert(s1.Parent == null, "名称+时长: 子叶 S1 (1h) 虽继承名称仍被时长过滤移除");
        Assert(s2.Parent == math2, "名称+时长: 子叶 S2 (4h) 继承名称且通过时长被选中");
        Assert(other.Parent == null, "名称+时长: 未命中且未达时长的 Other 被移除");
        Assert(math2.Text == "Math2  4时0分0秒  [视频20]", "名称+时长: Math2 重算仅含通过过滤的子叶, got " + math2.Text);
        Assert(root8.Text == "Root8  4时0分0秒  [视频20]", "名称+时长: 根重算为 4h, got " + root8.Text);

        // 名称命中 + 视频数: 子文件夹视为名称命中，但仍须通过数量过滤才被选中
        tree.Nodes.Clear();
        var root9 = new System.Windows.Forms.TreeNode("Root9  6时0分0秒  [视频30]");
        var math3 = new System.Windows.Forms.TreeNode("Math3  6时0分0秒  [视频30]");
        var g1 = new System.Windows.Forms.TreeNode("C1  2时0分0秒  [视频8]");
        var g2 = new System.Windows.Forms.TreeNode("C2  4时0分0秒  [视频22]");
        math3.Nodes.Add(g1);
        math3.Nodes.Add(g2);
        root9.Nodes.Add(math3);
        tree.Nodes.Add(root9);
        TreeFilter.ApplyFilter(tree, new FilterOptions { Name = "math", CountMin = 10 }, null);
        Assert(math3.Parent == root9, "名称+数量: Math3 枝干保留");
        Assert(g1.Parent == null, "名称+数量: 子叶 C1 (8<10) 继承名称但仍被数量过滤移除");
        Assert(g2.Parent == math3, "名称+数量: 子叶 C2 (22) 继承名称且通过数量被选中");
        Assert(math3.Text == "Math3  4时0分0秒  [视频22]", "名称+数量: Math3 重算仅含通过过滤的子叶, got " + math3.Text);
        Assert(root9.Text == "Root9  4时0分0秒  [视频22]", "名称+数量: 根重算为 4h, got " + root9.Text);
    }

    public static void TestExtractName()
    {
        Console.WriteLine("== TreeFilter.ExtractName ==");
        Assert(TreeFilter.ExtractName("folder  1时0分0秒  [视频5]") == "folder", "Extract name from formatted text");
        Assert(TreeFilter.ExtractName("a  0时0分0秒  [视频0]") == "a", "Extract single char name");
        Assert(TreeFilter.ExtractName("") == "", "Empty string returns empty");
    }

    public static void TestExtractNameRobust()
    {
        Console.WriteLine("== TreeFilter.ExtractName 双空格文件夹名 ==");
        Assert(TreeFilter.ExtractName("a  b  1时0分0秒  [视频1]") == "a  b", "文件夹名含双空格被完整保留");
        Assert(TreeFilter.ExtractName("普通目录  0时0分0秒  [视频0]") == "普通目录", "普通目录名正确提取");
        Assert(Math.Abs(TreeFilter.ExtractSeconds("a  b  2时3分4秒  [视频1]") - 7384) < 0.01, "双空格名解析时长 2h3m4s = 7384s");
        Assert(TreeFilter.ExtractCount("a  b  0时0分0秒  [视频9]") == 9, "双空格名解析数量 9");
    }

    public static void TestExtractSeconds()
    {
        Console.WriteLine("== TreeFilter.ExtractSeconds ==");
        Assert(Math.Abs(TreeFilter.ExtractSeconds("x  1时0分0秒  [视频1]") - 3600) < 0.01, "1h = 3600s");
        Assert(Math.Abs(TreeFilter.ExtractSeconds("x  0时30分0秒  [视频1]") - 1800) < 0.01, "30m = 1800s");
        Assert(Math.Abs(TreeFilter.ExtractSeconds("x  0时0分45秒  [视频1]") - 45) < 0.01, "45s");
        Assert(Math.Abs(TreeFilter.ExtractSeconds("x  2时30分15秒  [视频1]") - 9015) < 0.01, "2h30m15s = 9015s");
    }

    public static void TestExtractCount()
    {
        Console.WriteLine("== TreeFilter.ExtractCount ==");
        Assert(TreeFilter.ExtractCount("x  1时0分0秒  [视频123]") == 123, "Extract count 123");
        Assert(TreeFilter.ExtractCount("x  0时0分0秒  [视频0]") == 0, "Extract count 0");
        Assert(TreeFilter.ExtractCount("no bracket") == 0, "No bracket returns 0");
    }

    public static void TestRunMultiple()
    {
        Console.WriteLine("== VideoScanner.RunMultiple ==");

        string tempRoot = Path.Combine(Path.GetTempPath(), "vt_test_multiroot_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        try
        {
            string rootA = Path.Combine(tempRoot, "RootA");
            string rootB = Path.Combine(tempRoot, "RootB");
            string subA = Path.Combine(rootA, "SubFolder");
            Directory.CreateDirectory(subA);
            Directory.CreateDirectory(rootB);

            Console.WriteLine("  Creating test video files in temp dirs...");

            string[] testFiles = new string[]
            {
                Path.Combine(rootA, "video1.mp4"),
                Path.Combine(rootA, "video2.mp4"),
                Path.Combine(subA, "video3.mp4"),
                Path.Combine(rootB, "video4.mp4"),
            };

            foreach (string f in testFiles)
                CreateDummyMp4(f);

            var ct = CancellationToken.None;
            ScanResult result = VideoScanner.RunMultiple(new[] { rootA, rootB }, true, ct);

            Assert(result != null, "RunMultiple returns non-null");
            Assert(result.FolderResults.Count == 3, "RunMultiple found 3 folders (RootA, RootA/SubFolder, RootB), got " + result.FolderResults.Count);

            // Dummy MP4s have no valid duration, so TotalSeconds may be 0
            Assert(result.TotalSeconds >= 0, "TotalSeconds >= 0, got " + result.TotalSeconds);

            // Each folder's FileCount includes subfolder files (Aggregate behavior)
            // RootA has 3 (2 direct + 1 in SubFolder), RootA/SubFolder has 1, RootB has 1
            // The root folder's FileCount is the total unique file count
            var rootAFolder = result.FolderResults.Find(r => r.FolderPath == rootA);
            Assert(rootAFolder != null, "RootA found in FolderResults");
            Assert(rootAFolder.FileCount == 3, "RootA FileCount = 3 (includes SubFolder), got " + rootAFolder.FileCount);

            var rootBFolder = result.FolderResults.Find(r => r.FolderPath == rootB);
            Assert(rootBFolder != null, "RootB found in FolderResults");
            Assert(rootBFolder.FileCount == 1, "RootB FileCount = 1, got " + rootBFolder.FileCount);

            int depthA = VideoScanner.DepthOf(result, Path.Combine(rootA, "SubFolder"));
            Assert(depthA == 1, "DepthOf RootA/SubFolder = 1, got " + depthA);

            int depthB = VideoScanner.DepthOf(result, rootB);
            Assert(depthB == 0, "DepthOf RootB (root) = 0, got " + depthB);
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    public static void TestRunMultipleOverlap()
    {
        Console.WriteLine("== VideoScanner.RunMultiple overlap detection ==");

        string tempRoot = Path.Combine(Path.GetTempPath(), "vt_test_overlap_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        try
        {
            string rootA = Path.Combine(tempRoot, "RootA");
            string subA = Path.Combine(rootA, "Sub");
            Directory.CreateDirectory(subA);

            CreateDummyMp4(Path.Combine(rootA, "v1.mp4"));
            CreateDummyMp4(Path.Combine(subA, "v2.mp4"));

            var ct = CancellationToken.None;
            ScanResult result = VideoScanner.RunMultiple(new[] { rootA, rootA }, true, ct);

            // Duplicate roots are processed separately; each adds its own FolderResults
            // RootA appears twice, RootA/Sub appears twice
            Assert(result.FolderResults.Count == 4, "Duplicate roots: found 4 folder entries (RootA x2, RootA/Sub x2), got " + result.FolderResults.Count);
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    public static void TestNormalizePath()
    {
        Console.WriteLine("== VideoScanner.NormalizePath ==");
        Assert(VideoScanner.NormalizePath(@"C:/a/b") == @"C:\a\b", "'/' 归一化为 '\\'");
        Assert(VideoScanner.NormalizePath(@"C:\a\b") == @"C:\a\b", "反斜杠保持不变");
        Assert(VideoScanner.NormalizePath(@"""C:/a/b""") == @"C:\a\b", "去除包围引号并归一化");
        Assert(VideoScanner.NormalizePath("  C:\\a\\b  ") == @"C:\a\b", "去除首尾空白");
        Assert(VideoScanner.NormalizePath(@"C:\a\b/c\d\e") == @"C:\a\b\c\d\e", "混合分隔符全部归一化");
        Assert(VideoScanner.NormalizePath("") == "", "空串返回空串");
        Assert(VideoScanner.NormalizePath(null) == null, "null 返回 null");
    }

    public static void TestRunMultipleSeparators()
    {
        Console.WriteLine("== RunMultiple 分隔符等价 ==");

        string tempRoot = Path.Combine(Path.GetTempPath(), "vt_test_sep_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        try
        {
            string rootA = Path.Combine(tempRoot, "RootA");
            string rootB = Path.Combine(tempRoot, "RootB");
            Directory.CreateDirectory(rootA);
            Directory.CreateDirectory(rootB);
            CreateDummyMp4(Path.Combine(rootA, "v1.mp4"));
            CreateDummyMp4(Path.Combine(rootB, "v2.mp4"));

            var ct = CancellationToken.None;
            ScanResult bs = VideoScanner.RunMultiple(new[] { rootA, rootB }, false, ct);
            ScanResult fwd = VideoScanner.RunMultiple(new[] { rootA.Replace('\\', '/'), rootB.Replace('\\', '/') }, false, ct);

            Assert(bs.FolderResults.Count == 2, "反斜杠根: 2 个文件夹, got " + bs.FolderResults.Count);
            Assert(fwd.FolderResults.Count == 2, "正斜杠根: 2 个文件夹, got " + fwd.FolderResults.Count);
            Assert(bs.TotalSeconds == fwd.TotalSeconds, "正/反斜杠总时长一致 (" + bs.TotalSeconds + " vs " + fwd.TotalSeconds + ")");

            var bsPaths = new HashSet<string>(bs.FolderResults.ConvertAll(r => r.FolderPath));
            foreach (var r in fwd.FolderResults)
            {
                Assert(bsPaths.Contains(r.FolderPath), "正斜杠根的结果路径与反斜杠运行一致: " + r.FolderPath);
                Assert(r.FolderPath.IndexOf('/') < 0, "结果路径已归一化为反斜杠: " + r.FolderPath);
            }
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    public static void TestDepthOfNested()
    {
        Console.WriteLine("== VideoScanner.DepthOf 嵌套深度 ==");

        string tempRoot = Path.Combine(Path.GetTempPath(), "vt_test_depth_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        try
        {
            string a = Path.Combine(tempRoot, "A");
            string b = Path.Combine(a, "B");
            string c = Path.Combine(b, "C");
            Directory.CreateDirectory(c);
            CreateDummyMp4(Path.Combine(c, "v.mp4"));

            var ct = CancellationToken.None;
            ScanResult result = VideoScanner.RunMultiple(new[] { tempRoot }, true, ct);

            Assert(VideoScanner.DepthOf(result, tempRoot) == 0, "根目录深度 0, got " + VideoScanner.DepthOf(result, tempRoot));
            Assert(VideoScanner.DepthOf(result, a) == 1, "A 深度 1, got " + VideoScanner.DepthOf(result, a));
            Assert(VideoScanner.DepthOf(result, b) == 2, "B 深度 2, got " + VideoScanner.DepthOf(result, b));
            Assert(VideoScanner.DepthOf(result, c) == 3, "C 深度 3, got " + VideoScanner.DepthOf(result, c));
            Assert(VideoScanner.DepthOf(result, a.Replace('\\', '/')) == 1, "正斜杠输入深度等价 1");
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    static void CreateDummyMp4(string path)
    {
        byte[] header = new byte[1024];
        header[0] = 0x00; header[1] = 0x00; header[2] = 0x00; header[3] = 0x18;
        header[4] = 0x66; header[5] = 0x74; header[6] = 0x79; header[7] = 0x70;
        File.WriteAllBytes(path, header);
    }

    // 回归防护：树挂载在可见窗体上时，ApplyFilter/ClearFilter 逐节点改 Text/Remove/Expand
    // 会触发海量重排重绘而严重卡死（E:\ 全盘扫描树曾 >600s 无响应）。必须由 BeginUpdate 包裹。
    public static void TestFilterPerformanceOnVisibleTree()
    {
        Console.WriteLine("== TreeFilter 可见树性能（防回归） ==");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Exception threadError = null;
        var thread = new Thread(() =>
        {
            try
            {
                var form = new System.Windows.Forms.Form { Width = 400, Height = 500, Opacity = 0, ShowInTaskbar = false };
                var tree = new System.Windows.Forms.TreeView { Dock = System.Windows.Forms.DockStyle.Fill };
                form.Controls.Add(tree);
                form.Show();

                tree.BeginUpdate();
                try
                {
                    var root = new System.Windows.Forms.TreeNode("Root  200时0分0秒  [视频6000]");
                    for (int i = 0; i < 200; i++)
                    {
                        var dir = new System.Windows.Forms.TreeNode("Dir" + i + "  1时0分0秒  [视频30]");
                        for (int j = 0; j < 30; j++)
                            dir.Nodes.Add(new System.Windows.Forms.TreeNode("Sub" + i + "_" + j + "  1时0分0秒  [视频1]"));
                        root.Nodes.Add(dir);
                    }
                    tree.Nodes.Add(root);
                }
                finally { tree.EndUpdate(); }

                var opt = new FilterOptions { Name = "sub" };
                FilterState st = null;
                st = TreeFilter.ApplyFilter(tree, opt, st);
                TreeFilter.ClearFilter(tree, st);
                form.Close();
            }
            catch (Exception ex) { threadError = ex; }
        });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        bool done = thread.Join(20000);
        sw.Stop();
        if (!done)
        {
            Assert(false, "可见树过滤超过 20 秒（疑似逐节点重排重绘卡死）");
            return;
        }
        if (threadError != null)
        {
            Assert(false, "可见树过滤异常: " + threadError.Message);
            return;
        }
        Assert(sw.ElapsedMilliseconds < 20000, "可见树 6200 节点 过滤+还原耗时 " + sw.ElapsedMilliseconds + " ms (< 20000)");
    }

    public static void Main(string[] args)
    {
        Console.WriteLine("=== MultiRoot + Filter Tests ===");
        TestTreeFilterMatches();
        TestFilterTreeApply();
        TestExtractName();
        TestExtractNameRobust();
        TestExtractSeconds();
        TestExtractCount();
        TestNormalizePath();
        TestRunMultiple();
        TestRunMultipleOverlap();
        TestRunMultipleSeparators();
        TestDepthOfNested();
        TestFilterPerformanceOnVisibleTree();
        Console.WriteLine();
        Console.WriteLine("Total: Passed " + Passed + ", Failed " + Failed);
        Environment.ExitCode = Failed > 0 ? 1 : 0;
    }
}
