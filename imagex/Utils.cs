
using System.Buffers.Binary;
using System.Diagnostics;
using System.Numerics;

namespace utils;

public class Size
{
    public int width;
    public int height;

}

public class BitStream
{
    readonly byte[] data;
    readonly int off;
    ulong cont; // must be 64 bit

    readonly int arrLen;
    int bytesLoaded;
    int payload;

    readonly bool skip;
    readonly Func<int, bool> Refill;

    void InitLoad()
    {
        bytesLoaded = Math.Min(8, arrLen);
        if (bytesLoaded == 8)
        {
            var sp = new ReadOnlySpan<byte>(data, off, 8);
            cont = BinaryPrimitives.ReadUInt64BigEndian(sp);
        } else
        {
            for (int i = 0; i < bytesLoaded; i++)
            {
                cont <<= 8;
                cont |= data[off + i];
            }
            cont <<= 64 - bytesLoaded * 8;
        }
        payload = bytesLoaded * 8;
    }

    /// <summary>
    /// Skip zero bytes at 0xFF00 in encoded JPEG data
    /// </summary>
    void InitLoadSkip()
    {
        bytesLoaded = 0;
        int i = bytesLoaded;
        int ldCnt = 0;
        do
        {
            byte b = data[off + i];
            cont <<= 8;
            cont |= b;
            ldCnt++;
            i = b != 0xFF ? i + 1 : i + 2;

        } while (ldCnt < 8 && i < arrLen);
        bytesLoaded = i;
        payload = ldCnt * 8;
        cont <<= 64 - payload;

        //Console.WriteLine("------- InitLoadSkip");
        //Console.WriteLine(cont.BytesLeftToRight().ToHexString());
    }

    /// <summary>
    /// BitStream Ctor
    /// </summary>
    /// <param name="skip">Skip zero bytes at 0xFF00 in encoded JPEG data</param>
    public BitStream(byte[] _data, int begOff, int endOff, bool _skip)
    {

        data = _data;
        off = begOff;

        arrLen = endOff - begOff;

        cont = 0;

        skip = _skip;
        if (skip) InitLoadSkip(); else InitLoad();

        Refill = skip ? RefillSkip : RefillAny;
    }

    bool RefillAny(int vbitLen)
    {
        int bytesToLoad = arrLen - bytesLoaded;
        if (payload + bytesToLoad * 8 < vbitLen) return false;

        // refill cont
        ulong payloadRefill = 0; // must be 64 bit
        var availLen = 64 - payload;
        var refillBytes = Math.Min(availLen / 8, bytesToLoad);
        var refillBits = refillBytes * 8;
        for (int i = 0; i < refillBytes; i++)
        {
            payloadRefill <<= 8;
            payloadRefill |= data[off + bytesLoaded];
            bytesLoaded++;
        }
        payloadRefill <<= availLen - refillBits;
        cont |= payloadRefill;

        payload += refillBits;

        return true;
    }

    /// <summary>
    /// Skip zero bytes at 0xFF00 in encoded JPEG data
    /// </summary>
    bool RefillSkip(int vbitLen)
    {
        //Console.WriteLine("------- RefillSkip");
        //Console.WriteLine($"payload: {payload}");
        //Console.Write(cont.BytesLeftToRight().ToHexString());

        if (bytesLoaded == arrLen) return false;

        // refill cont
        ulong payloadRefill = 0; // must be 64 bit
        var availLen = 64 - payload;
        var refillBytesMax = availLen / 8;
        int refillBytes = 0;
        int i = bytesLoaded;
        do
        {
            byte b = data[off + i];
           // Console.WriteLine($"--- {b:X2}");
            payloadRefill <<= 8;
            payloadRefill |= b;
            refillBytes++;
            i = b != 0xFF ? i + 1 : i + 2;

            //if (b == 0xFF) Console.Write("------- FF00");

        } while (refillBytes < refillBytesMax && i < arrLen);
        bytesLoaded = i;

        //Console.WriteLine(" <--- " + payloadRefill.BytesLeftToRight().ToHexString());

        var refillBits = refillBytes * 8;
        payloadRefill <<= availLen - refillBits;
        cont |= payloadRefill;

        payload += refillBits;

        return true;
    }

    public void FwdBits(int vbitLen)
    {
        cont <<= vbitLen;
        payload -= vbitLen;
    }

