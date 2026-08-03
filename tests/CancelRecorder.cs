using System;
using System.Collections.Generic;
using System.Threading;
using VideoTime;

public class CancelRecorder : IProgress<ScanProgress>
{
    public string CancelPhase = "";
    public Action OnHit = null;
    public int Hit;
    public List<string> Lines = new List<string>();

    public void Report(ScanProgress v)
    {
        lock (Lines)
        {
            Lines.Add(v.Phase + ":" + v.Processed + "/" + v.Total);
        }
        if (v.Phase == CancelPhase && Interlocked.Exchange(ref Hit, 1) == 0 && OnHit != null)
            OnHit();
    }
}
