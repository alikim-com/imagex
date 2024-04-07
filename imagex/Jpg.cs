// 1. https://www.media.mit.edu/pia/Research/deepview/exif.html
//    table tag info extractor ./tagExtractor.js
// 2. https://www.youtube.com/watch?v=CPT4FSkFUgs&list=PLpsTn9TA_Q8VMDyOPrDKmSJYt1DLgDZU4&pp=iAQB

using System.Buffers.Binary;
using static imagex.Segment;
using static imagex.SgmDHT;

namespace imagex;

public class Jpg : Image
{
    enum Status
    {
        None = 0,
        OK = 1,
        FrameHeaderNotFound = 2,
        SOSNotFound = 4,
        EOINotFound = 8,
    }
    Status status;

    public struct Marker
    {
        public SgmType type;
        public int pos;
        public int len;
    }

    readonly ArraySegment<byte> data;
    readonly List<Marker> markers;
    readonly List<Segment> segments = [];

    readonly List<Scan> scan = [];

    public readonly static int[,] rowCol = ZigZagToRowCol();

    public Jpg(
        ArraySegment<byte> _data,
        List<Marker> _markers) : base(Format.Jpeg, 0, 0)
    {
        status = Status.None;

        // create segment objects

        data = _data;
        markers = _markers;
        for (int i = 0; i < markers.Count; i++)
        {
            var mrk = markers[i];
            segments.Add(Create(mrk, _data));
        }

        var sgmSos = GetSegments(SgmType.SOS)[0];
        if (sgmSos is not SgmSOS sos)
        {
            status |= Status.SOSNotFound;
            return;
        }

        if (GetSegments(SgmType.SOF0)[0] is not SgmSOF0 sof0)
        {
            status |= Status.FrameHeaderNotFound;
            return;
        }

        Width = sof0.samPerLine;
        Height = sof0.numLines;

        // process scan (decode Data Units and assemble MCUs)

        var dht = GetSegments(SgmType.DHT).Select(sgm => (SgmDHT)sgm);

        var begOff = sos.bitStrOff;
        // end of scan data is defined by a next segment marker or EOI
        var eoiOff = _data.Offset + _data.Count;
        var sosInd = segments.FindIndex(sgm => sgm == sgmSos);
        var endOff = sosInd < segments.Count - 1 ? segments[sosInd + 1].GetOffset() : eoiOff;

        scan.Add(new Scan(data.Array!, begOff, endOff, sof0, sos, dht));

        // visualise scan for testing purposes
        var path = "../../../testImages";
        var fname = "q_50.jpg";
        var rgba = scan[^1].ToRGBA();
        rgba.ToFile(path, fname);

        status |= Status.OK;
    }

    public static int[,] ZigZagToRowCol(int size = 8)
    {
        /*

         0  1  5  6 14 15 27 28 
         2  4  7 13 16 26 29 42 
         3  8 12 17 25 30 41 43
         9 11 18 24 31 40 44 53
        10 19 23 32 39 45 52 54
        20 22 33 38 46 51 55 60
        21 34 37 47 50 56 59 61
        35 36 48 49 57 58 62 63

        */

        // 1, 2, .. 8,   7, 6 .. 1 = 15 diag strides
        int snum = size * 2 - 1;
        int sm1 = size - 1;
        int[,] si = new int[snum, 2]; // stride intervals
        int cnt = 0;
        int run = 0;
        for (int i = 1; i <= size; i++)
        { // stride length
            si[cnt, 0] = run;
            si[cnt, 1] = run + i - 1;
            run += i;
            cnt++;
        }
        for (int i = sm1; i > 0; i--)
        {
            si[cnt, 0] = run;
            si[cnt, 1] = run + i - 1;
            run += i;
            cnt++;
        }

        int szSq = size * size;

        int[,] rowCol = new int[szSq, 2];

        for (int i = 0; i < szSq; i++)
        {
            int sind = 0;
            int fwd = 0;
            int bwd = 0;
            for (int s = 0; s < snum; s++)
            {
                fwd = i - si[s, 0];
                bwd = i - si[s, 1];
                if (bwd <= 0 && fwd >= 0)
                {
                    sind = s;
                    break;
                }
            }
            int off = sind % 2 != 0 ? fwd : -bwd;
            int col = Math.Min(sm1, sind) - off;
            int row = Math.Max(0, sind - sm1) + off;
            rowCol[i, 0] = row;
            rowCol[i, 1] = col;
        }

        return rowCol;
    }