    public bool PeekBits(int vbitLen, out ushort val)
    {
        val = 0;
        int shft = 64 - vbitLen;

        if (payload < vbitLen && !Refill(vbitLen)) return false;

        val = (ushort)(cont >> shft);

        return true;
    }

    public bool GetBits(int vbitLen, out ushort val)
    {
        val = 0;
        int shft = 64 - vbitLen;

        if (payload < vbitLen && !Refill(vbitLen)) return false;

        val = (ushort)(cont >> shft);

        cont <<= vbitLen;
        payload -= vbitLen;

        return true;
    }

    /// <summary>
    /// For JPEG DCT compressed data
    /// ... 000 001 010 011   100 101 110 111 ...
    ///     -7  -6  -5  -4    4   5   6   7
    /// </summary>
    public bool GetDCTValue(int vbitLen, out short val)
    {
        val = 0;
        int shft = 64 - vbitLen;

        if (payload < vbitLen && !Refill(vbitLen)) return false;

        int maxv = (1 << vbitLen) - 1;
        var neg = (cont >> 63) == 0;
        val = neg ? (short)((int)(cont >> shft) - maxv) : (short)(cont >> shft);

        cont <<= vbitLen;
        payload -= vbitLen;

        return true;
    }
}

public static class ArraySegmentExtensions
{
    public static string SneakPeek(this ArraySegment<byte> segm, int depth = 4)
    {
        var beg = segm.Offset;
        var len = segm.Count;
        var end = beg + len;
        var arr = segm.Array;
        var outp = arr == null ? "" : len <= 2 * depth ? BitConverter.ToString(arr, beg, len) :
        BitConverter.ToString(arr, beg, depth) + " .. " + BitConverter.ToString(arr, end - depth, depth);
        return outp.Replace("-", " ");
    }
}

public static class ByteArrayExtensions
{
    public static string ToBinString(this byte[] bytes, string fmt = " ") =>
        string.Join("", bytes.Select(b => Convert.ToString(b, 2).PadLeft(8,'0') + fmt)!);

    public static string ToHexString(this byte[] bytes, string fmt = " ")
    {
        string hex = BitConverter.ToString(bytes);
        return hex.Replace("-", fmt);
    }

    public static string SneakPeek(this byte[] arr, int depth = 4)
    {
        var len = arr.Length;
        var outp = arr == null ? "" : len <= 2 * depth ? BitConverter.ToString(arr, 0, len) :
        BitConverter.ToString(arr, 0, depth) + " .. " + BitConverter.ToString(arr, len - depth, depth);
        return outp.Replace("-", " ");
    }

    public static byte[] Slice(this byte[] arr, int start, int length)
    {
        var slice = new byte[length];
        Buffer.BlockCopy(arr, start, slice, 0, length);
        return slice;
    }

    public static string ToText(this byte[] bytes) =>
        string.Concat(bytes.TakeWhile(b => b != 0).Select(b => (char)b));
}

public static class IntExtensions
{
    static readonly bool isLE = BitConverter.IsLittleEndian;

    /// <summary>
    /// BitConverter.GetBytes always reads memory low -> high;
    /// </summary>
    /// 
    public static byte[] BytesLeftToRight(this short val)
    {
        short memLayoutBE = isLE ? BinaryPrimitives.ReverseEndianness(val) : val;
        return BitConverter.GetBytes(memLayoutBE);
    }

    public static byte[] BytesLeftToRight(this int val)
    {
        int memLayoutBE = isLE ? BinaryPrimitives.ReverseEndianness(val) : val;
        return BitConverter.GetBytes(memLayoutBE);
    }

    public static byte[] BytesLeftToRight(this long val)
    {
        long memLayoutBE = isLE ? BinaryPrimitives.ReverseEndianness(val) : val;
        return BitConverter.GetBytes(memLayoutBE);
    }

    public static byte[] BytesLeftToRight(this ulong val)
    {
        ulong memLayoutBE = isLE ? BinaryPrimitives.ReverseEndianness(val) : val;
        return BitConverter.GetBytes(memLayoutBE);
    }

    public static byte[] BytesRightToLeft(this short val)
    {
        short memLayoutLE = isLE ? val : BinaryPrimitives.ReverseEndianness(val);
        return BitConverter.GetBytes(memLayoutLE);
    }

    public static byte[] BytesRightToLeft(this int val)
    {
        int memLayoutLE = isLE ? val : BinaryPrimitives.ReverseEndianness(val);
        return BitConverter.GetBytes(memLayoutLE);
    }

