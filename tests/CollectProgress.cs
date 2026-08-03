using System;
using System.Collections.Generic;
using VideoTime;
public class CollectProgress : IProgress<ScanProgress>
{
    public List<string> Lines = new List<string>();
    public void Report(ScanProgress v)
    {
        lock (Lines)
        {
            Lines.Add(v.Phase + ":" + v.Processed + "/" + v.Total);
        }
    }
}
