


namespace imagex;

public class Xjpg
{
    
}
/// <summary>
/// Start Of Image
/// </summary>
public class SgmSOI { }
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
/// Restart
/// </summary>
public class SgmRST { }
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
/// <summary>
/// End Of Image
/// </summary>
public class SgmEOI { }

public abstract class Segment
{
    public enum SgmType
    {
        SOI =  0xFFD8,
        SOF0 = 0xFFC0,
        SOF2 = 0xFFC2,
        DHT	=  0xFFC4,
        DQT	=  0xFFDB,
        DRI	=  0xFFDD,
        SOS	=  0xFFDA,
        RST0 = 0xFFD0,
        RST1 = 0xFFD1,
        RST2 = 0xFFD2,
        RST3 = 0xFFD3,
        RST4 = 0xFFD4,
        RST5 = 0xFFD5,
        RST6 = 0xFFD6,
        RST7 = 0xFFD7,
        APP0 = 0xFFE0, 
        APP1 = 0xFFE1,
        COM	=  0xFFFE,
        EOI	=  0xFFD9
    }

    static readonly Dictionary<SgmType, Type> classMap = new() {
        { SgmType.SOI, typeof(SgmSOI)},
        { SgmType.SOF0, typeof(SgmSOF0)},
        { SgmType.SOF2, typeof(SgmSOF2)},
        { SgmType.DHT, typeof(SgmDHT)},
        { SgmType.DQT, typeof(SgmDQT)},
        { SgmType.DRI, typeof(SgmDRI)},
        { SgmType.SOS, typeof(SgmSOS)},
        { SgmType.RST0, typeof(SgmRST)},
        { SgmType.RST1, typeof(SgmRST)},
        { SgmType.RST2, typeof(SgmRST)},
        { SgmType.RST3, typeof(SgmRST)},
        { SgmType.RST4, typeof(SgmRST)},
        { SgmType.RST5, typeof(SgmRST)},
        { SgmType.RST6, typeof(SgmRST)},
        { SgmType.RST7, typeof(SgmRST)},
        { SgmType.APP0, typeof(SgmAPP0)},
        { SgmType.APP1, typeof(SgmAPP1)},
        { SgmType.COM, typeof(SgmCOM)},
        { SgmType.EOI, typeof(SgmEOI)},
    };
}
