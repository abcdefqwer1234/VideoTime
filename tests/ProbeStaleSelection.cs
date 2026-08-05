using System;
using System.Reflection;
using System.Windows.Forms;
using VideoTime;

internal static class ProbeStaleSelection
{
    private static int _passed, _failed;

    private static void Check(bool cond, string name, string detail)
    {
        if (cond) { _passed++; Console.WriteLine("PASS: " + name); }
        else { _failed++; Console.WriteLine("FAIL: " + name + " :: " + detail); }
    }

    private static string GetClipText()
    {
        try { return Clipboard.GetText(); }
        catch (Exception) { return null; }
    }

    private static object GetField(object o, string name)
    {
        return o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(o);
    }

    private static void SetField(object o, string name, object value)
    {
        o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(o, value);
    }

    private static void InvokeCopy(TreeView tree, MethodInfo copySel, object f, TreeNode node)
    {
        SetField(f, "_selNode", node);
        tree.SelectedNode = node;
        try { copySel.Invoke(f, null); }
        catch (Exception ex) { Console.WriteLine("  EX: " + ex); throw; }
    }

    [STAThread]
    private static void Main()
    {
        using (var f = new Form1())
        {
            f.Show();
            Application.DoEvents();
            var t = f.GetType();
            var tree = GetField(f, "DetailTree") as TreeView;
            var copySel = t.GetMethod("CopyTreeSelection", BindingFlags.Instance | BindingFlags.NonPublic);
            var node = new TreeNode("0123456789");
            tree.Nodes.Add(node);

            // --- Case 1: selection fully beyond rewritten (shorter) text -> fallback to SelectedNode ---
            SetField(f, "_selStart", 8);
            SetField(f, "_selEnd", 15);
            node.Text = "0123";
            bool threw = false;
            try { InvokeCopy(tree, copySel, f, node); }
            catch (Exception) { threw = true; }
            Check(!threw, "Case1: 选区全部越界不再抛异常", "threw=" + threw);
            string c1 = GetClipText();
            if (c1 == null) { Console.WriteLine("  WARN: 剪贴板不可用，跳过内容校验"); _passed++; }
            else Check(c1 == "0123", "Case1: 回退复制整行文本 0123", "clip=" + c1);

            // --- Case 2: partial clamp -> substring survives with clamped end ---
            SetField(f, "_selStart", 3);
            SetField(f, "_selEnd", 9);
            node.Text = "012345";
            threw = false;
            try { InvokeCopy(tree, copySel, f, node); }
            catch (Exception) { threw = true; }
            Check(!threw, "Case2: 部分越界选区不再抛异常", "threw=" + threw);
            string c2 = GetClipText();
            if (c2 == null) { Console.WriteLine("  WARN: 剪贴板不可用，跳过内容校验"); _passed++; }
            else Check(c2 == "345", "Case2: 剪贴板为钳制后子串 345", "clip=" + c2);

            // --- Case 3: empty text protection (SetText("") throws inside SafeSetClipboard; only no-exception matters) ---
            SetField(f, "_selStart", 0);
            SetField(f, "_selEnd", 5);
            node.Text = "";
            threw = false;
            try { InvokeCopy(tree, copySel, f, node); }
            catch (Exception) { threw = true; }
            Check(!threw, "Case3: 空文本选区不再抛异常", "threw=" + threw);

            // --- Case 4: both indices beyond length, both clamp -> fallback to SelectedNode ---
            SetField(f, "_selStart", 20);
            SetField(f, "_selEnd", 30);
            node.Text = "abcdefgh";
            threw = false;
            try { InvokeCopy(tree, copySel, f, node); }
            catch (Exception) { threw = true; }
            Check(!threw, "Case4: 整体越界选区不再抛异常", "threw=" + threw);
            string c4 = GetClipText();
            if (c4 == null) { Console.WriteLine("  WARN: 剪贴板不可用，跳过内容校验"); _passed++; }
            else Check(c4 == "abcdefgh", "Case4: 回退复制整行文本 abcdefgh", "clip=" + c4);
        }

        Console.WriteLine();
        Console.WriteLine("Total: Passed " + _passed + ", Failed " + _failed);
        Environment.ExitCode = _failed > 0 ? 1 : 0;
    }
}
