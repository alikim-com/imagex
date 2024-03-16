﻿// https://www.media.mit.edu/pia/Research/deepview/exif.html
// table tag info extractor ./tagExtractor.js

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
        List<Jpg> JpgList = [];

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
                    JpgList.Add(new Jpg(ars, markers));
                    i++;
                    break;
                }

                int rawLen = BinaryPrimitives.ReadInt32BigEndian
                (new ReadOnlySpan<byte>(data, i, 4)) & 0xFFFF;

                // starts after len-bytes
                markers.Add(new Marker { type = smrk, pos = i + 2, len = rawLen - 2 });

                i += rawLen; // FromFileRobust w/o this line

                break;
            }
        }

        return JpgList;
    }

    public static Xjpg ToXjpg(Jpg png, bool verbose = true)
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

        var rawOff = _data.Offset;
        var rawDat = _data.Array;

        denX = BinaryPrimitives.ReadUInt16BigEndian
            (new ReadOnlySpan<byte>(rawDat, rawOff + 8, 2));

        denY = BinaryPrimitives.ReadUInt16BigEndian
            (new ReadOnlySpan<byte>(rawDat, rawOff + 10, 2));

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
            for (int i = rawOff + 14; i < size * 3; i += 3)
            {
                Buffer.BlockCopy(rawDat!, i, rgbaData, rgbaOff, 3);
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
    readonly bool isLE;
    readonly List<IFD> ifds;

    enum IFDType
    {
        MainImage = 0, // IFD0
        DigicamInfo = 1, // SubIFD0
        Thumbnail = 2, // IFD1
    }

    public SgmAPP1(Jpg.Marker _marker, ArraySegment<byte> _data) : base(_marker, _data)
    {
        status = (int)Status.OK;

        Id = string.Join("", _data.Slice(0, 6).TakeWhile(b => b != 0).Select(b => (char)b));

        var ordType = BinaryPrimitives.ReadUInt16BigEndian
            (new ReadOnlySpan<byte>(_data.Array, _data.Offset + 6, 2));
        isLE = ordType == 0x4949;

        var data = _data.Slice(6);

        ifds = [];

        if (isLE) ParseLE(data, ifds); else ParseBE(data, ifds);

    }

    /// <summary>
    /// Creates IFDs for main image, digicam & thumbnail
    /// </summary>
    /// <param name="_data">
    /// Starts at 49492a00 - internal IFD offsets origin
    /// </param>
    static void ParseLE(ArraySegment<byte> _data, List<IFD> ifds)
    {
        var ifdOff = BinaryPrimitives.ReadInt32LittleEndian
            (new ReadOnlySpan<byte>(_data.Array, _data.Offset + 4, 4));

        int ifdName = 0;
        List<int> subOff = []; // SubIFDs

        Status status = Status.None;
        while (ifdOff > 0)
        {
            ifdOff = IFD.CreateLE(ifdOff, _data, ifds, (IFDType)ifdName, out int sOff, out status);
            if (sOff != 0) subOff.Add(sOff);
            ifdName += 2;
        }

        ifdName = 1;
        foreach (var sOff in subOff)
        {
            IFD.CreateLE(sOff, _data, ifds, (IFDType)ifdName, out int _, out status);
            ifdName += 2;
        }

        if (status != Status.OK) { }


    }

    static void ParseBE(ArraySegment<byte> _data, List<IFD> ifds)
    {
        ifds = [];

        var ifdOff = BinaryPrimitives.ReadInt32BigEndian
            (new ReadOnlySpan<byte>(_data.Array, _data.Offset + 4, 4));

        Status status = Status.None;
        while (ifdOff > 0)
            ifdOff = IFD.CreateBE(ifdOff, _data, out ifds, out status);

        if (status != Status.OK) { }
    }

    protected override string ParsedData()
    {
        //string tn = thumb != null ?
        //    $"{thWidth}x{thHeight} {thumb.pixelData.SneakPeek()}" : "none";

        string byteOrd = isLE ? "LittleEndian" : "BigEndinan";
        string ifdStr = "";
        foreach (var ifd in ifds)
            ifdStr += $"{ifd}";
        return
        $"""
              id: {Id}
              byte order: {byteOrd}
        {ifdStr}
              status: {Utils.IntBitsToEnums(status, typeof(Status))}

        """;
    }

    /// <summary>
    /// Image File Directory
    /// </summary>
    class IFD
    {
        internal struct Entry
        {
            internal TagEnum tag;
            internal ushort format;
            internal uint compNum;
            internal object value;

            internal Entry(TagEnum _tag, ushort _format, uint _compNum, object _value)
            {
                tag = _tag;
                format = _format;
                compNum = _compNum;
                value = _value;
            }
        }

        readonly IFDType name;
        readonly int offset;
        readonly List<Entry> entries;
        readonly int nextOff;

        internal static int CreateLE(
            int ifdOff,
            ArraySegment<byte> data,
            List<IFD> ifds,
            IFDType ifdName,
            out int subOff,
            out Status status)
        {
            subOff = 0;
            status = Status.OK;

            int _rawOff = data.Offset + ifdOff; // IFD start = 0x4949.. + ifdOff
            int rawOff = _rawOff;
            var rawDat = data.Array;
            var rawLen = rawDat!.Length;

            if (rawOff > rawLen - 2)
            {
                status = Status.IFDOutOfBounds;
                return 0;
            }

            var entryNum = BinaryPrimitives.ReadUInt16LittleEndian
            (new ReadOnlySpan<byte>(rawDat, rawOff, 2));

            int entryLen = 12;

            var totLen = entryLen * entryNum;

            rawOff += 2 + totLen;

            if (rawOff > rawLen - 4)
            {
                status = Status.IFDOutOfBounds;
                return 0;
            }

            int nextOff = BinaryPrimitives.ReadInt32LittleEndian
            (new ReadOnlySpan<byte>(rawDat, rawOff, 4));

            // block integrity OK
            // create individual entries

            List<Entry> entries = [];
            rawOff = _rawOff + 2;

            for (int i = 0; i < entryNum; i++)
            {
                TagEnum tag = (TagEnum)(int)BinaryPrimitives.ReadUInt16LittleEndian
                (new ReadOnlySpan<byte>(rawDat, rawOff, 2));

                ushort format = BinaryPrimitives.ReadUInt16LittleEndian
                (new ReadOnlySpan<byte>(rawDat, rawOff + 2, 2));

                if (!BitesPerCompon.TryGetValue(format, out int byteNum))
                {
                    status = Status.IFDFormatError;
                    return 0;
                }

                uint compNum = BinaryPrimitives.ReadUInt32LittleEndian
                (new ReadOnlySpan<byte>(rawDat, rawOff + 4, 4));

                int datLen = (int)(compNum * byteNum);
                int datOff = datLen <= 4 ? rawOff + 8 : BinaryPrimitives.ReadInt32LittleEndian
                (new ReadOnlySpan<byte>(rawDat, rawOff + 8, 4)) + data.Offset;
                object value = format switch
                {
                    1 => rawDat[datOff],
                    2 => rawDat.Slice(datOff, datLen),
                    3 => BinaryPrimitives.ReadUInt16LittleEndian(new ReadOnlySpan<byte>(rawDat, datOff, 2)),
                    4 => BinaryPrimitives.ReadUInt32LittleEndian(new ReadOnlySpan<byte>(rawDat, datOff, 4)), // 8
                    5 => new uint[2]
                    {
                        BinaryPrimitives.ReadUInt32LittleEndian(new ReadOnlySpan<byte>(rawDat, datOff, 4)), // 8
                        BinaryPrimitives.ReadUInt32LittleEndian(new ReadOnlySpan<byte>(rawDat, datOff + 4, 4)), // 8
                    },
                    6 => (sbyte)rawDat[datOff],
                    7 => rawDat.Slice(datOff, datLen),
                    8 => BinaryPrimitives.ReadInt16LittleEndian(new ReadOnlySpan<byte>(rawDat, datOff, 2)),
                    9 => BinaryPrimitives.ReadInt64LittleEndian(new ReadOnlySpan<byte>(rawDat, datOff, 8)),
                    10 => new int[2]
                    {
                        BinaryPrimitives.ReadInt32LittleEndian(new ReadOnlySpan<byte>(rawDat, datOff, 4)), // 8
                        BinaryPrimitives.ReadInt32LittleEndian(new ReadOnlySpan<byte>(rawDat, datOff + 4, 4)), // 8
                    },
                    11 => BinaryPrimitives.ReadSingleLittleEndian(new ReadOnlySpan<byte>(rawDat, datOff, 4)),
                    12 => BinaryPrimitives.ReadDoubleLittleEndian(new ReadOnlySpan<byte>(rawDat, datOff, 8)),
                    _ => 0,
                };

                // subIFD
                if (tag == TagEnum.ExifOffset && value is uint _subOff)
                    subOff = (int)_subOff;

                // create IFD
                entries.Add(new Entry(tag, format, compNum, value));

                rawOff += entryLen;
            }

            ifds.Add(new IFD(ifdOff, entries, nextOff, ifdName));

            return nextOff;
        }

        internal static int CreateBE(
            int ifdOff,
            ArraySegment<byte> data,
            out List<IFD> ifds,
            out Status status)
        {
            ifds = [];
            status = Status.OK;
            return 0;
        }

        IFD(int _ifdOff, List<Entry> _entries, int _nextOff, IFDType _name)
        {
            offset = _ifdOff;
            entries = _entries;
            nextOff = _nextOff;
            name = _name;
        }

        static readonly Dictionary<int, int> BitesPerCompon = new() {
            {1, 1}, {2, 1}, {3, 2}, {4, 4}, {5, 8}, {6, 1},
            {7, 1}, {8, 2}, {9, 4}, {10, 8}, {11, 4}, {12, 8}
        };

        // ResolutionUnitEnum

        public override string ToString()
        {
            var enmenm = new Dictionary<Enum, Type> {
                { TagEnum.Orientation, typeof(OrientationEnum) },
                { TagEnum.ResolutionUnit, typeof(ResolutionUnitEnum) },
                { TagEnum.YCbCrPositioning, typeof(YCbCrPositioningEnum) },
                { TagEnum.ExposureProgram, typeof(ExposureProgramEnum) },
                { TagEnum.MeteringMode, typeof(MeteringModeEnum) },
                { TagEnum.LightSource, typeof(LightSourceEnum) },
                { TagEnum.Flash, typeof(FlashEnum) },
            };

            string entStr = "";
            foreach (var ent in entries)
            {
                string valStr;
                if ((ent.format == 2 || ent.tag == TagEnum.UserComment) && ent.value is byte[] asciiBytes)

                    valStr = asciiBytes.ToText();

                else if (ent.tag == TagEnum.ShutterSpeedValue && ent.value is int[] sratio)

                    valStr = $"{1 / Math.Pow(2.0, (double)sratio[0] / sratio[1]):F4}";

                else if ((ent.tag == TagEnum.ApertureValue || ent.tag == TagEnum.MaxApertureValue) && ent.value is int[] aratio)

                    valStr = $"{Math.Pow(Math.Sqrt(2), (double)aratio[0] / aratio[1]):F4}";

                else if ((ent.tag == TagEnum.ExifVersion || ent.tag == TagEnum.FlashPixVersion) && ent.value is byte[] bytes)
                {
                    var chars = bytes.Select(b => (char)b).ToArray();
                    valStr = $"{chars[0]}{chars[1]}.{chars[2]}{chars[3]}";

                } else if (ent.tag == TagEnum.MakerNote && ent.value is byte[] mbytes)

                    valStr = mbytes.SneakPeek();

                else if (ent.format == 5 && ent.value is uint[] uratio)
                {
                    uint ur0 = uratio[0];
                    uint ur1 = uratio[1];
                    valStr =
                        ur1 switch
                        {
                            1 => ur0.ToString(),
                            10 or 100 or 1000 => $"{(float)ur0 / ur1}",
                            _ => $"{ur0}/{ur1}"
                        };
                } else if (ent.format == 10 && ent.value is int[] ratio)
                {
                    int r0 = ratio[0];
                    int r1 = ratio[1];
                    valStr =
                        r1 switch
                        {
                            1 => r0.ToString(),
                            10 or 100 or 1000 => $"{(float)r0 / r1}",
                            _ => $"{r0}/{r1}"
                        };

                } else if (enmenm.TryGetValue(ent.tag, out Type? dstType))

                    valStr = dstType == null ? ent.value.ToString()! :
                        $"{Enum.ToObject(dstType, Convert.ToInt32(ent.value))}";

                else
                    valStr = ent.value.ToString()!;

                entStr += $"         {ent.tag}: {valStr}\n";
            }

            return
                $"""
                      {name}
                {entStr}         next offset: {nextOff}

                """;
        }

        enum FlashEnum
        {
            Off = 0,
            On = 1,
        }

        enum LightSourceEnum
        {
            Auto = 0,
            DayLight = 1,
            Fluorescent = 2,
            Tungsten = 3,
            Flash = 10,
        }

        enum MeteringModeEnum
        {
            Average = 1,
            CenterWeighted = 2,
            Spot = 3,
            MultiSpot = 4,
            MultiSegment = 5,
        }

        enum ExposureProgramEnum
        {
            ManualControl = 1,
            Normal = 2,
            AperturePriority = 3,
            ShutterPriority = 4,
            Creative = 5,
            Action = 6,
            PortraitMode = 7,
            LandscapeMode = 8,
        }

        enum OrientationEnum
        {
            UpperLeft = 1,
            LowerRight = 3,
            UpperRight = 6,
            LowerLeft = 8,
        }

        enum ResolutionUnitEnum
        {
            NoUnit = 1,
            Inch = 2,
            Centimeter = 3
        }

        enum YCbCrPositioningEnum
        {
            Center = 1,
            Datum = 2,
        }

        internal enum TagEnum
        {
            // IFD0
            ImageDescription = 0x010e,
            Make = 0x010f,
            Model = 0x0110,
            Orientation = 0x0112,
            XResolution = 0x011a,
            YResolution = 0x011b,
            ResolutionUnit = 0x0128,
            Software = 0x0131,
            DateTime = 0x0132,
            WhitePoint = 0x013e,
            PrimaryChromaticities = 0x013f,
            YCbCrCoefficients = 0x0211,
            YCbCrPositioning = 0x0213,
            ReferenceBlackWhite = 0x0214,
            Copyright = 0x8298,
            ExifOffset = 0x8769,
            // SubIFD
            ExposureTime = 0x829a,
            FNumber = 0x829d,
            ExposureProgram = 0x8822,
            ISOSpeedRatings = 0x8827,
            ExifVersion = 0x9000,
            DateTimeOriginal = 0x9003,
            DateTimeDigitized = 0x9004,
            ComponentConfiguration = 0x9101,
            CompressedBitsPerPixel = 0x9102,
            ShutterSpeedValue = 0x9201,
            ApertureValue = 0x9202,
            BrightnessValue = 0x9203,
            ExposureBiasValue = 0x9204,
            MaxApertureValue = 0x9205,
            SubjectDistance = 0x9206,
            MeteringMode = 0x9207,
            LightSource = 0x9208,
            Flash = 0x9209,
            FocalLength = 0x920a,
            MakerNote = 0x927c,
            UserComment = 0x9286,
            FlashPixVersion = 0xa000,
            ColorSpace = 0xa001,
            ExifImageWidth = 0xa002,
            ExifImageHeight = 0xa003,
            RelatedSoundFile = 0xa004,
            ExifInteroperabilityOffset = 0xa005,
            FocalPlaneXResolution = 0xa20e,
            FocalPlaneYResolution = 0xa20f,
            FocalPlaneResolutionUnit = 0xa210,
            SensingMethod = 0xa217,
            FileSource = 0xa300,
            SceneType = 0xa301,
            // IFD1
            ImageWidth = 0x0100,
            ImageLength = 0x0101,
            BitsPerSample = 0x0102,
            Compression = 0x0103,
            PhotometricInterpretation = 0x0106,
            StripOffsets = 0x0111,
            SamplesPerPixel = 0x0115,
            RowsPerStrip = 0x0116,
            StripByteConunts = 0x0117,
            PlanarConfiguration = 0x011c,
            JpegIFOffset = 0x0201,
            JpegIFByteCount = 0x0202,
            YCbCrSubSampling = 0x0212,
            // Misc
            NewSubfileType = 0x00fe,
            SubfileType = 0x00ff,
            TransferFunction = 0x012d,
            Artist = 0x013b,
            Predictor = 0x013d,
            TileWidth = 0x0142,
            TileLength = 0x0143,
            TileOffsets = 0x0144,
            TileByteCounts = 0x0145,
            SubIFDs = 0x014a,
            JPEGTables = 0x015b,
            CFARepeatPatternDim = 0x828d,
            CFAPattern = 0x828e,
            BatteryLevel = 0x828f,
            IPTC_NAA = 0x83bb,
            InterColorProfile = 0x8773,
            SpectralSensitivity = 0x8824,
            GPSInfo = 0x8825,
            OECF = 0x8828,
            Interlace = 0x8829,
            TimeZoneOffset = 0x882a,
            SelfTimerMode = 0x882b,
            FlashEnergy = 0x920b,
            SpatialFrequencyResponse = 0x920c,
            Noise = 0x920d,
            ImageNumber = 0x9211,
            SecurityClassification = 0x9212,
            ImageHistory = 0x9213,
            SubjectLocation = 0x9214,
            ExposureIndex = 0x9215,
            TIFF_EPStandardID = 0x9216,
            SubSecTime = 0x9290,
            SubSecTimeOriginal = 0x9291,
            SubSecTimeDigitized = 0x9292,
            FlashEnergy2 = 0xa20b,
            SpatialFrequencyResponse2 = 0xa20c,
            SubjectLocation2 = 0xa214,
            ExposureIndex2 = 0xa215,
            CFAPattern2 = 0xa302,
            // Undocumented
            ExposureMode = 0xa402,
            WhiteBalance = 0xa403,
            FocalLengthIn35mm = 0xa405,
            SceneCaptureType = 0xa406,
            ImageUniqueId = 0xa420,
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
        IFDFormatError = 8,
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