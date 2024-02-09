// official format doc
// https://www.w3.org/TR/png/

using System;
using System.Buffers.Binary;

namespace imagex;

public class Image
{
    public enum Format
    {
        JPEG,
        PNG,
    }

    public readonly Format format;
    public readonly int width;
    public readonly int height;

    public Image(Format _format, int _width, int _height)
    {
        format = _format;
        width = _width;
        height = _height;
    }
}

public class PNG : Image
{
    static readonly ulong SIG = 0x89504E470D0A1A0A;

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
        byte[]? pixelData) : base(Format.PNG, _width, _height) { 

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
        var BEData = Utils.ReadFileBytes(path, fname);
        var len = BEData.Length;

        var offset = 0;

        var sp = new ReadOnlySpan<byte>(BEData, offset, 8);
        var sig = BinaryPrimitives.ReadUInt64BigEndian(sp);
        if (sig != SIG) throw new Exception
                ($"PNG.FromFile : bad file signature '{sig:X}'");

        // find chunks
        offset = 8;
        List<Chunk> chList = [];
        while(offset < len)
        {
            var spLen = new ReadOnlySpan<byte>(BEData, offset, 4);
            var chLen = BinaryPrimitives.ReadInt32BigEndian(spLen);

            var spType = new ReadOnlySpan<byte>(BEData, offset + 4, 4);
            var chType = BinaryPrimitives.ReadInt32BigEndian(spType);

            var spCrc = new ReadOnlySpan<byte>(BEData, offset + 8 + chLen, 4);
            var crc = BinaryPrimitives.ReadInt32BigEndian(spCrc);

            chList.Add(new Chunk(chType, BEData, offset + 8, chLen, crc));

            offset += 12 + chLen;
        }

        foreach (var chunk in chList) Utils.Log(chunk.ToString());

        return new PNG(
        ColorType.Truecolour,
        8,
        1,
        1,
        new byte[] { 0,0,0,0} );
    }

    class Chunk
    {
        public enum Type
        {
            None = 0,
            IHDR = 0x49484452,
            IDAT = 0x49444154,
            IEND = 0x49454E44,
        }

        readonly Type type;
        readonly byte[] BEData;
        readonly int offset;
        readonly int length;
        readonly int crc;

        // READ CRC32
        // READ CONTENT INTO STRUCT? IHDR

        internal Chunk(int _type, byte[] _data, int _offset, int _length, int _crc) {

            if (!Enum.IsDefined(typeof(Type), _type))
            {
                type = Type.None;
                Utils.Log($"Chunk.Ctor : chunk type '{_type:X}' not supported, skipping");
            } else {
                type = (Type)_type;
            }

            BEData = _data;
            offset = _offset;
            length = _length;
            crc = _crc;
        }

        void Some() {
            var spData = new ReadOnlySpan<byte>(BEData, offset, length);
        }

        public override string ToString() {
            var beg = offset;
            var end = offset + length;
            var raw = length > 4 ? 
                BitConverter.ToString(BEData[beg..(beg + 4)]) + 
                " .. " + BitConverter.ToString(BEData[(end - 4)..end]) :
                BitConverter.ToString(BEData[beg..end]);
            return 
            $"""
            type: {type}
            length: {length}
            raw data: {raw.Replace("-", " ")}
            crc: {crc:X}

            """;
        }
    }

    class ByteFilter
    {
        public enum Type
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
        static public byte Encode(Type type, params int[] args)
        {
            return type switch
            {
                Type.None => (byte)args[0],
                Type.Sub => (byte)(args[0] - args[1]),
                Type.Up => (byte)(args[0] - args[2]),
                Type.Average => (byte)(args[0] - (args[1] + args[2]) / 2),
                Type.Paeth => (byte)(args[0] - PaethPredictor(args[1], args[2], args[3])),
                _ => 0,
            };
        }

        static public byte Decode(Type type, params int[] args)
        {
            return type switch
            {
                Type.None => (byte)args[0],
                Type.Sub => (byte)(args[0] + args[1]),
                Type.Up => (byte)(args[0] + args[2]),
                Type.Average => (byte)(args[0] + (args[1] + args[2]) / 2),
                Type.Paeth => (byte)(args[0] + PaethPredictor(args[1], args[2], args[3])),
                _ => 0,
            };
        }
    }

}







