
using System.Buffers.Binary;
using static imagex.Segment;

namespace imagex;

public class Jpg : Image
{
    public struct Marker
    {
        public SgmType type;
        public int pos;
        public int len;
    }

    readonly ArraySegment<byte> data;
    readonly List<Marker> markers;
    readonly List<Segment> segments = [];

    public Jpg(
        ArraySegment<byte> _data,
        List<Marker> _markers) : base(Format.Jpeg, 0, 0)
    {
        data = _data;
        markers = _markers;
        for (int i = 0; i < markers.Count; i++)
        {
            var mrk = markers[i];
            segments.Add(Create(mrk, _data));
        }
    }

    public static List<Jpg> FromFile(string path, string fname)
    {
        var images = new List<Jpg>();

        var data = Utils.ReadFileBytes(path, fname);
        var len = data.Length;

        var mArr = Enum.GetValues(typeof(SgmType));

        List<Marker> markers = [];

        int ioff = 0;
        for (int i = 0; i < len - 1; i++)
        {
            if (data[i] != 0xFF) continue;

            int vmrk = 0xff00 + data[i + 1];

            foreach (int mrk in mArr)
            {
                if (vmrk != mrk) continue;

                var smrk = (SgmType)mrk;

                if (smrk == SgmType.SOI)
                {
                    ioff = i + 2;
                    markers = [];
                    i++;
                    break;
                } else if (smrk == SgmType.EOI)
                {
                    var ars = new ArraySegment<byte>(data, ioff, i - ioff);
                    images.Add(new Jpg(ars, markers));
                    i++;
                    break;
                }

                int slen = BinaryPrimitives.ReadInt32BigEndian
                (new ReadOnlySpan<byte>(data, i, 4)) & 0xFFFF;

                // starts after len bytes
                markers.Add(new Marker { type = smrk, pos = i + 2, len = slen - 2 });

                i += slen; // FromFileRobust w/o this line

                break;
            }
        }

        return images;
    }

    public static Xjpg ToXdat(Jpg png, bool verbose = true)
    {

        return new Xjpg();

    }

    public override string ToString()
    {
        var raw = data.SneakPeek();

        string sgmStr = string.Join("", segments.Select(sgm => sgm.ToString()));

        return
        $"""
        image
           raw data: {raw}
        {sgmStr} 
        """;
    }
}
/// <summary>
/// Start Of Frame (baseline DCT)
/// </summary>
public class SgmSOF0(Jpg.Marker _marker, ArraySegment<byte> _data) : Segment(_marker, _data)
{
}
/// <summary>
/// Start Of Frame (progressive DCT)
/// </summary>
public class SgmSOF2(Jpg.Marker _marker, ArraySegment<byte> _data) : Segment(_marker, _data)
{
}
/// <summary>
/// Define Huffman Table(s)
/// </summary>
public class SgmDHT(Jpg.Marker _marker, ArraySegment<byte> _data) : Segment(_marker, _data)
{
}
/// <summary>
/// Define Quantization Table(s)
/// </summary>
public class SgmDQT(Jpg.Marker _marker, ArraySegment<byte> _data) : Segment(_marker, _data)
{
}
/// <summary>
/// Define Restart Interval
/// </summary>
public class SgmDRI(Jpg.Marker _marker, ArraySegment<byte> _data) : Segment(_marker, _data)
{
}
/// <summary>
/// Start Of Scan
/// </summary>
public class SgmSOS(Jpg.Marker _marker, ArraySegment<byte> _data) : Segment(_marker, _data)
{
}
/// <summary>
/// (JFIF) JPEG File Interchange Format
/// </summary>
public class SgmAPP0 : Segment
{
    enum DensityUnits
    {
        Unitless = 0,
        PixPerInch = 1,
        PixPerCent = 2
    }

    readonly string Id;
    readonly byte mjVer;
    readonly byte mnVer;
    readonly DensityUnits denU;
    readonly ushort denX;
    readonly ushort denY;

    readonly byte thWidth;
    readonly byte thHeight;
    readonly Rgba? thumb;

    public SgmAPP0(
        Jpg.Marker _marker,
        ArraySegment<byte> _data) : base(_marker, _data)
    {
        status = (int)Status.OK;

        Id = string.Join("", _data.Slice(0, 5).TakeWhile(b => b != 0).Select(b => (char)b));

        mjVer = _data[5];
        mnVer = _data[6];

        denU = (DensityUnits)_data[7];

        var soff = _data.Offset;
        var sdat = _data.Array;

        denX = BinaryPrimitives.ReadUInt16BigEndian
            (new ReadOnlySpan<byte>(sdat, soff + 8, 2));

        denY = BinaryPrimitives.ReadUInt16BigEndian
            (new ReadOnlySpan<byte>(sdat, soff + 10, 2));

        thWidth = _data[12];
        thHeight = _data[13];

        if (thWidth > 0 && thHeight > 0)
        {
            var size = thWidth * thHeight;

            if (size != _data.Count - 14)
            {
                status |= (int)Status.ThumbSizeMismatch;
                return;
            }

            var rgbaData = new byte[4 * size];
            Array.Fill<byte>(rgbaData, 0xFF);

            int rgbaOff = 0;
            for (int i = soff + 14; i < size * 3; i += 3)
            {
                Buffer.BlockCopy(sdat!, i, rgbaData, rgbaOff, 3);
                rgbaOff += 4;
            }

            thumb = new Rgba(thWidth, thHeight, rgbaData);
        }
    }

