// official format doc
// https://www.w3.org/TR/png/


using System.Buffers.Binary;
using System.IO.Hashing;

namespace imagex;

public class Image(Image.Format _format, int _width, int _height)
{
    public enum Format
    {
        JPEG,
        PNG,
    }

    public readonly Format format = _format;
    public readonly int width = _width;
    public readonly int height = _height;
}

public class PNG : Image
{
    static readonly ulong SIG = 0x89504E470D0A1A0A;

    static readonly bool isLE = BitConverter.IsLittleEndian;

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

    public readonly ColorType ctype;
    public readonly int bitDepth;
    public readonly int bitsPerPixel;
    public readonly byte[] PixelData;

    public PNG(
        ColorType _ctype,
        int _bitDepth,
        int _width,
        int _height,
        byte[]? pixelData) : base(Format.PNG, _width, _height)
    {

        ctype = _ctype;
        if (!allowedBitDepths[ctype].Contains(_bitDepth)) throw new NotImplementedException
                ($"PNG.Ctor : depth value '{_bitDepth}' is not supported for '{_ctype}'");

        bitDepth = _bitDepth;
        var numChann = channels[ctype];
        bitsPerPixel = _width * _height * numChann * bitDepth;

        PixelData = pixelData ?? new byte[bitsPerPixel];
    }

    public static PNG FromFile(string path, string fname)
    {
        var data = Utils.ReadFileBytes(path, fname);
        var len = data.Length;

        var offset = 0;

        var sp = new ReadOnlySpan<byte>(data, offset, 8);
        var sig = BinaryPrimitives.ReadUInt64BigEndian(sp);
        if (sig != SIG) throw new Exception
                ($"PNG.FromFile : bad file signature '{sig:X}'");

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

        foreach (var chunk in chList) Utils.Log(chunk.ToString());

        return new PNG(
        ColorType.Truecolour,
        8,
        1,
        1,
        [0, 0, 0, 0]);
    }

    class NoneChunk(Chunk.ChType _type, int _status, ArraySegment<byte> _data, int _crc) :
        Chunk(_type, _status, _data, _crc)
    { }

    class IHDRChunk : Chunk
    {
        public readonly int width;
        public readonly int height;
        public readonly byte bitDepth;
        public readonly byte cType;
        public readonly byte compression; // 0
        public readonly byte filter; // 0
        public readonly byte interlaced;

        public IHDRChunk(ChType _type, int _status, ArraySegment<byte> _data, int _crc) :
            base(_type, _status, _data, _crc)
        {
            width = BinaryPrimitives.ReadInt32BigEndian(_data);
            height = BinaryPrimitives.ReadInt32BigEndian(_data.Slice(4));
            bitDepth = _data[8];
            cType = _data[9];
            compression = _data[10];
            filter = _data[11];
            interlaced = _data[12];
        }

        protected override string ParsedData() =>
        $"""
          width: {width}
          height: {height}
          bitDepth: {bitDepth}
          cType: {cType}
          compression: {compression}
          filter: {filter}
          interlaced: {filter}

        """;
    }

    class IDATChunk : Chunk
    {
        public IDATChunk(ChType _type, int _status, ArraySegment<byte> _data, int _crc) :
            base(_type, _status, _data, _crc)
        {

        }
    }

    class IENDChunk(Chunk.ChType _type, int _status, ArraySegment<byte> _data, int _crc) :
        Chunk(_type, _status, _data, _crc)
    { }

    abstract class Chunk
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

        readonly ChType type;
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
                Utils.Log($"Chunk.Create : chunk type '{_type:X}' not supported");
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
            var beg = data.Offset;
            var len = data.Count;
            var end = beg + len;
            var arr = data.Array;
            var raw = arr == null ? "" : len <= 8 ? BitConverter.ToString(arr, beg, len) :
            BitConverter.ToString(arr, beg, 4) + " .. " + BitConverter.ToString(arr, end - 4, 4);

            var parsedData = ParsedData();
            var parsedDataStr = parsedData == "" ? "" :
                $"""
                parsed data
                {parsedData}
                """;

            return
            $"""
            type: {type}
            length: {len}
            raw data: {raw.Replace("-", " ")}
            crc: {crc:X}
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
    }

}







