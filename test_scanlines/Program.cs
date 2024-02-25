
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;

namespace test;

class Program
{
    public static void Main(string[] args)
    {
        //TestByteOrder();

        //TestChunks();

        TestDecodeScanlines();
    }

    struct _hdrCh
    {
        internal int bitDepth;
        internal int width;
        internal int height;
    }
    static void TestDecodeScanlines()
    {
        var path = "../../../testImages";
        var fname = "rgb_3x3.png";
        //byte[] fileBuffer = Utils.ReadFileBytes(path, fname);
        //var segm = new ArraySegment<byte>(fileBuffer, fileBuffer.Length - 54, 0x26);
        //Console.WriteLine("0x" + segm.HexStr(", 0x"));

        byte[] comprData = new byte[] {
        //0x78, 0xDA, 
        0x62, 0xFA, 0xCF, 0xC0, 0xC0, 0x00,
        0xC6, 0x4C, 0x0C, 0x33, 0x19, 0x18, 0xCE, 0xA6,
        0xCD, 0x64, 0x38, 0xCB, 0x74, 0xD6, 0x98, 0x81,
        0x61, 0x16, 0x43, 0x7A, 0xDA, 0x59, 0x80, 0x00,
        0x03, 0x00, 
        //0x86, 0xD5, 0x09, 0x6A
        };

        byte[] decomprData;
        decomprData = Decompress(comprData);

        var decompDataStream = new MemoryStream();
        decompDataStream.Write(decomprData, 0, decomprData.Length);

        var hdrCh = new _hdrCh { bitDepth = 8, width = 3, height = 3 };

        //var cType = (ColorType)hdrCh.cType;
        var numChan = 3;// channels[cType];
        var bitsPerPixel = numChan * hdrCh.bitDepth;
        var scanlineBitLen = bitsPerPixel * hdrCh.width;
        var wholeBytes = scanlineBitLen / 8;
        int scanlineBytelen = scanlineBitLen % 8 == 0 ? wholeBytes : wholeBytes + 1;

        byte[] pixelData = new byte[scanlineBytelen * hdrCh.height];

        DecodeScanlines(
            decompDataStream.GetBuffer(),
            pixelData,
            scanlineBytelen + 1,
            hdrCh.height);

        byte[][] xData = new byte[][]
        {
            hdrCh.width.BytesRightToLeft(),
            hdrCh.height.BytesRightToLeft(),
            new byte[] { (byte)numChan, (byte)hdrCh.bitDepth, 0, 0 },
            pixelData
        };

        Utils.WriteFileBytes(path, fname + ".xdat", xData);
    }

    static byte[] Decompress(ArraySegment<byte> _data)
    {
        if (_data.Array == null) return Array.Empty<byte>();
        using var iStream = new MemoryStream();
        using var oStream = new MemoryStream();
        iStream.Write(_data.Array, _data.Offset, _data.Count);
        iStream.Position = 0;
        using var decompressor = new DeflateStream(iStream, CompressionMode.Decompress);
        decompressor.CopyTo(oStream);
        return oStream.ToArray();
    }

    public static void TestChunks()
    {
        byte[] data = new byte[] { 
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 
            0x08, 0x02, 0x00, 0x00, 0x00 
        };

        ArraySegment<byte> segm1 = new(data, 0, 13);
        ArraySegment<byte> segm2 = new(data, 2, 4);
        ArraySegment<byte> segm3 = new(data, 4, 4);

        int crc = 0x11223344;

        //List<Chunk> chunks = new() {
        //    Chunk.Create(0x49484452, segm1, crc),
        //    Chunk.Create(0x49444154, segm2, crc),
        //    Chunk.Create(0x49454E44, segm3, crc),
        //    Chunk.Create(0x4, segm1, crc),
        //};

        //Console.WriteLine();
        //foreach (var ch in chunks) Console.WriteLine(ch.ToString());
    }

