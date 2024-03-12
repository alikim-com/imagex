
using System.Buffers.Binary;
using static imagex.Png.Chunk;
using static imagex.Segment;

namespace imagex;

public class Jpg
{
    ArraySegment<byte> data;
    readonly Dictionary<SgmType, KeyValuePair<int, int>> markers;

    public Jpg(
        ArraySegment<byte> _data, 
        Dictionary<SgmType, KeyValuePair<int, int>> _markers)
    {
        data = _data;
        markers = _markers;
    }

    public static List<Jpg> FromFile(string path, string fname)
    {
        var images = new List<Jpg>();

        var data = Utils.ReadFileBytes(path, fname);
        var len = data.Length;

        var mArr = Enum.GetValues(typeof(SgmType));
        Dictionary<SgmType, KeyValuePair<int, int>> markers = new();

        int ioff = 0;
        for (int i = 0; i < len; i++)
        {
            if (data[i] != 0xFF) continue;
            var val = BinaryPrimitives.ReadInt32BigEndian
                (new ReadOnlySpan<byte>(data, i, 4));
            var vmrk = val >> 16;
            foreach (int mrk in mArr)
            {
                if (vmrk != mrk) continue;

                var smrk = (SgmType)mrk;
                int slen = val & 0xFFFF;

                if (smrk == SgmType.SOI)
                {
                    ioff = i;
                    markers = new();
                }

                markers.Add(smrk, new KeyValuePair<int, int>(i,slen));

                if (smrk == SgmType.EOI)
                {
                    var ars = new ArraySegment<byte>(data, ioff, i - ioff + 2);
                    images.Add(new Jpg(ars, markers));
                }

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

        string mrkStr = "";
        foreach(var (k,p) in markers)
        {
            mrkStr += $"{k} pos: {p.Key}, len: {p.Value}\n";
        }

        return
        $"""
        image
            raw data: {raw}
            {mrkStr} 
        """;
    }
}
/// <summary>
/// Start Of Frame (baseline DCT)
/// </summary>
public class SgmSOF0 { }
/// <summary>
/// Start Of Frame (progressive DCT)
/// </summary>
public class SgmSOF2 { }
/// <summary>
/// Define Huffman Table(s)
/// </summary>
public class SgmDHT { }
/// <summary>
/// Define Quantization Table(s)
/// </summary>
public class SgmDQT { }
/// <summary>
/// Define Restart Interval
/// </summary>
public class SgmDRI { }
/// <summary>
/// Start Of Scan
/// </summary>
public class SgmSOS { }
/// <summary>
/// (JFIF) JPEG File Interchange Format
/// </summary>
public class SgmAPP0 { }
/// <summary>
/// (Exif) Exchangeable image file format
/// </summary>
public class SgmAPP1 { }
/// <summary>
/// Comment
/// </summary>
public class SgmCOM { }

public abstract class Segment
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
}