    public static byte[] BytesRightToLeft(this long val)
    {
        long memLayoutLE = isLE ? val : BinaryPrimitives.ReverseEndianness(val);
        return BitConverter.GetBytes(memLayoutLE);
    }
    public static byte[] BytesRightToLeft(this ulong val)
    {
        ulong memLayoutLE = isLE ? val : BinaryPrimitives.ReverseEndianness(val);
        return BitConverter.GetBytes(memLayoutLE);
    }

    public static string MemHexStr(this int val, string fmt = " ")
    {
        var bytes = BitConverter.GetBytes(val);
        string hex = BitConverter.ToString(bytes);
        return hex.Replace("-", fmt);
    }

    public static string HexStr(this int val, string fmt = "")
    {
        int memLayoutBE = isLE ? BinaryPrimitives.ReverseEndianness(val) : val;
        var bytes = BitConverter.GetBytes(memLayoutBE);
        var nzBytes = bytes.SkipWhile(b => b == 0x00).ToArray();
        string hex = BitConverter.ToString(nzBytes);
        return hex.Replace("-", fmt);
    }

    public static string ToText(this int val)
    {
        char[] arr = val.BytesLeftToRight().Select(b => (char)b).ToArray();
        return string.Concat(arr);
    }
}

public class Utils
{
    public static T[,] ScaleArray<T>(T[,] array, int scaleX, int scaleY)
    {
        int rows = array.GetLength(0);
        int cols = array.GetLength(1);

        int newRows = rows * scaleY;
        int newCols = cols * scaleX;

        T[,] newArray = new T[newRows, newCols];

        for (int r = 0; r < newRows; r++)
        {
            var mapR = r / scaleY;
            for (int c = 0; c < newCols; c++)
                newArray[r, c] = array[mapR, c / scaleX];
        }

        return newArray;
    }

    public static string TableToStr<T>(T[,] table, int space = 4, int margin = 0)
    {
        string marStr = new(' ', margin);
        string tInfo = "";
        var height = table.GetLength(0);
        var width = table.GetLength(1);
        for (int k = 0; k < height; k++)
        {
            tInfo += marStr;
            for (int j = 0; j < width; j++)
            {
                T tkj = table[k, j];
                if (tkj == null) tInfo += "null";
                else tInfo += tkj.ToString()!.PadLeft(space, ' ');
            }
            tInfo += "\n";
        }
        return tInfo;
    }

    static public void PrintBytes(byte[] bytes, string fmt = " ")
    {
        string hex = BitConverter.ToString(bytes);
        Console.WriteLine(hex.Replace("-", fmt));
    }

    static public string IntBitsToEnums(int val, Type enmType)
    {
        List<string> outp = [];
        int mult = 1;

        for (int i = 0; i < 8; i++)
        {
            int lsb = val & 0x00_00_00_01;

            if (lsb == 1)
            {
                string? name = Enum.GetName(enmType, lsb * mult);
                if (name != null) outp.Add(name);
            }
            val >>= 1;
            mult *= 2;
        }

        return string.Join(" | ", outp);
    }

