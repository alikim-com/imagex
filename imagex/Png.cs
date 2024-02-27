// official format doc
// https://www.w3.org/TR/png/

using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Hashing;

namespace imagex;

/// <summary>
/// Designed for high level storage and chunk manipulation
/// Use ToXXX() methods to extract pixel data and convert to other formats
/// </summary>
public class Png(
    Png.ColorType _cType,
    int _bitDepth,
    int _width,
    int _height,
    List<Png.Chunk> _chList) : Image(Format.Png, _width, _height)
{
    static readonly ulong SIG = 0x89504E470D0A1A0A;

    static readonly bool isLE = BitConverter.IsLittleEndian;

    public enum CompressionMethod
    {
        Deflate = 0
    }

    public enum FilterMethod
    {
        Adaptive = 0
    }

    public enum ColorType
    {
        Greyscale = 0,
        Truecolour = 2,
        IndexedColour = 3,
        GreyscaleWithAlpha = 4,
        TruecolourWithAlpha = 6,
    }

    static readonly Dictionary<ColorType, int> channels = new() {
        { ColorType.Greyscale, 1 },
        { ColorType.Truecolour, 3 },
        { ColorType.IndexedColour, 1 },
        { ColorType.GreyscaleWithAlpha, 2 },
        { ColorType.TruecolourWithAlpha, 4 },
    };

    static readonly Dictionary<ColorType, int[]> allowedBitDepths = new()
    {
        // each pixel is a greyscale sample
        { ColorType.Greyscale, new int[] {1, 2, 4, 8, 16 } },
        // each pixel is an R,G,B triple
        { ColorType.Truecolour, new int[] {8, 16} },
        // each pixel is a palette index; a PLTE chunk shall appear
        { ColorType.IndexedColour, new int[]  {1, 2, 4, 8} },  
        // each pixel is a greyscale sample followed by an alpha sample
        { ColorType.GreyscaleWithAlpha, new int[]  {8, 16} },
        // each pixel is an R,G,B triple followed by an alpha sample
        { ColorType.TruecolourWithAlpha, new int[] {8, 16 } },
    };

    public readonly ColorType cType = _cType;
    public readonly int bitDepth = _bitDepth;
    public readonly List<Chunk> chList = _chList;

    static bool ValidateFormat(IHDRChunk hdrCh, out string msg)
    {
        msg = "";

        if ((CompressionMethod)hdrCh.compression != CompressionMethod.Deflate ||
            (FilterMethod)hdrCh.filter != FilterMethod.Adaptive)
        {
            msg = "Wrong compression/filter value";
            return false;
        }

        if (hdrCh.width <= 0 || hdrCh.height <= 0)
        {
            msg = "Bad width/height";
            return false;
        }

        if (hdrCh.interlaced != 0)
        {
            msg = "Interlace data is not supported";
            return false;
        }

        if (!Enum.IsDefined(typeof(ColorType), hdrCh.cType))
        {
            msg = $"Bad color type '{hdrCh.cType}'";
            return false;
        }

        var cType = (ColorType)hdrCh.cType;

        if (!allowedBitDepths[cType].Contains(hdrCh.bitDepth))
        {
            msg = $"Png.Ctor : depth value '{hdrCh.bitDepth}' is not supported for '{cType}'";
            return false;
        }

        return true;
    }

    public static Png FromFile(string path, string fname)
    {
        var data = Utils.ReadFileBytes(path, fname);
        var len = data.Length;

        var offset = 0;

        var sp = new ReadOnlySpan<byte>(data, offset, 8);
        var sig = BinaryPrimitives.ReadUInt64BigEndian(sp);
        if (sig != SIG) throw new Exception
                ($"Png.FromFile : bad file signature '{sig:X}'");

        // find chunks
        offset = 8;
        List<Chunk> chList = [];
        while (offset < len)
        {
            var spLen = new ReadOnlySpan<byte>(data, offset, 4);
            var chLen = BinaryPrimitives.ReadInt32BigEndian(spLen);

            var spType = new ReadOnlySpan<byte>(data, offset + 4, 4);
            var chType = BinaryPrimitives.ReadInt32BigEndian(spType);

            var spCrc = new ReadOnlySpan<byte>(data, offset + 8 + chLen, 4);
            var crc = BinaryPrimitives.ReadInt32BigEndian(spCrc);

            var chunkData = new ArraySegment<byte>(data, offset + 8, chLen);

            chList.Add(Chunk.Create(chType, chunkData, crc));

            offset += 12 + chLen;
        }

        if (chList[0] is not IHDRChunk hdrCh) throw new InvalidDataException
                ($"Png.FromFile : file '{Path.Combine(path, fname)}' first chunk is not header");

        if (!ValidateFormat(hdrCh, out string msg)) throw new InvalidDataException
                ($"Png.FromFile : file '{Path.Combine(path, fname)}' header invalid", new Exception(msg));

        return new Png((ColorType)hdrCh.cType, hdrCh.bitDepth, hdrCh.width, hdrCh.height, chList);
    }

    public static Xdat ToXdat(Png png, bool verbose = true)
    {
        var decompDataStream = new MemoryStream();

        foreach (var chunk in png.chList)
            if (chunk is IDATChunk datCh)
                decompDataStream.Write(IDATChunk.Decompress(datCh.comprData));

        if (png.chList[0] is not IHDRChunk hdrCh) throw new InvalidDataException
        ($"Png.ToXdat : first chunk is not header");

        var cType = (ColorType)hdrCh.cType;
        var numChan = channels[cType];
        var bitsPerPixel = numChan * hdrCh.bitDepth;
        var scanlineBitLen = bitsPerPixel * hdrCh.width;
        var wholeBytes = scanlineBitLen / 8;
        int scanlineBytelen = scanlineBitLen % 8 == 0 ? wholeBytes : wholeBytes + 1;

        byte[] pixelData = new byte[scanlineBytelen * hdrCh.height];

        Array.Clear(filtersFound, 0, 5);

        DecodeScanlines(
            decompDataStream.GetBuffer(),
            pixelData,
            scanlineBytelen + 1,
            hdrCh.height);

        if (verbose)
        {
            Console.WriteLine(
           $"""
            filter stats:
            None {filtersFound[0]},
            Sub {filtersFound[1]},
            Up {filtersFound[2]},
            Average {filtersFound[3]},
            Paeth {filtersFound[4]},
            -----
            total scanlines {filtersFound.Sum()}

            """);
        }

        return new Xdat(
            cType,
            hdrCh.bitDepth,
            numChan,
            hdrCh.width,
            hdrCh.height,
            pixelData);
    }

    static readonly int[] filtersFound = new int[5];
    static void DecodeScanlines(
        byte[] buffer,
        byte[] pixelData,
        int lineLen, // includes 1 byte of filter type
        int height)
    {

        // Utils.PrintBytes(buffer);
        // Console.WriteLine("---");

        int row = 0;
        int off = 1;
        int pdOff = 0;
        int lineLenm1 = lineLen - 1;

        bool decoded = ByteFilter.DecodeFirstScanline(
            new ArraySegment<byte>(buffer, off, lineLenm1),
            pixelData,
            out string msg);

        if (!decoded) throw new InvalidDataException(
            $"Png.DecodeScanlines : decoding row {row} failed",
            new Exception(msg));

        while (row < height - 1 && decoded)
        {
            off += lineLen;
            pdOff += lineLenm1;
            row++;

            decoded = ByteFilter.DecodeScanline(
                new ArraySegment<byte>(buffer, off, lineLenm1),
                pixelData,
                pdOff, // offset inside pixelData
                out msg);
        }

        if (!decoded) throw new InvalidDataException(
            $"Png.DecodeScanlines : decoding row {row} failed",
            new Exception(msg));

        // Utils.PrintBytes(pixelData);
        // Console.WriteLine("---");
    }

    public class NoneChunk(Chunk.ChType _type, int _status, ArraySegment<byte> _data, int _crc) :
        Chunk(_type, _status, _data, _crc)
    { }

    public class IHDRChunk(Chunk.ChType _type, int _status, ArraySegment<byte> _data, int _crc) : Chunk(_type, _status, _data, _crc)
    {
        public readonly int width = BinaryPrimitives.ReadInt32BigEndian(_data);
        public readonly int height = BinaryPrimitives.ReadInt32BigEndian(_data.Slice(4));
        public readonly byte bitDepth = _data[8];
        public readonly int cType = _data[9];
        public readonly byte compression = _data[10]; // 0 - deflate image compression 
        public readonly byte filter = _data[11]; // 0 - adaptive with 5 byte filters
        public readonly byte interlaced = _data[12];

        protected override string ParsedData() =>
        $"""
          width: {width}
          height: {height}
          bitDepth: {bitDepth}
          cType: {cType} {(ColorType)cType}
          compression: {compression}
          filter: {filter}
          interlaced: {interlaced}

        """;
    }

    public class IDATChunk : Chunk
    {
        // https://datatracker.ietf.org/doc/html/rfc1950#page-4
        readonly byte compression; // chunk compression, 8 - deflate
        readonly int windowSize;  // for deflate only
        readonly byte fcheck;
        readonly bool fdict;
        readonly byte flevel;
        readonly int presetDict;
        public readonly ArraySegment<byte> comprData;
        readonly int Adler32Checksum;

        public IDATChunk(ChType _type, int _status, ArraySegment<byte> _data, int _crc) :
            base(_type, _status, _data, _crc)
        {
            compression = (byte)(_data[0] & 0b0000_1111);
            windowSize = 2 << ((_data[0] >> 4) + 8 - 1); // pow(2, n + 8)
            fcheck = (byte)(_data[1] & 0b0001_1111);
            fdict = ((_data[1] & 0b0010_0000) >> 5) != 0;
            flevel = (byte)((_data[1] & 0b1100_0000) >> 6);

            if (fdict) presetDict = BinaryPrimitives.ReadInt32BigEndian(_data.Slice(2));
            // [1,1,data,4] = _data.Count
            comprData = _data.Slice(fdict ? 6 : 2, _data.Count - 6);
            Adler32Checksum = BinaryPrimitives.ReadInt32BigEndian(_data.Slice(_data.Count - 4));
        }

        static public byte[] Decompress(ArraySegment<byte> _data)
        {
            if (_data.Array == null) return [];
            using var iStream = new MemoryStream();
            using var oStream = new MemoryStream();
            iStream.Write(_data.Array, _data.Offset, _data.Count);
            iStream.Position = 0;
            using var decompressor = new DeflateStream(iStream, CompressionMode.Decompress);
            decompressor.CopyTo(oStream);
            return oStream.ToArray();
        }

        protected override string ParsedData()
        {
            var dict = !fdict ? "none" : $"{presetDict:X8}";

            return
            $"""
              compression: {compression}
              windowSize: {windowSize}
              compr.level: {flevel}
              fcheck: {fcheck}
              dictionary: {dict}
              compr.data: {comprData.SneakPeek()}
              checksum: {Adler32Checksum:X8}

            """;
        }
    }

    public class IENDChunk(Chunk.ChType _type, int _status, ArraySegment<byte> _data, int _crc) :
        Chunk(_type, _status, _data, _crc)
    { }

    public abstract class Chunk
    {
        public enum ChType
        {
            None = 0,
            IHDR = 0x49484452,
            IDAT = 0x49444154,
            IEND = 0x49454E44,
        }

        static readonly Dictionary<ChType, Type> classMap = new() {
        {ChType.None, typeof(NoneChunk)},
        {ChType.IHDR, typeof(IHDRChunk)},
        {ChType.IDAT, typeof(IDATChunk)},
        {ChType.IEND, typeof(IENDChunk)},
    };

        public enum Status
        {
            None = 0,
            OK = 1,
            TypeNotSupported = 2,
            CRC32Mismatch = 4,
        }

        public readonly ChType type;
        readonly ArraySegment<byte> data;
        readonly int crc;
        readonly int status;

        static public Chunk Create(int _type, ArraySegment<byte> _data, int _crc)
        {
            ChType type;
            int status = (int)Status.None;

            if (!Enum.IsDefined(typeof(ChType), _type))
            {
                type = ChType.None;
                Utils.Log($"Chunk.Create : chunk type '{_type:X}({_type.ToText()})' not supported");
                status |= (int)Status.TypeNotSupported;
            } else
            {
                type = (ChType)_type;
            }

            Type clsType = classMap[type];

            var obj = Activator.CreateInstance(clsType, new object[] { type, status, _data, _crc });

            return obj == null ? throw new Exception
                ($"Chunk.Create : couldn't create instance of type '{clsType}'") : (Chunk)obj;
        }

        public Chunk(ChType _chType, int _status, ArraySegment<byte> _data, int _crc)
        {
            type = _chType;
            status = _status;
            data = _data;
            crc = _crc;
            status |= (int)CheckCrc();

            if (status == (int)Status.None) status = (int)Status.OK;
        }

        Status CheckCrc()
        {
            var crc = new Crc32();
            crc.Append(((int)type).BytesLeftToRight());
            crc.Append(data);

            var chksumBE = crc.GetCurrentHash(); // always BE

            var status = chksumBE.SequenceEqual(this.crc.BytesRightToLeft()) ? Status.None : Status.CRC32Mismatch;

            return status;
        }

        virtual protected string ParsedData() => "";

        public override string ToString()
        {
            var raw = data.SneakPeek();
            var parsedData = ParsedData();
            var parsedDataStr = parsedData == "" ? "" :
                $"""
                parsed data
                {parsedData}
                """;

            return
            $"""
            type: {type}
            length: {data.Count}
            raw data: {raw}
            crc: {crc:X8}
            status: {Utils.IntBitsToEnums(status, typeof(Status))}
            {parsedDataStr}
            """;
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
        /// 0 None Filt(x) = Orig(x)   
        ///        Recon(x) = Filt(x)
        ///        
        /// 1 Sub  Filt(x) = Orig(x) - Orig(a) 
        ///        Recon(x) = Filt(x) + Recon(a)
        ///        
        /// 2 Up   Filt(x) = Orig(x) - Orig(b) 
        ///        Recon(x) = Filt(x) + Recon(b)
        ///        
        /// 3 Average Filt(x) = Orig(x) - floor((Orig(a) + Orig(b)) / 2)
        ///           Recon(x) = Filt(x) + floor((Recon(a) + Recon(b)) / 2)
        ///           
        /// 4 Paeth   Filt(x) = Orig(x) - PaethPredictor(Orig(a), Orig(b), Orig(c))
        ///           Recon(x) = Filt(x) + PaethPredictor(Recon(a), Recon(b), Recon(c))
        /// </summary>
        /// <param name="args">
        /// [(0)x - current byte, a(1) - left byte, b(2) - up byte, c(3) - up left byte]
        /// </param>
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

        static public bool DecodeFirstScanline(
        ArraySegment<byte> line,
        byte[] pixelData,
        out string msg)
        {
            //Console.WriteLine(line.HexStr());
            //Console.WriteLine("---");

            byte[] lines = line.Array!;
            int lnOff = line.Offset;
            int len = line.Count;

            msg = "";
            int filter = lines[lnOff - 1];
            filtersFound[filter]++;
            if (!Enum.IsDefined(typeof(BFType), filter))
            {
                msg = $"ByteFilter.DecodeFirstScanline : filter '{filter}' not recognized";
                return false;
            }
            BFType ftype = (BFType)filter;

            switch (ftype)
            {
                case BFType.None:
                case BFType.Up:
                    Array.Copy(lines, lnOff, pixelData, 0, len);
                    break;

                case BFType.Sub:
                case BFType.Paeth:
                    pixelData[0] = line[0];
                    for (int pos = 1; pos < len; pos++)
                        pixelData[pos] = (byte)(line[pos] + pixelData[pos - 1]);
                    break;

                case BFType.Average:
                    pixelData[0] = line[0];
                    for (int pos = 1; pos < len; pos++)
                        pixelData[pos] = (byte)(line[pos] + pixelData[pos - 1] / 2);
                    break;

                default:
                    msg = $"ByteFilter.DecodeFirstScanline : filter '{filter}' processor not found";
                    return false;
            };

            return true;
        }

        static public bool DecodeScanline(
                ArraySegment<byte> line, // only pixel data
                byte[] pixelData,
                int pdOff, // starting offset inside pixelData
                out string msg)
        {
            //Console.WriteLine(line.HexStr());
            //Console.WriteLine("---");

            byte[] lines = line.Array!;
            int lnOff = line.Offset;
            int len = line.Count;

            msg = "";
            int filter = lines[lnOff - 1];
            filtersFound[filter]++;
            if (!Enum.IsDefined(typeof(BFType), filter))
            {
                msg = $"ByteFilter.DecodeScanline : filter '{filter}' not recognized";
                return false;
            }
            BFType ftype = (BFType)filter;

            switch (ftype)
            {
                case BFType.None:
                    Array.Copy(lines, lnOff, pixelData, pdOff, len);
                    break;

                case BFType.Sub:
                    pixelData[pdOff] = line[0];
                    for (int pos = 1; pos < len; pos++)
                    {
                        int off = pdOff + pos;
                        pixelData[off] = (byte)(line[pos] + pixelData[off - 1]);
                    }
                    break;

                case BFType.Up:
                    for (int pos = 0; pos < len; pos++)
                    {
                        int off = pdOff + pos;
                        pixelData[off] = (byte)(line[pos] + pixelData[off - len]);
                    }
                    break;

                case BFType.Average:
                    pixelData[pdOff] = (byte)((line[0] + pixelData[pdOff - len]) / 2);
                    for (int pos = 1; pos < len; pos++)
                    {
                        int off = pdOff + pos;
                        pixelData[off] = (byte)(line[pos] +
                            (pixelData[off - 1] + pixelData[off - len]) / 2);
                    }
                    break;

                case BFType.Paeth:
                    pixelData[pdOff] = (byte)(line[0] + pixelData[pdOff - len]);
                    for (int pos = 1; pos < len; pos++)
                    {
                        int off = pdOff + pos;
                        int offUp = off - len;
                        pixelData[off] = (byte)(line[pos] + PaethPredictor(
                            pixelData[off - 1],
                            pixelData[offUp],
                            pixelData[offUp - 1]));
                    }
                    break;

                default:
                    msg = $"ByteFilter.DecodeScanline : filter '{filter}' processor not found";
                    return false;
            };

            return true;
        }
    }

    public override string ToString()
    {
        string outp = "";
        foreach (var chunk in chList) outp += chunk.ToString() + "\n";
        return outp;
    }
}