    public static int[] ZigZagToRowMajor(int size = 8)
    {
        // 1, 2, .. 8,   7, 6 .. 1 = 15 diag strides
        int snum = size * 2 - 1;
        int sm1 = size - 1;
        int[,] si = new int[snum, 2]; // stride intervals
        int cnt = 0;
        int run = 0;
        for (int i = 1; i <= size; i++)
        { // stride length
            si[cnt, 0] = run;
            si[cnt, 1] = run + i - 1;
            run += i;
            cnt++;
        }
        for (int i = sm1; i > 0; i--)
        {
            si[cnt, 0] = run;
            si[cnt, 1] = run + i - 1;
            run += i;
            cnt++;
        }

        int szSq = size * size;

        int[] rowMajor = new int[szSq];

        for (int i = 0; i < szSq; i++)
        {
            int sind = 0;
            int fwd = 0;
            int bwd = 0;
            for (int s = 0; s < snum; s++)
            {
                fwd = i - si[s, 0];
                bwd = i - si[s, 1];
                if (bwd <= 0 && fwd >= 0)
                {
                    sind = s;
                    break;
                }
            }
            int off = sind % 2 != 0 ? fwd : -bwd;
            int col = Math.Min(sm1, sind) - off;
            int row = Math.Max(0, sind - sm1) + off;
            rowMajor[i] = row * size + col;
        }

        return rowMajor;
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

            int vmrk = 0xFF00 + data[i + 1];

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

                int rawLen = BinaryPrimitives.ReadInt16BigEndian
                (new ReadOnlySpan<byte>(data, i + 2, 2)); // rawLen includes 2 len bytes

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

    public List<Segment> GetSegments(SgmType stype)
    {
        Type cType = classMap[stype];
        return segments.Where(s => s.GetType() == cType).ToList();
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
        status: {Utils.IntBitsToEnums((int)status, typeof(Status))}
        MCU: 
        """;
    }
}

/// <summary>
/// Stored in array where array index is comp id
/// </summary>
public struct Component
{
    // defined in frame header SOF0
    // added in SOF0 Ctor

    public byte samFactHor; // Hi
    public byte samFactVer; // Vi
    public byte quanTableInd; // Tqi
    public int upscaleHor; // from subsampling; MCU / DU
    public int upscaleVer;

    // added in Scan Ctor only for scan comp

    public KeyValuePair<ushort, Symb>[][]? dcCodesToSymb; // from DHTs
    public List<int>? dcValidCodeLength;
    public KeyValuePair<ushort, Symb>[][]? acCodesToSymb;
    public List<int>? acValidCodeLength;
}

class Scan
{
    enum Status
    {
        None = 0,
        OK = 1,
        DHTNotFound = 2,
        ComponInfoNotFound = 4,
        BitStreamOutOfBounds = 8,
        HCodeNotFound = 16,
        UnknownSymbol = 32,
    }
    Status status;

    readonly Component[] comp;
    public readonly int[] compList;

    /// <summary>
    /// Represents one Data Unit (8x8) channel
    /// </summary>
    struct DataUnit
    {
        public int compId; // [1,1,1,1, 2,2, 3]
        public short[] zigZag;
        public short[,] table;

        public int chan; // [0,0,0,0, 1,1, 2]
        public int pixTop;
        public int pixLeft;
    }
    readonly DataUnit[] DUnits = [];

    /// <summary>
    /// Minimal Coded Unit
    /// </summary>
    struct MCU
    {
        public int pixTop;
        public int pixLeft;
        public ArraySegment<DataUnit> DUnits;
    }
    readonly MCU[,] MCUs = new MCU[0, 0];

    readonly int numComp;
    readonly int mcuWidthInDu;
    readonly int mcuHeightInDu;

    readonly int[] duSeq;

    readonly int widthInMcu;
    readonly int heightInMcu;

    void AssignComponentTables(SgmSOS sos, IEnumerable<SgmDHT> dht)
    {
        var tableInd = sos.tableInd;
        foreach (var ind in compList)
        {
            var dcInd = tableInd[ind].dc;
            var acInd = tableInd[ind].ac;

            var dcFound = false;
            var acFound = false;
            foreach (var ht in dht)
            {
                if (ht.HasMatch(
                    TblClass.DC,
                    dcInd,
                    out comp[ind].dcCodesToSymb,
                    out comp[ind].dcValidCodeLength))
                {
                    dcFound = true;
                    break;
                }
            }
            foreach (var ht in dht)
            {
                if (ht.HasMatch(
                    TblClass.AC,
                    acInd,
                    out comp[ind].acCodesToSymb,
                    out comp[ind].acValidCodeLength))
                {
                    acFound = true;
                    break;
                }
            }
            if (!dcFound || !acFound)
            {
                status |= Status.DHTNotFound;
                return;
            }
        }
    }

    bool DecodeDataUnits(byte[] data, int begOff, int endOff, out DataUnit[] DUnits)
    {
        var areaInMcu = widthInMcu * heightInMcu;
        DUnits = new DataUnit[areaInMcu * duSeq.Length];

        var bstr = new BitStream(data, begOff, endOff, true);

        int duCnt = 0;

        short[] dcDiff = new short[8];

        for (int m = 0; m < areaInMcu; m++)
            foreach (var ind in duSeq)
            {
                DUnits[duCnt] = new DataUnit
                {
                    compId = ind,
                    zigZag = new short[64]
                };
                var zigZag = DUnits[duCnt].zigZag;

                int zzInd = 0;

                // DC
                bool found = false;
                var dcCodesToSymb = comp[ind].dcCodesToSymb ?? [];
                foreach (var dcLen in comp[ind].dcValidCodeLength!)
                {
                    if (!bstr.PeekBits(dcLen, out ushort code))
                    {
                        status |= Status.BitStreamOutOfBounds;
                        return false;
                    }
                    var subArrInd = dcLen - 1; // zero-index
                    var idx = Array.FindIndex(dcCodesToSymb[subArrInd], kv => kv.Key == code);
                    if (idx == -1) continue;

                    found = true;
                    bstr.FwdBits(dcLen);
                    var symb = dcCodesToSymb[subArrInd][idx].Value;
                    var valBitlen = symb.valBitlen;
                    short dcVal = 0;
                    // valBitlen == 0 -> dcVal == 0 [code:0/0]
                    if (valBitlen != 0 && !bstr.GetDCTValue(valBitlen, out dcVal))
                    {
                        status |= Status.BitStreamOutOfBounds;
                        return false;
                    }
                    dcDiff[ind] = zigZag[zzInd] = (short)(dcVal + dcDiff[ind]);
                    // var codeBin = Convert.ToString(code, 2).PadLeft(dcLen, '0');
                    // Console.WriteLine($"---------------- {codeBin}:{symb.numZeroes:X}/{symb.valBitlen:X} {dcVal}");
                    break;
                }
                if (!found)
                {
                    Console.WriteLine("---- DC code not found ----");
                    status |= Status.HCodeNotFound;
                    return false;
                }

                // AC
                found = false;
                var acCodesToSymb = comp[ind].acCodesToSymb ?? [];
                while (zzInd < 64)
                {
                    foreach (var acLen in comp[ind].acValidCodeLength!)
                    {
                        if (!bstr.PeekBits(acLen, out ushort code))
                        {
                            status |= Status.BitStreamOutOfBounds;
                            return false;
                        }
                        var subArrInd = acLen - 1; // zero-index
                        var idx = Array.FindIndex(acCodesToSymb[subArrInd], kv => kv.Key == code);
                        if (idx == -1) continue;

                        found = true;
                        bstr.FwdBits(acLen);
                        var symb = acCodesToSymb[subArrInd][idx].Value;
                        var valBitlen = symb.valBitlen;
                        var numZeroes = symb.numZeroes;
                        if (valBitlen == 0)
                        {
                            if (numZeroes == 0) // end of mcu
                            {
                                zzInd = 64;
                                break;
                            }
                            if (numZeroes == 15) // 16 zeros 
                            {
                                zzInd += 16;
                                break;
                            }
                            Console.WriteLine($"UnknownACSymbol {code}:{numZeroes:X}/{valBitlen:X}");
                            status |= Status.UnknownSymbol;
                            return false;
                        }
                        if (!bstr.GetDCTValue(valBitlen, out short acVal))
                        {
                            status |= Status.BitStreamOutOfBounds;
                            return false;
                        }
                        zzInd += numZeroes + 1;
                        if (zzInd < 64) zigZag[zzInd] = acVal;
                        // var codeBin = Convert.ToString(code, 2).PadLeft(acLen, '0');
                        // Console.WriteLine($"------------------- {codeBin}:{numZeroes:X}/{valBitlen:X} {acVal}");
                        break;
                    }
                    if (!found)
                    {
                        Console.WriteLine("---- AC code not found ----");
                        status |= Status.HCodeNotFound;
                        return false;
                    }
                }

                var table = DUnits[duCnt].table = new short[8, 8];
                for (int i = 0; i < 64; i++)
                {
                    int row = Jpg.rowCol[i, 0];
                    int col = Jpg.rowCol[i, 1];
                    table[row, col] = zigZag[i];
                }

                //Console.WriteLine(Utils.TableToStr(table, 4, 5));

                duCnt++;
            }

        return true;
    }

    bool AssembleMCUs(out MCU[,] MCUs)
    {
        MCUs = new MCU[heightInMcu, widthInMcu];

        var mcuWidth = mcuWidthInDu * 8;
        var mcuHeight = mcuHeightInDu * 8;

        int duOff = 0;
        int seqLen = duSeq.Length;
        for (int r = 0; r < heightInMcu; r++)
        {
            var pixTop = r * mcuHeight;
            for (int c = 0; c < widthInMcu; c++)
            {
                var arrSgm = new ArraySegment<DataUnit>(DUnits, duOff, seqLen);
                MCUs[r, c] = new MCU
                {
                    pixTop = pixTop,
                    pixLeft = c * mcuWidth,
                    DUnits = arrSgm,
                };

                // position DUs inside MCU, rowMajor
                var compId = -1;
                var chan = -1;
                var rnr = 0;
                for (int k = 0; k < arrSgm.Count; k++)
                {
                    if (compId != duSeq[k])
                    {
                        chan++;
                        rnr = 0;
                        compId = duSeq[k];
                    }
                    var off = duOff + k;
                    DUnits[off].chan = chan;
                    DUnits[off].pixTop = 8 * (rnr / mcuWidthInDu);
                    DUnits[off].pixLeft = 8 * (rnr % mcuWidthInDu);
                    rnr++;
                }

                duOff += seqLen;
            }
        }
        return true;
    }

    public Scan(byte[] _data, int _begOff, int _endOff, SgmSOF0 sof0, SgmSOS sos, IEnumerable<SgmDHT> dht)
    {
        numComp = sos.numComp;

        comp = sof0.comp;
        mcuWidthInDu = sof0.mcuWidthInDu;
        mcuHeightInDu = sof0.mcuHeightInDu;
        duSeq = sof0.duSeq;

        widthInMcu = sof0.scanWidthInMcu;
        heightInMcu = sof0.scanHeightInMcu;

        // check comp integrity as a subset

        compList = sos.compList;
        foreach (var sInd in compList)
            if (Array.IndexOf(sof0.compList, sInd) == -1)
            {
                status |= Status.ComponInfoNotFound;
                return;
            }

        AssignComponentTables(sos, dht);

        DecodeDataUnits(_data, _begOff, _endOff, out DUnits);

        AssembleMCUs(out MCUs);
    }

    public Rgba ToRGBA()
    {
        var mcuWidthInPix = mcuWidthInDu * 8;
        var mcuHeightInPix = mcuHeightInDu * 8;
        var scanWidth = widthInMcu * mcuWidthInPix;
        var scanHeight = heightInMcu * mcuHeightInPix;
        var pdWidth = scanWidth * 4; // RGBA
        var pdHeight = scanHeight;
        var pixelData = new byte[pdWidth * pdHeight];

        for (int r = 0; r < heightInMcu; r++)
            for (int c = 0; c < widthInMcu; c++)
            {
                var mcu = MCUs[r, c];
                var arrSgm = mcu.DUnits;
                var rOff = mcu.pixTop;
                var cOff = mcu.pixLeft * 4; // RGBA
                for (int k = 0; k < arrSgm.Count; k++)
                {
                    var du = arrSgm[k];
                    var compId = du.compId;
                    var upscaleHor = comp[compId].upscaleHor;
                    var upscaleVer = comp[compId].upscaleVer;
                    var duScaled = upscaleHor == 1 && upscaleVer == 1 ? 
                        du.table : Utils.ScaleArray(du.table, upscaleHor, upscaleVer);
                    
                    var rOff2 = rOff + du.pixTop;
                    var cOff2 = cOff + du.pixLeft * 4; // RGBA
                    var chan = du.chan;

                    var upRows = upscaleVer * 8;
                    var upCols = upscaleHor * 8;
                    for (int tr = 0; tr < upRows; tr++)
                    {
                        var rOff3 = rOff2 + tr;
                        var pdRnr = rOff3 * pdWidth;
                        for (int tc = 0; tc < upCols; tc++)
                        {
                            var cOff3 = cOff2 + tc * 4 + chan;
                            var ampl = (byte)Math.Abs(duScaled[tr, tc]);
                            pixelData[pdRnr + cOff3] = ampl;
                        }
                    }
                }
            }

        return new Rgba(scanWidth, scanHeight, pixelData);
    }
}

/// <summary>
/// Start Of Frame (baseline DCT)
/// </summary>
/// <param name="comp">
/// Sparse array for holding info about up to 8 components; </br>
/// Array index is the component Id
/// </param>
/// <param name="duSeq">
/// Sequence or reading DUs for one MCU; </br> 
/// For quad subsampling 2:1:1 it's [1,1,1,1, 2, 3] -> [Y0,Y1,Y2,Y3, Cb0, Cr0]
/// </param>
public class SgmSOF0 : Segment
{
    readonly byte samPrec; // P
    public readonly ushort numLines; // Y
    public readonly ushort samPerLine; // X
    public readonly byte numComp; // Nf

    public readonly Component[] comp;
    public readonly int[] compList;

    public readonly int mcuWidthInDu;
    public readonly int mcuHeightInDu;

    public readonly int[] duSeq;

    public readonly int scanWidthInMcu;
    public readonly int scanHeightInMcu;

    /// <summary>
    /// Baseline JPEG - STORE FLAG IN JPEG CLASS
    /// </summary>
    public SgmSOF0(
        Jpg.Marker _marker,
        ArraySegment<byte> _data) : base(_marker, _data)
    {
        status = (int)Status.OK;

        samPrec = _data[0];

        var rawOff = _data.Offset;
        var rawDat = _data.Array;

        numLines = BinaryPrimitives.ReadUInt16BigEndian(new ReadOnlySpan<byte>(rawDat, rawOff + 1, 2));
        samPerLine = BinaryPrimitives.ReadUInt16BigEndian(new ReadOnlySpan<byte>(rawDat, rawOff + 3, 2));
        numComp = _data[5];

        comp = new Component[8]; // sparse array, index is comp id
        compList = new int[numComp];

        int cnt = 0;
        for (int off = 0; off < numComp * 3; off += 3)
        {
            int ind = _data[6 + off];
            byte samFact = _data[7 + off];
            comp[ind].samFactHor = (byte)(samFact >> 4);
            comp[ind].samFactVer = (byte)(samFact & 0x0F);
            comp[ind].quanTableInd = _data[8 + off];
            compList[cnt++] = ind;
        }

        // find MCU size and upsampling 

        int hMin, vMin, hMax, vMax;
        hMin = vMin = int.MaxValue;
        hMax = vMax = int.MinValue;
        foreach (var ind in compList)
        {
            var h = comp[ind].samFactHor;
            var v = comp[ind].samFactVer;
            if (h < hMin) hMin = h;
            if (h > hMax) hMax = h;
            if (v < vMin) vMin = v;
            if (v > vMax) vMax = v;

        }
        mcuWidthInDu = hMax / hMin;
        mcuHeightInDu = vMax / vMin;

        var mwpix = mcuWidthInDu * 8;
        var mhpix = mcuHeightInDu * 8;

        scanWidthInMcu = (samPerLine + mwpix - 1) / mwpix;
        scanHeightInMcu = (numLines + mhpix - 1) / mhpix;

        // upscale & reading sequence

        List<int> seq = [];
        var mcuArea = mcuWidthInDu * mcuHeightInDu;
        foreach (var ind in compList)
        {
            var uh = comp[ind].upscaleHor = hMax / comp[ind].samFactHor;
            var uv = comp[ind].upscaleVer = vMax / comp[ind].samFactVer;
            var rep = mcuArea / (uh * uv);
            for (int i = 0; i < rep; i++) seq.Add(ind);
        }
        duSeq = [.. seq];
    }

    protected override string ParsedData()
    {
        string compInfo = "";
        foreach (var ind in compList)
        {
            var ci = comp[ind];
            compInfo +=
            $"""
                   [{ind}]
                   subsampl. hor: {ci.samFactHor}
                   subsampl. ver: {ci.samFactVer}
                   quant table [{ci.quanTableInd}]

             """;
        }

        return
        $"""
              sample precision: {samPrec}
              max lines: {numLines}
              max samples per line: {samPerLine}
              components: {numComp}
        {compInfo}      status: {Utils.IntBitsToEnums(status, typeof(Status))}

        """;
    }

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
public class SgmDHT : Segment
{
    public enum TblClass
    {
        DC = 0,
        AC = 1,
    }

    public struct Symb
    {
        public byte numZeroes;
        public byte valBitlen; // AC 1-10, DC 0-11
    }

    readonly List<TblClass> type; // Tc
    readonly List<byte> huffTableInd; // Th
    readonly List<byte[]> codesPerLen; // Li 
    public readonly List<KeyValuePair<ushort, Symb>[][]> codesToSymb; // [1-16][Li] = code/symb
    public readonly List<List<int>> validCodeLength;

    public bool HasMatch(
        TblClass _type,
        int _ind,
        out KeyValuePair<ushort, Symb>[][]? _codesToSymb,
        out List<int>? _validCodeLength)
    {

        _codesToSymb = null;
        _validCodeLength = null;

        for (int i = 0; i < type.Count; i++)
        {
            if (type[i] == _type && huffTableInd[i] == _ind)
            {
                _codesToSymb = codesToSymb[i];
                _validCodeLength = validCodeLength[i];
                return true;
            }
        }

        return false;
    }

    public SgmDHT(Jpg.Marker _marker, ArraySegment<byte> _data) : base(_marker, _data)
    {
        status = (int)Status.OK;

        int qtOff = 0;

        type = [];
        huffTableInd = [];
        codesPerLen = [];
        codesToSymb = [];
        validCodeLength = [];

        while (qtOff < _marker.len)
        {
            byte ti = _data[qtOff];
            type.Add((TblClass)(ti >> 4));
            huffTableInd.Add((byte)(ti & 0x0F));
            codesPerLen.Add(new byte[16]);
            codesToSymb.Add(new KeyValuePair<ushort, Symb>[16][]);
            validCodeLength.Add([]);
            var _codesPerLen = codesPerLen[^1];
            var _codesToSymb = codesToSymb[^1];
            var _validCodeLength = validCodeLength[^1];

            ushort code = 0;

            qtOff++;
            int vOff = qtOff + 16;
            for (int i = 0; i < 16; i++)
            {
                int Li = _codesPerLen[i] = _data[qtOff + i];
                var cts = _codesToSymb[i] = new KeyValuePair<ushort, Symb>[Math.Max(1, Li)];
                if (Li > 0) _validCodeLength.Add(i + 1);

                for (int k = 0; k < Li; k++)
                {
                    byte symb = _data[vOff + k];
                    cts[k] = new KeyValuePair<ushort, Symb>(code++, new Symb
                    {
                        numZeroes = (byte)(symb >> 4),
                        valBitlen = (byte)(symb & 0x0F)
                    });
                }

                vOff += Li;

                code <<= 1;
            }

            qtOff = vOff;
        }
    }

    protected override string ParsedData()
    {
        int maxLineLen = 100;

        string outp = "";

        for (int i = 0; i < huffTableInd.Count; i++)
        {
            var _codesPerLen = codesPerLen[i];
            var _codesToSymb = codesToSymb[i];

            string tInfo = "      codebitlen(totalSymb) code:numZrs/valBitlen, ..\n";
            for (int k = 0; k < 16; k++)
            {
                var cts = _codesToSymb[k];
                var len = _codesPerLen[k];
                var pref = $"      {k + 1,2}({len})".PadRight(13);
                int lineLen = pref.Length;
                tInfo += pref;
                for (int j = 0; j < len; j++)
                {
                    var kv = cts[j];
                    var code = kv.Key;
                    var symb = kv.Value;
                    var numZrs = symb.numZeroes;
                    var valBitlen = symb.valBitlen;
                    var codeBin = Convert.ToString(code, 2).PadLeft(k + 1, '0');
                    var rec = $" {codeBin}:{numZrs:X}/{valBitlen:X}";
                    if (lineLen + rec.Length > maxLineLen)
                    {
                        lineLen = 0;
                        rec = "\n".PadRight(pref.Length + 1) + rec;
                    }
                    lineLen += rec.Length;
                    tInfo += rec;
                }
                tInfo += "\n";
            }
            outp +=
            $"""
                  table type: {type[i]}
                  table id [{huffTableInd[i]}]
            {tInfo}      status: {Utils.IntBitsToEnums(status, typeof(Status))}

            """;
        }

        return outp;
    }
}
/// <summary>
/// Define Quantization Table
/// </summary>
public class SgmDQT : Segment
{
    /* 
     QTable[i, j] = ceil(Baseline_Table[i, j]* Scale_Factor(Q) / 50)
     Q e [1,100]
     Scale_Factor(Q) = 5000 / Q
     Scale_Factor(Q) = 2 ^ ((100 - Q) / 50)
       
     PS Q e [1,12]
     Scale_Factor(Q) = 200 - 2*Q, for Q >= 8
                     = 5000 / Q, for Q < 8

    */

    readonly static int[,] rowCol = Jpg.ZigZagToRowCol();

    readonly List<byte> qtPrec; // Pq
    readonly List<byte> quanTableInd; // Tq
    readonly List<ushort[]> QZigZag;
    readonly List<ushort[,]> QTable; // for printing

    public SgmDQT(Jpg.Marker _marker, ArraySegment<byte> _data) : base(_marker, _data)
    {
        status = (int)Status.OK;

        int qtOff = 0;
        qtPrec = [];
        quanTableInd = [];
        QZigZag = [];
        QTable = [];

        while (qtOff < _marker.len)
        {
            QZigZag.Add(new ushort[64]);
            QTable.Add(new ushort[8, 8]);

            var _QZigZag = QZigZag[^1];
            var _QTable = QTable[^1];

            byte pt = _data[qtOff];
            var _qtPrec = (byte)(pt >> 4);
            qtPrec.Add(_qtPrec);
            quanTableInd.Add((byte)(pt & 0x0F));

            if (_qtPrec == 0)
                for (int i = 0; i < 64; i++) _QZigZag[i] = _data[qtOff + i + 1];

            else if (_qtPrec == 1)
            {
                var rawOff = qtOff + _data.Offset + 1;
                var rawDat = _data.Array;
                int cnt = 0;
                for (int i = 0; i < 128; i += 2)
                    _QZigZag[cnt++] =
                        BinaryPrimitives.ReadUInt16BigEndian(new ReadOnlySpan<byte>(rawDat, rawOff + i, 2));

            } else
            {
                status = (int)Status.QTableBadFormat;
                return;
            }

            for (int i = 0; i < 64; i++)
            {
                int row = rowCol[i, 0];
                int col = rowCol[i, 1];
                _QTable[row, col] = _QZigZag[i];
            }

            qtOff += _qtPrec == 0 ? 65 : 129;
        }
    }

    public bool GetQTableByIndex(byte ind, out ushort[,]? qTable)
    {
        int k = quanTableInd.IndexOf(ind);
        bool OK = k != -1;
        qTable = OK ? QTable[k] : null;
        return OK;
    }

    protected override string ParsedData()
    {
        string outp = "";

        for (int i = 0; i < QTable.Count; i++)
        {
            var _QTable = QTable[i];
            string qtInfo = "";
            for (int k = 0; k < 8; k++)
            {
                qtInfo += "     ";
                for (int j = 0; j < 8; j++) qtInfo += _QTable[k, j].ToString().PadLeft(4, ' ');
                qtInfo += "\n";
            }
            outp +=
            $"""
                  table precision: {(qtPrec[i] + 1) * 8} bit
                  table id [{quanTableInd[i]}]
            {qtInfo}      status: {Utils.IntBitsToEnums(status, typeof(Status))}

            """;
        }

        return outp;
    }

    static readonly int[,] LuminQuanBaseTable = new int[8, 8] {
        { 16, 11, 10, 16, 24, 40, 51, 61 },
        { 12, 12, 14, 19, 26, 58, 60, 55 },
        { 14, 13, 16, 24, 40, 57, 69, 56 },
        { 14, 17, 22, 29, 51, 87, 80, 62 },
        { 18, 22, 37, 56, 68, 109, 103, 77 },
        { 24, 35, 55, 64, 81, 104, 113, 92 },
        { 49, 64, 78, 87, 103, 121, 120, 101 },
        { 72, 92, 95, 98, 112, 100, 103, 99 }};

    static readonly int[,] ChrominQuantBaseTable = new int[8, 8] {
        { 17, 18, 24, 47, 99, 99, 99, 99 },
        { 18, 21, 26, 66, 99, 99, 99, 99 },
        { 24, 26, 56, 99, 99, 99, 99, 99 },
        { 47, 66, 99, 99, 99, 99, 99, 99 },
        { 99, 99, 99, 99, 99, 99, 99, 99 },
        { 99, 99, 99, 99, 99, 99, 99, 99 },
        { 99, 99, 99, 99, 99, 99, 99, 99 },
        { 99, 99, 99, 99, 99, 99, 99, 99 }};


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
public class SgmSOS : Segment
{
    public struct TableInd
    {
        public byte dc; // Td
        public byte ac; // Ta
    }
    public readonly TableInd[] tableInd; // array index is Ci
    public readonly int[] compList; // Cis

    public readonly byte numComp; // Ns

    readonly byte selBeg; // Ss
    readonly byte selEnd; // Se
    readonly byte bitPosHigh; // Ah
    readonly byte bitPosLow; // Al

    public readonly int bitStrOff;

    public SgmSOS(Jpg.Marker _marker, ArraySegment<byte> _data) : base(_marker, _data)
    {
        status = (int)Status.OK;

        numComp = _data[0];
        tableInd = new TableInd[8];
        compList = new int[numComp];

        int cnt = 0;
        int paramLen = numComp * 2;
        for (int off = 0; off < paramLen; off += 2)
        {
            int ind = _data[1 + off];
            byte dcAc = _data[2 + off];
            tableInd[ind].dc = (byte)(dcAc >> 4);
            tableInd[ind].ac = (byte)(dcAc & 0x0F);
            compList[cnt++] = ind;
        }

        selBeg = _data[1 + paramLen];
        selEnd = _data[2 + paramLen];
        byte bitPos = _data[3 + paramLen];
        bitPosHigh = (byte)(bitPos >> 4);
        bitPosLow = (byte)(bitPos & 0x0F);

        bitStrOff = _data.Offset + 4 + paramLen;
    }

    protected override string ParsedData()
    {
        string compInfo = "";
        foreach (var ind in compList)
        {
            var ti = tableInd[ind];
            compInfo +=
            $"""
                   [{ind}]
                   DC table [{ti.dc}]
                   AC table [{ti.ac}]

             """;
        }

        return
        $"""
              components: {numComp}
              selection: {selBeg} - {selEnd}
              bit pos high: {bitPosHigh}
              bit pos low:  {bitPosLow}
        {compInfo}      status: {Utils.IntBitsToEnums(status, typeof(Status))}

        """;
    }
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

        while (ifdOff > 0)
        {
            ifdOff = IFD.CreateLE(ifdOff, _data, ifds, (IFDType)ifdName, out int sOff);
            if (sOff != 0) subOff.Add(sOff);
            ifdName += 2;
        }

        ifdName = 1;
        foreach (var sOff in subOff)
        {
            IFD.CreateLE(sOff, _data, ifds, (IFDType)ifdName, out int _);
            ifdName += 2;
        }
    }

    static void ParseBE(ArraySegment<byte> _data, List<IFD> ifds)
    {
        var ifdOff = BinaryPrimitives.ReadInt32BigEndian
            (new ReadOnlySpan<byte>(_data.Array, _data.Offset + 4, 4));

        int ifdName = 0;
        List<int> subOff = []; // SubIFDs

        while (ifdOff > 0)
        {
            ifdOff = IFD.CreateBE(ifdOff, _data, ifds, (IFDType)ifdName, out int sOff);
            if (sOff != 0) subOff.Add(sOff);
            ifdName += 2;
        }

        ifdName = 1;
        foreach (var sOff in subOff)
        {
            IFD.CreateBE(sOff, _data, ifds, (IFDType)ifdName, out int _);
            ifdName += 2;
        }
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
        {ifdStr.TrimEnd('\n', '\r')}
              status: {Utils.IntBitsToEnums(status, typeof(Status))}

        """;
    }

    /// <summary>
    /// Image File Directory
    /// </summary>
    class IFD
    {
        enum Status
        {
            None = 0,
            OK = 1,
            IFDOutOfBounds = 2,
            IFDFormatError = 4,
        }

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
        readonly Status status;

        internal static int CreateLE(
            int ifdOff,
            ArraySegment<byte> data,
            List<IFD> ifds,
            IFDType ifdName,
            out int subOff)
        {
            subOff = 0;
            Status _status = Status.OK;

            int _rawOff = data.Offset + ifdOff; // IFD start = 0x4949.. + ifdOff
            int rawOff = _rawOff;
            var rawDat = data.Array;
            var rawLen = rawDat!.Length;

            if (rawOff > rawLen - 2)
            {
                _status = Status.IFDOutOfBounds;
                return 0;
            }

            var entryNum = BinaryPrimitives.ReadUInt16LittleEndian
            (new ReadOnlySpan<byte>(rawDat, rawOff, 2));

            int entryLen = 12;

            var totLen = entryLen * entryNum;

            rawOff += 2 + totLen;

            if (rawOff > rawLen - 4)
            {
                _status = Status.IFDOutOfBounds;
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
                    _status = Status.IFDFormatError;
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

            ifds.Add(new IFD(ifdOff, entries, nextOff, ifdName, _status));

            return nextOff;
        }

        internal static int CreateBE(
            int ifdOff,
            ArraySegment<byte> data,
            List<IFD> ifds,
            IFDType ifdName,
            out int subOff)
        {
            subOff = 0;
            Status _status = Status.OK;

            int _rawOff = data.Offset + ifdOff; // IFD start = 0x4949.. + ifdOff
            int rawOff = _rawOff;
            var rawDat = data.Array;
            var rawLen = rawDat!.Length;

            if (rawOff > rawLen - 2)
            {
                _status = Status.IFDOutOfBounds;
                return 0;
            }

            var entryNum = BinaryPrimitives.ReadUInt16BigEndian
            (new ReadOnlySpan<byte>(rawDat, rawOff, 2));

            int entryLen = 12;

            var totLen = entryLen * entryNum;

            rawOff += 2 + totLen;

            if (rawOff > rawLen - 4)
            {
                _status = Status.IFDOutOfBounds;
                return 0;
            }

            int nextOff = BinaryPrimitives.ReadInt32BigEndian
            (new ReadOnlySpan<byte>(rawDat, rawOff, 4));

            // block integrity OK
            // create individual entries

            List<Entry> entries = [];
            rawOff = _rawOff + 2;

            for (int i = 0; i < entryNum; i++)
            {
                TagEnum tag = (TagEnum)(int)BinaryPrimitives.ReadUInt16BigEndian
                (new ReadOnlySpan<byte>(rawDat, rawOff, 2));

                ushort format = BinaryPrimitives.ReadUInt16BigEndian
                (new ReadOnlySpan<byte>(rawDat, rawOff + 2, 2));

                if (!BitesPerCompon.TryGetValue(format, out int byteNum))
                {
                    _status = Status.IFDFormatError;
                    return 0;
                }

                uint compNum = BinaryPrimitives.ReadUInt32BigEndian
                (new ReadOnlySpan<byte>(rawDat, rawOff + 4, 4));

                int datLen = (int)(compNum * byteNum);
                int datOff = datLen <= 4 ? rawOff + 8 : BinaryPrimitives.ReadInt32BigEndian
                (new ReadOnlySpan<byte>(rawDat, rawOff + 8, 4)) + data.Offset;
                object value = format switch
                {
                    1 => rawDat[datOff],
                    2 => rawDat.Slice(datOff, datLen),
                    3 => BinaryPrimitives.ReadUInt16BigEndian(new ReadOnlySpan<byte>(rawDat, datOff, 2)),
                    4 => BinaryPrimitives.ReadUInt32BigEndian(new ReadOnlySpan<byte>(rawDat, datOff, 4)), // 8
                    5 => new uint[2]
                    {
                        BinaryPrimitives.ReadUInt32BigEndian(new ReadOnlySpan<byte>(rawDat, datOff, 4)), // 8
                        BinaryPrimitives.ReadUInt32BigEndian(new ReadOnlySpan<byte>(rawDat, datOff + 4, 4)), // 8
                    },
                    6 => (sbyte)rawDat[datOff],
                    7 => rawDat.Slice(datOff, datLen),
                    8 => BinaryPrimitives.ReadInt16BigEndian(new ReadOnlySpan<byte>(rawDat, datOff, 2)),
                    9 => BinaryPrimitives.ReadInt64BigEndian(new ReadOnlySpan<byte>(rawDat, datOff, 8)),
                    10 => new int[2]
                    {
                        BinaryPrimitives.ReadInt32BigEndian(new ReadOnlySpan<byte>(rawDat, datOff, 4)), // 8
                        BinaryPrimitives.ReadInt32BigEndian(new ReadOnlySpan<byte>(rawDat, datOff + 4, 4)), // 8
                    },
                    11 => BinaryPrimitives.ReadSingleBigEndian(new ReadOnlySpan<byte>(rawDat, datOff, 4)),
                    12 => BinaryPrimitives.ReadDoubleBigEndian(new ReadOnlySpan<byte>(rawDat, datOff, 8)),
                    _ => 0,
                };

                // subIFD
                if (tag == TagEnum.ExifOffset && value is uint _subOff)
                    subOff = (int)_subOff;

                // create IFD
                entries.Add(new Entry(tag, format, compNum, value));

                rawOff += entryLen;
            }

            ifds.Add(new IFD(ifdOff, entries, nextOff, ifdName, _status));

            return nextOff;
        }

        IFD(int _ifdOff, List<Entry> _entries, int _nextOff, IFDType _name, Status _status)
        {
            offset = _ifdOff;
            entries = _entries;
            nextOff = _nextOff;
            name = _name;
            status = _status;
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
                { TagEnum.Compression, typeof(CompressionEnum) },
                { TagEnum.Flash, typeof(FlashEnum) },
            };

            string entStr = "";
            foreach (var ent in entries)
            {
                string valStr;
                if ((ent.format == 2 || ent.tag == TagEnum.UserComment) && ent.value is byte[] asciiBytes)

                    valStr = asciiBytes.ToText().TrimEnd('\n', '\r');

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
                {entStr}         next IFD: 0x{nextOff:X}

                """;
        }

        enum CompressionEnum
        {
            Uncompressed = 1,
            JPEG = 6,
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
    // SOF0 : Baseline DCT
    // SOF1 : Extended sequential DCT, Huffman coding
    // SOF2 : Progressive DCT, Huffman coding
    // SOF3 : Lossless(sequential), Huffman coding
    // SOF9 : Extended sequential DCT, arithmetic coding
    // SOF10 : Progressive DCT, arithmetic coding
    // SOF11 : Lossless(sequential), arithmetic coding

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

    public static readonly Dictionary<SgmType, Type> classMap = new() {
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
        QTableBadFormat = 4,
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

    public int GetOffset() => marker.pos;

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
