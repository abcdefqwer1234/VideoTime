using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace VideoTime
{
    public static class DurationParser
    {
        public static readonly HashSet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mov", ".m4v", ".3gp", ".mkv", ".webm", ".avi", ".wmv", ".asf"
        };

        public static bool IsVideoFile(string path)
        {
            return Extensions.Contains(Path.GetExtension(path));
        }

        public static string[] GetVideoFiles(string folder)
        {
            string[] all = Directory.GetFiles(folder);
            if (all.Length == 0) return all;
            return Array.FindAll(all, IsVideoFile);
        }

        public static Dictionary<string, double> ReadAll(List<string> files, out int fail, int threads)
        {
            return ReadAll(files, out fail, out List<FailureRecord> ignored, threads, CancellationToken.None, null);
        }

        internal static Dictionary<string, double> ReadAll(List<string> files, out int fail, out List<FailureRecord> failed, int threads, CancellationToken ct = default, Action<string> fileDone = null)
        {
            var ordered = new double[files.Count];
            var failFlags = new int[files.Count];
            var failures = new ConcurrentBag<FailureRecord>();
            var opts = new ParallelOptions { MaxDegreeOfParallelism = threads, CancellationToken = ct };
            Parallel.ForEach(files, opts, (path, state, index) =>
            {
                ct.ThrowIfCancellationRequested();
                double d = ParseFile(path, out string reason);
                if (d >= 0) ordered[index] = d;
                else
                {
                    Interlocked.Exchange(ref failFlags[index], 1);
                    failures.Add(new FailureRecord { Path = path, Reason = reason });
                }
                if (fileDone != null) fileDone(path);
            });

            var result = new Dictionary<string, double>();
            int f = 0;
            for (int i = 0; i < files.Count; i++)
            {
                if (failFlags[i] != 0) f++;
                else result[files[i]] = ordered[i];
            }
            fail = f;
            failed = new List<FailureRecord>(failures);
            return result;
        }

        public static double ParseFile(string path)
        {
            return ParseFile(path, out _);
        }

        internal static double ParseFile(string path, out string reason)
        {
            reason = "";
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536))
                {
                    long fileLen = fs.Length;
                    if (fileLen < 16)
                    {
                        reason = "文件过小(<16字节)";
                        return -1;
                    }

                    byte[] magic = new byte[16];
                    int r = fs.Read(magic, 0, magic.Length);

                    if (r >= 4 && magic[0] == 0x1A && magic[1] == 0x45 && magic[2] == 0xDF && magic[3] == 0xA3)
                        return ParseMkv(fs, out reason);

                    if (r >= 12 && magic[0] == 'R' && magic[1] == 'I' && magic[2] == 'F' && magic[3] == 'F'
                        && magic[8] == 'A' && magic[9] == 'V' && magic[10] == 'I' && magic[11] == ' ')
                        return ParseAvi(fs, out reason);

                    if (IsAsfGuid(magic))
                        return ParseAsf(fs, out reason);

                    bool mp4ish = r >= 8
                        && ((magic[4] == 'f' && magic[5] == 't' && magic[6] == 'y' && magic[7] == 'p')
                         || (magic[4] == 'm' && magic[5] == 'o' && magic[6] == 'o' && magic[7] == 'v'));
                    if (mp4ish || IsMp4Ext(Path.GetExtension(path)))
                        return ParseMp4(fs, out reason);

                    reason = "不支持的文件格式: " + Path.GetExtension(path);
                    return -1;
                }
            }
            catch (Exception ex)
            {
                reason = ShortReason(ex);
                return -1;
            }
        }

        internal static string ShortReason(Exception ex)
        {
            string msg = ex.GetType().Name + ": " + ex.Message;
            if (msg.Length > 200) msg = msg.Substring(0, 200) + "…";
            return msg;
        }

        private static bool IsMp4Ext(string ext)
        {
            return ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".mov", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".m4v", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".3gp", StringComparison.OrdinalIgnoreCase);
        }

        // ---------- MP4 / MOV family ----------

        private static double ParseMp4(FileStream fs, out string reason)
        {
            reason = "";
            try
            {
                long fileLen = fs.Length;

                const long Window = 4L << 20;
                const long MaxWindow = 128L << 20;
                long first = Math.Min(Window, fileLen);

                double tail = ScanWindowStreaming(fs, fileLen - first, first, fileLen, out string tailReason);
                if (tail >= 0) return tail;
                reason = tailReason;

                if (fileLen > first)
                {
                    double head = ScanWindowStreaming(fs, 0, first, fileLen, out string headReason);
                    if (head >= 0) return head;
                    if (!string.IsNullOrEmpty(headReason)) reason = headReason;
                }

                if (fileLen > first)
                {
                    long grow = Math.Min(MaxWindow, fileLen);
                    if (grow > first)
                    {
                        double grown = ScanWindowStreaming(fs, fileLen - grow, grow, fileLen, out string growReason);
                        if (grown >= 0) return grown;
                        if (!string.IsNullOrEmpty(growReason)) reason = growReason;
                    }
                }

                if (string.IsNullOrEmpty(reason)) reason = "未找到有效moov元数据";
                return -1;
            }
            catch (Exception ex)
            {
                reason = ShortReason(ex);
                return -1;
            }
        }

        private const int ScanChunk = 1 << 20;

        private static double ScanWindowStreaming(FileStream fs, long start, long len, long fileLen, out string reason)
        {
            reason = "";
            try
            {
                if (len <= 0) return -1;
                const int Overlap = 7; // "moov" 8 字节盒头可能跨块，保留末 7 字节重扫
                byte[] buf = new byte[ScanChunk + Overlap];
                long end = start + len;

                long anchor = start;
                int fill = ReadFully(fs, buf, 0, start, (int)Math.Min(ScanChunk, end - start));

                while (true)
                {
                    for (int i = 0; i + 8 <= fill; i++)
                    {
                        if (buf[i + 4] == 'm' && buf[i + 5] == 'o' && buf[i + 6] == 'o' && buf[i + 7] == 'v')
                        {
                            long boxStart = anchor + i;
                            if (boxStart + 8 > end) continue;
                            long boxSize = BE32(buf, i);
                            if (boxSize >= 8 && boxStart + boxSize <= fileLen)
                            {
                                double d = ParseMoov(fs, boxStart, boxSize);
                                if (d >= 0) return d;
                                reason = "moov元数据损坏或不支持";
                            }
                        }
                    }

                    if (anchor + fill >= end) break;

                    int keep = Math.Min(Overlap, fill);
                    if (keep > 0) Buffer.BlockCopy(buf, fill - keep, buf, 0, keep);
                    anchor += fill - keep;
                    long pos = anchor + keep;
                    int want = (int)Math.Min(ScanChunk, end - pos);
                    if (want <= 0) break;
                    int got = ReadFully(fs, buf, keep, pos, want);
                    if (got <= 0) break;
                    fill = keep + got;
                }
                return -1;
            }
            catch (Exception ex)
            {
                reason = ShortReason(ex);
                return -1;
            }
        }

        private static int ReadFully(FileStream fs, byte[] buf, int offset, long filePos, int count)
        {
            if (count <= 0) return 0;
            fs.Position = filePos;
            int total = 0;
            while (total < count)
            {
                int n = fs.Read(buf, offset + total, count - total);
                if (n <= 0) break;
                total += n;
            }
            return total;
        }

        private static double ParseMoov(FileStream fs, long boxStart, long boxSize)
        {
            try
            {
                long toRead = Math.Min(boxSize, 16L << 20);
                if (toRead < 16) return -1;
                byte[] moov = new byte[toRead];
                fs.Position = boxStart;
                int read = fs.Read(moov, 0, moov.Length);
                if (read < 16) return -1;

                long p = 8;
                while (p + 8 <= read)
                {
                    long csize = BE32(moov, p);
                    if (csize < 8) break;
                    if (moov[p + 4] == 'm' && moov[p + 5] == 'v' && moov[p + 6] == 'h' && moov[p + 7] == 'd')
                        return ParseMvhd(moov, p, csize);
                    if (p + csize > read) break;
                    p += csize;
                }
                return -1;
            }
            catch { return -1; }
        }

        private static double ParseMvhd(byte[] b, long off, long size)
        {
            try
            {
                if (size < 32) return -1;
                int version = b[off + 8];
                if (version == 0)
                {
                    uint timescale = BE32(b, off + 20);
                    uint duration = BE32(b, off + 24);
                    if (timescale == 0) return -1;
                    return duration / (double)timescale;
                }
                else if (version == 1)
                {
                    uint timescale = BE32(b, off + 28);
                    ulong duration = BE64(b, off + 32);
                    if (timescale == 0) return -1;
                    return duration / (double)timescale;
                }
                return -1;
            }
            catch { return -1; }
        }

        // ---------- MKV / WebM (EBML) ----------

        private static double ParseMkv(FileStream fs, out string reason)
        {
            reason = "";
            try
            {
                long fileLen = fs.Length;
                long window = Math.Min(4L << 20, fileLen);
                byte[] buf = new byte[window];
                fs.Position = 0;
                int read = fs.Read(buf, 0, buf.Length);
                if (read < 8) { reason = "mkv文件过小"; return -1; }

                long segStart = FindEbmElement(buf, read, 0, 0x18538067);
                if (segStart < 0) { reason = "未找到Segment"; return -1; }

                if (!TryReadEbmlId(buf, read, segStart, out int idLen, out _)) { reason = "mkv结构错误"; return -1; }
                if (!TryReadEbmlSize(buf, read, segStart + idLen, out int szLen, out long segSize)) { reason = "mkv结构错误"; return -1; }

                long dataStart = segStart + idLen + szLen;
                long segEnd = segSize >= 0 ? dataStart + segSize : read;
                long cap = Math.Min(segEnd, read);

                long p = dataStart;
                while (p + 4 <= cap)
                {
                    if (!TryReadEbmlId(buf, read, p, out int cidLen, out ulong cid)) break;
                    if (!TryReadEbmlSize(buf, read, p + cidLen, out int cszLen, out long csz)) break;
                    if (csz < 0) break;
                    long elemData = p + cidLen + cszLen;
                    if (cid == 0x1549A966)
                    {
                        long len = Math.Min(csz, cap - elemData);
                        return ParseMkvInfo(buf, read, elemData, len, out reason);
                    }
                    long next = elemData + csz;
                    if (next <= p || next > cap) break;
                    p = next;
                }

                reason = "未找到mkv的Info时长信息";
                return -1;
            }
            catch (Exception ex)
            {
                reason = ShortReason(ex);
                return -1;
            }
        }

        private static double ParseMkvInfo(byte[] b, int read, long dataStart, long dataLen, out string reason)
        {
            reason = "";
            ulong timescale = 1000000;
            double? duration = null;
            long p = dataStart;
            long end = Math.Min(dataStart + dataLen, read);
            while (p + 4 <= end)
            {
                if (!TryReadEbmlId(b, read, p, out int idLen, out ulong id)) break;
                if (!TryReadEbmlSize(b, read, p + idLen, out int szLen, out long sz)) break;
                if (sz < 0) break;
                long elemData = p + idLen + szLen;
                if (elemData + sz > end) break;
                if (id == 0x2AD7B1 && sz == 4)
                    timescale = ReadEbmlUInt(b, elemData);
                else if (id == 0x4489 && (sz == 4 || sz == 8))
                    duration = sz == 8 ? ReadEbmlDouble(b, elemData) : ReadEbmlFloat(b, elemData);
                p = elemData + sz;
            }
            if (duration.HasValue)
                return duration.Value * timescale / 1e9;
            reason = "mkv的Info缺少Duration";
            return -1;
        }

        private static long FindEbmElement(byte[] b, int read, long from, ulong wantId)
        {
            long p = from;
            while (p + 4 <= read)
            {
                if (!TryReadEbmlId(b, read, p, out int idLen, out ulong id)) return -1;
                if (id == wantId) return p;
                if (!TryReadEbmlSize(b, read, p + idLen, out int szLen, out long sz)) return -1;
                if (sz < 0) return -1;
                long next = p + idLen + szLen + sz;
                if (next <= p || next > read) return -1;
                p = next;
            }
            return -1;
        }

        private static int EbmlVintLength(byte first)
        {
            if ((first & 0x80) != 0) return 1;
            if ((first & 0x40) != 0) return 2;
            if ((first & 0x20) != 0) return 3;
            if ((first & 0x10) != 0) return 4;
            if ((first & 0x08) != 0) return 5;
            if ((first & 0x04) != 0) return 6;
            if ((first & 0x02) != 0) return 7;
            if ((first & 0x01) != 0) return 8;
            return 0;
        }

        private static bool TryReadEbmlId(byte[] b, int read, long p, out int len, out ulong id)
        {
            len = 0; id = 0;
            if (p < 0 || p >= read) return false;
            int l = EbmlVintLength(b[p]);
            if (l == 0 || p + l > read) return false;
            len = l;
            for (int i = 0; i < l; i++) id = (id << 8) | b[p + i];
            return true;
        }

        private static bool TryReadEbmlSize(byte[] b, int read, long p, out int len, out long size)
        {
            len = 0; size = -1;
            if (p < 0 || p >= read) return false;
            int l = EbmlVintLength(b[p]);
            if (l == 0 || p + l > read) return false;
            len = l;
            ulong val = 0;
            for (int i = 0; i < l; i++) val = (val << 8) | b[p + i];
            val &= ~(1UL << (7 * l));
            ulong allOnes = (1UL << (7 * l)) - 1;
            if (val == allOnes) size = -1;
            else size = (long)val;
            return true;
        }

        private static ulong ReadEbmlUInt(byte[] b, long p)
        {
            ulong v = 0;
            for (int i = 0; i < 4; i++) v = (v << 8) | b[p + i];
            return v;
        }

        private static double ReadEbmlFloat(byte[] b, long p)
        {
            FloatBits fb = new FloatBits();
            fb.U = BE32(b, p);
            return fb.F;
        }

        private static double ReadEbmlDouble(byte[] b, long p)
        {
            ulong v = ((ulong)BE32(b, p) << 32) | (ulong)BE32(b, p + 4);
            return BitConverter.Int64BitsToDouble(unchecked((long)v));
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
        private struct FloatBits
        {
            [System.Runtime.InteropServices.FieldOffset(0)] public float F;
            [System.Runtime.InteropServices.FieldOffset(0)] public uint U;
        }

        // ---------- AVI (RIFF) ----------

        private static double ParseAvi(FileStream fs, out string reason)
        {
            reason = "";
            try
            {
                long fileLen = fs.Length;
                int toRead = (int)Math.Min(64 * 1024, fileLen);
                byte[] buf = new byte[toRead];
                fs.Position = 0;
                int read = fs.Read(buf, 0, toRead);

                for (int i = 0; i + 4 + 56 <= read; i++)
                {
                    if (buf[i] == 'a' && buf[i + 1] == 'v' && buf[i + 2] == 'i' && buf[i + 3] == 'h')
                    {
                        uint micro = LE32(buf, i + 4);
                        uint frames = LE32(buf, i + 4 + 16);
                        if (micro == 0 || frames == 0) { reason = "avi头无有效时长"; return -1; }
                        return frames * (double)micro / 1e6;
                    }
                }
                reason = "未找到avi的avih头";
                return -1;
            }
            catch (Exception ex)
            {
                reason = ShortReason(ex);
                return -1;
            }
        }

        // ---------- WMV / ASF ----------

        private static readonly byte[] AsfHeaderGuid =
        {
            0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11, 0xA6, 0xD9, 0x00, 0xAA, 0x00, 0x62, 0xCE, 0x6C
        };

        private static readonly byte[] FilePropsGuid =
        {
            0xA1, 0xDC, 0xAB, 0x8C, 0x47, 0xA9, 0xCF, 0x11, 0x8E, 0xE4, 0x00, 0xC0, 0x0C, 0x20, 0x53, 0x65
        };

        private static bool IsAsfGuid(byte[] b)
        {
            if (b.Length < 16) return false;
            for (int i = 0; i < 16; i++)
                if (b[i] != AsfHeaderGuid[i]) return false;
            return true;
        }

        private static double ParseAsf(FileStream fs, out string reason)
        {
            reason = "";
            try
            {
                long fileLen = fs.Length;
                byte[] head = new byte[64 * 1024];
                fs.Position = 0;
                int read = fs.Read(head, 0, head.Length);
                if (read < 30) { reason = "asf文件过小"; return -1; }

                ulong headerSize = LE64(head, 16);
                long hsz = Math.Min((long)headerSize, fileLen);
                hsz = Math.Min(hsz, 16L << 20);

                byte[] header = head;
                if (hsz > read)
                {
                    header = new byte[hsz];
                    fs.Position = 0;
                    int got = fs.Read(header, 0, (int)hsz);
                    if (got < (int)hsz) hsz = got;
                }

                long p = 30;
                long end = hsz;
                while (p + 16 + 8 <= end)
                {
                    bool isFileProps = GuidEquals(header, p, FilePropsGuid);
                    long objSize = (long)LE64(header, p + 16);
                    if (objSize < 24) break;
                    if (isFileProps)
                    {
                        if (objSize >= 92 && p + 88 + 4 <= end)
                        {
                            ulong play = LE64(header, p + 64);
                            ulong preroll = LE64(header, p + 80);
                            long diff = (long)play - (long)preroll * 10000;
                            if (diff > 0) return diff / 1e7;
                            reason = "asf无有效播放时长";
                            return -1;
                        }
                    }
                    p += objSize;
                }
                reason = "未找到asf的File Properties对象";
                return -1;
            }
            catch (Exception ex)
            {
                reason = ShortReason(ex);
                return -1;
            }
        }

        private static bool GuidEquals(byte[] b, long p, byte[] guid)
        {
            for (int i = 0; i < 16; i++)
                if (b[p + i] != guid[i]) return false;
            return true;
        }

        // ---------- endian helpers ----------

        private static uint BE32(byte[] b, long off)
        {
            return (uint)((b[off] << 24) | (b[off + 1] << 16) | (b[off + 2] << 8) | b[off + 3]);
        }

        private static ulong BE64(byte[] b, long off)
        {
            return ((ulong)BE32(b, off) << 32) | (ulong)BE32(b, off + 4);
        }

        private static uint LE32(byte[] b, long off)
        {
            return (uint)(b[off] | (b[off + 1] << 8) | (b[off + 2] << 16) | (b[off + 3] << 24));
        }

        private static ulong LE64(byte[] b, long off)
        {
            return (ulong)LE32(b, off) | ((ulong)LE32(b, off + 4) << 32);
        }
    }
}