    protected override string ParsedData()
    {
        string tn = thumb != null ?
            $"{thWidth}x{thHeight} {thumb.pixelData.SneakPeek()}" : "none";

        return
        $"""
              id: {Id}
              ver {mjVer}.{mnVer.ToString().PadLeft(2, '0')}
              density: {denX}x{denY} {denU}
              thumbnail: {tn}
              status: {Utils.IntBitsToEnums(status, typeof(Status))}

        """;
    }

}
/// <summary>
/// (Exif) Exchangeable image file format
/// </summary>
public class SgmAPP1 : Segment
{

    readonly string Id;
    bool isLE;
    List<IFD> ifds = [];

    public SgmAPP1(Jpg.Marker _marker, ArraySegment<byte> _data) : base(_marker, _data)
    {
        status = (int)Status.OK;

        Id = string.Join("", _data.Slice(0, 6).TakeWhile(b => b != 0).Select(b => (char)b));

        var ordType = BinaryPrimitives.ReadUInt16BigEndian
            (new ReadOnlySpan<byte>(_data.Array, _data.Offset + 6, 2));
        isLE = ordType == 0x4949;

        var data = _data.Slice(6);

        if (isLE) ParseLE(data); else ParseBE(data);

    }

    /// <summary>
    /// Creates a list of IFDs and processes thumbnail
    /// </summary>
    /// <param name="_data">
    /// Starts at 49492a00 - internal IFD offsets origin
    /// </param>
    static void ParseLE(ArraySegment<byte> _data)
    {
        var ifdOff = BinaryPrimitives.ReadInt32LittleEndian
            (new ReadOnlySpan<byte>(_data.Array, _data.Offset + 4, 4));

        while (ifdOff > 0)
        {
            ifdOff = IFD.CreateLE(ifdOff, _data, out Status status);
        }
    }

    static void ParseBE(ArraySegment<byte> _data)
    {
    }

    protected override string ParsedData()
    {
        //string tn = thumb != null ?
        //    $"{thWidth}x{thHeight} {thumb.pixelData.SneakPeek()}" : "none";

        string byteOrd = isLE ? "LittleEndian" : "BigEndinan";

        return
        $"""
              id: {Id}
              byte order: {byteOrd}
              status: {Utils.IntBitsToEnums(status, typeof(Status))}

        """;
    }

    /// <summary>
    /// Image File Directory
    /// </summary>
    class IFD
    {
        struct Entry
        {
            ushort tag;
            ushort format;
            uint compNum;
            uint dValue;
        }

        readonly ushort entryNum;
        readonly List<Entry> entries;
        readonly int nextOff;

        internal static int CreateLE(
            int ifdOff, 
            ArraySegment<byte> data,
            out Status status)
        {
            status = Status.None;

            int soff = data.Offset + ifdOff;
            var sdat = data.Array;
            var slen = sdat!.Length;

            if (soff > slen - 2) {
                status = Status.IFDOutOfBounds;
                return 0;
            }

            var entryNum = BinaryPrimitives.ReadUInt16LittleEndian
            (new ReadOnlySpan<byte>(sdat, soff, 2));

            var entryLen = 12 * entryNum;

            soff += 2 + entryLen;

            if (soff > slen - 4)
            {
                status = Status.IFDOutOfBounds;
                return 0;
            }

            int nextOff = BinaryPrimitives.ReadInt32LittleEndian
            (new ReadOnlySpan<byte>(sdat, soff, 4));

            // block integrity OK
            // create individual entries

            // create IFD

            return nextOff;
        }

        IFD(ushort _entryNum, List<Entry> _entries, int _nextOff)
        {
            entryNum = _entryNum;
            entries = _entries;
            nextOff = _nextOff;
        }
    }
}
/// <summary>
/// Comment
/// </summary>
public class SgmCOM(Jpg.Marker _marker, ArraySegment<byte> _data) : Segment(_marker, _data)
{
}

public abstract class Segment(Jpg.Marker _marker, ArraySegment<byte> _data)
{
    public enum SgmType
    {
        SOI = 0xFFD8,
        SOF0 = 0xFFC0,
        SOF2 = 0xFFC2,
        DHT = 0xFFC4,
        DQT = 0xFFDB,
        DRI = 0xFFDD,
        SOS = 0xFFDA,
        APP0 = 0xFFE0,
        APP1 = 0xFFE1,
        COM = 0xFFFE,
        EOI = 0xFFD9
    }

    static readonly Dictionary<SgmType, Type> classMap = new() {
        { SgmType.SOF0, typeof(SgmSOF0)},
        { SgmType.SOF2, typeof(SgmSOF2)},
        { SgmType.DHT, typeof(SgmDHT)},
        { SgmType.DQT, typeof(SgmDQT)},
        { SgmType.DRI, typeof(SgmDRI)},
        { SgmType.SOS, typeof(SgmSOS)},
        { SgmType.APP0, typeof(SgmAPP0)},
        { SgmType.APP1, typeof(SgmAPP1)},
        { SgmType.COM, typeof(SgmCOM)},
    };

    public enum Status
    {
        None = 0,
        OK = 1,
        ThumbSizeMismatch = 2,
        IFDOutOfBounds = 4,
    }

    protected int status;

    readonly ArraySegment<byte> data = _data;

    readonly Jpg.Marker marker = _marker;

    public static Segment Create(Jpg.Marker _marker, ArraySegment<byte> _data)
    {
        Type clsType = classMap[_marker.type];

        object[] param = [_marker, _data.Slice(_marker.pos, _marker.len)];
        var obj = Activator.CreateInstance(clsType, param);

        return obj == null ? throw new Exception
                ($"Segment.Create : couldn't create instance of type '{clsType}'") : (Segment)obj;
    }

    public override string ToString()
    {
        return $"""
                  {marker.type}
                     raw data: {data.SneakPeek()}
                     pos: 0x{marker.pos.HexStr()}
                     len: {marker.len}
               {ParsedData()}
               """;
    }

    virtual protected string ParsedData() => "";
}
