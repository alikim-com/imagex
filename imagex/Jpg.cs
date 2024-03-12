
using System.Buffers.Binary;

using static imagex.Segment;

namespace imagex;

public class Jpg
{
    public struct Marker
    {
        public SgmType type;
        public int pos;
        public int len;
    }

    ArraySegment<byte> data;
    readonly List<Marker> markers;

    public Jpg(
        ArraySegment<byte> _data,
        List<Marker> _markers)
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

        List<Marker> markers = new();

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
                    markers = new();
                    i++;
                    break;
                }
                else if (smrk == SgmType.EOI)
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

        string mrkStr = "";
        foreach(var mrk in markers)
        {
            mrkStr += $"   {mrk.type,5} pos: 0x{mrk.pos:X8}, len: {mrk.len,9}\n";
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