    /// <summary>
    /// Can compute CRC[1..55], depending on hardcoded polynomial;<br/>
    /// uses 64 bit ulong as a container<br/>
    /// zero initial CRC, Big Endian data order
    /// </summary>
    /// <param name="dsor">divisor polymonial loaded into highest bits of ulong; <br/>
    /// for CRC32: x32 + x26 + x23 + x22 + x16 + x12 + x11 + x10 + x8 + x7 + x5 + x4 + x2 + x1 + x0
    /// </param>
    /// <param name="dsorLen">actual bit-length of dsor; N + 1 for CRC[N]</param>
    /// <param name="divend">container holding up to 8 bytes of data being processed</param>
    /// <param name="payload">the number of data bits currently in divend, counting from msb</param>
    static public ulong CRC(byte[] data)
    {
        ulong divend = 0;
        // CRC32
        ulong dsor = 0b10000010_01100000_10001110_11011011_10000000_00000000_00000000_00000000;
        int dsorLen = 33;

        // CRC3
        //ulong dsor = 0b10110000_00000000_00000000_00000000_00000000_00000000_00000000_00000000;
        //int dsorLen = 4;

        int len = data.Length;

        // initial load
        int bytesLoaded = Math.Min(8, len);
        if (bytesLoaded < 8)
        {
            for (int i = 0; i < len; i++)
            {
                divend <<= 8;
                divend |= data[i];
            }
            divend <<= 64 - len * 8;
        } else
        {
            var sp = new ReadOnlySpan<byte>(data, 0, bytesLoaded);
            divend = BinaryPrimitives.ReadUInt64BigEndian(sp);
        }

        int payload = bytesLoaded * 8;

        while (true)
        {
            //PrintDivendBlocks(divend, payload, "div");

            int leadZeros = BitOperations.LeadingZeroCount(divend);

            int bytesToLoad = len - bytesLoaded;
            bool zeroPayload = leadZeros >= payload;

            if (bytesToLoad == 0 && zeroPayload) break;

            if (zeroPayload)
            {
                payload = 0;
            } else
            {
                payload -= leadZeros;
                divend <<= leadZeros;

                //PrintDivendBlocks(divend, payload, "zrs");
            }

            if (payload < dsorLen && bytesToLoad != 0)
            {
                var availPayloadLen = 64 - payload;
                var wholeBytes = Math.Min(availPayloadLen / 8, bytesToLoad);

                ulong newPayload = 0;
                for (int i = 0; i < wholeBytes; i++)
                {
                    newPayload <<= 8;
                    newPayload |= data[bytesLoaded++];
                }
                var actPayloadLen = wholeBytes * 8;
                newPayload <<= availPayloadLen - actPayloadLen;
                divend |= newPayload;

                payload += actPayloadLen;

                //Log("tal " + Convert.ToString((long)divend, 2).PadLeft(64, '0'));
            }

            divend ^= dsor;

            //Log("dsr " + Convert.ToString((long)dsor, 2).PadLeft(64, '0'));

        }

        //Log("fin " + Convert.ToString((long)divend, 2).PadLeft(64, '0'));

        return divend >> (64 - payload - (dsorLen - 1));
    }

    static public byte[] ReadFileBytes(string path, string name)
    {
        try
        {
            return File.ReadAllBytes(Path.Combine(path, name));
        }
        catch (Exception ex)
        {
            Log($"Utils.ReadFile : exception '{ex.Message}'");
            return [];
        }
    }

    static public void WriteFileBytes(string path, string name, byte[][] outp)
    {
        try
        {
            using var fileStream = new FileStream(Path.Combine(path, name), FileMode.Create);
            using var writer = new BinaryWriter(fileStream);

            foreach (var byteArr in outp)
                writer.Write(byteArr);
        }
        catch (Exception ex)
        {
            Log($"Utils.WriteAllBytes : exception '{ex.Message}'");
        }
    }

    static public void WriteFileBytes(string path, string name, byte[] outp)
    {
        try
        {
            File.WriteAllBytes(Path.Combine(path, name), outp);
        }
        catch (Exception ex)
        {
            Log($"Utils.WriteAllBytes : exception '{ex.Message}'");
        }
    }

    public enum LogMode
    {
        Console,
        Debug,
    }

    static readonly LogMode logMode = LogMode.Console;
    static Stopwatch? stopwatch;
    static public void LogWithTime(string log)
    {
        stopwatch ??= Stopwatch.StartNew();
        double elapsedSec = stopwatch.Elapsed.TotalSeconds;
        switch (logMode)
        {
            case LogMode.Console:
                Console.WriteLine(elapsedSec.ToString("0.00000") + " " + log);
                break;
            case LogMode.Debug:
                Debug.WriteLine(elapsedSec.ToString("0.00000") + " " + log);
                break;
            default:
                throw new NotImplementedException($"Utils.Log : logMode '{logMode}' is not supported");
        }

    }

    static public void Log(string log)
    {
        switch (logMode)
        {
            case LogMode.Console:
                Console.WriteLine(log);
                break;
            case LogMode.Debug:
                Debug.WriteLine(log);
                break;
            default:
                throw new NotImplementedException($"Utils.Log : logMode '{logMode}' is not supported");
        }

    }

    static public void PrintLongFmt(ulong res)
    {
        var strRes = Convert.ToString((long)res, 2).PadLeft(32, '0');
        var prn = string.Concat(strRes.Select((ch, i) => (i + 1) % 8 == 0 ? ch + " " : ch.ToString()));
        Log(prn);
    }

    static public void PrintDivendBlocks(ulong divend, int payload, string pref)
    {
        Console.Write(pref + " ");
        var clr = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Green;
        string divstr = Convert.ToString((long)divend, 2).PadLeft(64, '0');
        Console.Write(divstr[0..payload]);
        Console.ForegroundColor = clr;
        Console.WriteLine(divstr[payload..]);
    }
}


