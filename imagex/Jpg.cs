
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

                markers.Add(new Marker { type = smrk, pos = i, len = slen });

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
        None = 0,
        PixPerInch = 1,
        PixPerCent = 2
    }

    int length;
    string Id;
    byte mjVer;
    byte mnVer;
    DensityUnits denU;
    ushort denX;
    ushort denY;

    byte thWidth;
    byte thHeight;
    Rgba thumb;

    public SgmAPP0(
        Jpg.Marker _marker,
        ArraySegment<byte> _data) : base(_marker, _data)
    {


        //var len = pixelData.Length;
        //var rgbaData = new byte[4 * Width * Height];
        //int rgbaOff;
        //Array.Fill<byte>(rgbaData, 0xFF);
        //rgbaOff = 0;
        //for (int i = 0; i < len; i += 3)
        //{
        //    Array.Copy(pixelData, i, rgbaData, rgbaOff, 3);
        //    rgbaOff += 4;
        //}
    }

}
/// <summary>
/// (Exif) Exchangeable image file format
/// </summary>
public class SgmAPP1(Jpg.Marker _marker, ArraySegment<byte> _data) : Segment(_marker, _data)
{
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

    protected readonly ArraySegment<byte> data = _data;

    protected readonly Jpg.Marker marker = _marker;

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