    public static void TestByteOrder()
    {
        int val = 0x01020304;

        var strRes = Convert.ToString(val, 16).PadLeft(8, '0');
        var prn = string.Concat(strRes.Select((ch, i) => i % 2 == 0 ? "0x" + ch : ch + " "));

        Console.WriteLine(prn);
        Console.WriteLine("----");

        var bytesLR = val.BytesLeftToRight();
        var bytesRL = val.BytesRightToLeft();

        Console.WriteLine(string.Concat(bytesLR.Select(b => $"0x{b:X2} ")) + " LR");
        Console.WriteLine(string.Concat(bytesRL.Select(b => $"0x{b:X2} ")) + " RL");
    }

    static void DecodeScanlines(
        byte[] buffer,
        byte[] pixelData,
        int lineLen, // includes 1 byte of filter type
        int height)
    {
        string msg = "";

        int row = 0;
        int off = 0;
        int pdOff = 0;
        int lineLenm1 = lineLen - 1;
        bool decoded = true;
        while(row < height && decoded)
        {
            decoded = ByteFilter.DecodeScanline(
                new ArraySegment<byte>(buffer, off, lineLen),
                pixelData,
                pdOff, // offset inside pixelData
                out msg);

            off += lineLen;
            pdOff += lineLenm1;
            row++;
        }

        if (!decoded) throw new InvalidDataException(
            $"PNG.DecodeScanlines : decoding row {row} failed",
            new Exception(msg));
    }

}

class ByteFilter
{
    public enum BFType
    {
        None = 0,
        Sub = 1,
        Up = 2,
        Average = 3,
        Paeth = 4,
    }

    static byte PaethPredictor(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        return (byte)(pa <= pb && pa <= pc ? a : (pb <= pc ? b : c));
    }

    /// <summary>
    /// scanline filters
    /// 0 None Filt(x) = Orig(x)   Recon(x) = Filt(x)
    /// 1 Sub Filt(x) = Orig(x) - Orig(a) Recon(x) = Filt(x) + Recon(a)
    /// 2 Up Filt(x) = Orig(x) - Orig(b) Recon(x) = Filt(x) + Recon(b)
    /// 3 Average Filt(x) = Orig(x) - floor((Orig(a) + Orig(b)) / 2)	Recon(x) = Filt(x) + floor((Recon(a) + Recon(b)) / 2)
    /// 4 Paeth Filt(x) = Orig(x) - PaethPredictor(Orig(a), Orig(b), Orig(c))   Recon(x) = Filt(x) + PaethPredictor(Recon(a), Recon(b), Recon(c))
    /// </summary>
    /// <param name="args">[(0)x - current byte, a(1) - left byte, b(2) - up byte, c(3) - up left byte]</param>
    /// <returns></returns>
    static public byte Encode(BFType type, params int[] args)
    {
        return type switch
        {
            BFType.None => (byte)args[0],
            BFType.Sub => (byte)(args[0] - args[1]),
            BFType.Up => (byte)(args[0] - args[2]),
            BFType.Average => (byte)(args[0] - (args[1] + args[2]) / 2),
            BFType.Paeth => (byte)(args[0] - PaethPredictor(args[1], args[2], args[3])),
            _ => 0,
        };
    }

    static public byte Decode(BFType type, params int[] args)
    {
        return type switch
        {
            BFType.None => (byte)args[0],
            BFType.Sub => (byte)(args[0] + args[1]),
            BFType.Up => (byte)(args[0] + args[2]),
            BFType.Average => (byte)(args[0] + (args[1] + args[2]) / 2),
            BFType.Paeth => (byte)(args[0] + PaethPredictor(args[1], args[2], args[3])),
            _ => 0,
        };
    }

    static public bool DecodeScanline(
        ArraySegment<byte> line, 
        byte[] pixelData, 
        int pdOff, // offset inside pixelData
        out string msg)
    {
        msg = "";
        int filter = line[0];
        if (!Enum.IsDefined(typeof(BFType), filter))
        {
            msg = $"ByteFilter.DecodeScanline : filter '{filter}' not recognized";
            return false;
        }
        BFType ftype = (BFType)filter;

        byte[] lines = line.Array!;
        int lnOff = line.Offset;
        int len = line.Count;
        int upOff;
        switch (ftype)
        {
            case BFType.None:
                Array.Copy(lines, lnOff + 1, pixelData, pdOff, len - 1);
                break;

            case BFType.Sub:
                pixelData[pdOff] = line[1];
                for(int pos = 2; pos < len; pos++)
                {
                    int posm1 = pos - 1;
                    pixelData[pdOff + posm1] = (byte)(line[pos] + line[posm1]);
                }
                break;

            case BFType.Up:
                if(lnOff == 0) // topmost scanline
                {
                    Array.Copy(lines, lnOff + 1, pixelData, pdOff, len - 1);
                    break;
                }
                upOff = lnOff - len;
                for (int pos = 1; pos < len; pos++)
                {
                    pixelData[pdOff + pos - 1] = (byte)(line[pos] + lines[upOff + pos]);
                }
                break;

            case BFType.Average:
                if (lnOff == 0) // topmost scanline
                {
                    pixelData[pdOff] = line[1];
                    for (int pos = 2; pos < len; pos++)
                    {
                        int posm1 = pos - 1;
                        pixelData[pdOff + posm1] = (byte)(line[pos] + line[posm1] / 2);
                    }
                    break;
                }
                upOff = lnOff - len;
                pixelData[pdOff] = (byte)(line[1] + lines[upOff + 1] / 2);
                for (int pos = 2; pos < len; pos++)
                {
                    int posm1 = pos - 1;
                    pixelData[pdOff + posm1] = (byte)(line[pos] + (line[posm1] + lines[upOff + pos]) / 2);
                }
                break;

            case BFType.Paeth:
                if (lnOff == 0) // topmost scanline = Sub
                {
                    pixelData[pdOff] = line[1];
                    for (int pos = 2; pos < len; pos++)
                    {
                        int posm1 = pos - 1;
                        pixelData[pdOff + posm1] = (byte)(line[pos] + line[posm1]);
                    }
                    break;
                }
                upOff = lnOff - len;
                pixelData[pdOff] = (byte)(line[1] + lines[upOff + 1]);
                for (int pos = 2; pos < len; pos++)
                {
                    int posm1 = pos - 1;
                    pixelData[pdOff + posm1] = (byte)(line[pos] + PaethPredictor(line[posm1], lines[upOff + pos], lines[upOff + posm1]));
                }
                break;

            default:
                break;
        };

        return true;
    }
}

public static class IntExtensions
{
    static readonly bool isLE = false;// BitConverter.IsLittleEndian;

    /// <summary>
    /// BitConverter.GetBytes always reads memory low -> high;
    /// </summary>
    public static byte[] BytesLeftToRight(this int val)
    {
        int memLayoutBE = isLE ? BinaryPrimitives.ReverseEndianness(val) : val;
        return BitConverter.GetBytes(memLayoutBE);
    }

    public static byte[] BytesRightToLeft(this int val)
    {
        int memLayoutLE = isLE ? val : BinaryPrimitives.ReverseEndianness(val);
        return BitConverter.GetBytes(memLayoutLE);
    }
}

class Utils
{
    static public byte[] ReadFileBytes(string path, string name)
    {
        try
        {
            return File.ReadAllBytes(Path.Combine(path, name));
        }
        catch (Exception ex)
        {
            Log($"Utils.ReadFile : exception '{ex.Message}'");
            return Array.Empty<byte>();
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
}

public static class ArraySegmentExtensions
{
    public static string HexStr(this ArraySegment<byte> bytes, string fmt = " ")
    {
        var arr = bytes.Array;
        string hex = BitConverter.ToString(arr!, bytes.Offset, bytes.Count);
        return hex.Replace("-", fmt);
    }
}
