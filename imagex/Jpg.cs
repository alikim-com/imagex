﻿// 1.  https://www.media.mit.edu/pia/Research/deepview/exif.html
// 1a. table tag info extractor ./tagExtractor.js
// 2.  https://www.youtube.com/watch?v=CPT4FSkFUgs&list=PLpsTn9TA_Q8VMDyOPrDKmSJYt1DLgDZU4&pp=iAQB

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
        CriticalSegmentMissing = 2,
        WrongHeaderFormat = 4,
    }
    readonly Status status;

    public readonly static int[,] rowCol = ZigZagToRowCol();

    public struct Marker
    {
        public SgmType type;
        public int pos;
        public int len;
    }
    readonly ArraySegment<byte> data;
    readonly List<Marker> markers;

    public List<Segment> metaInfos;

    class Frame(SgmSOF0 _header, List<SgmDHT> _dhts, List<SgmDQT> _dqts)
    {
        public List<SgmDHT> dhts = _dhts;
        public List<SgmDQT> dqts = _dqts;
        public SgmSOF0 header = _header;
        public List<Scan> scans = [];
    }
    readonly List<Frame> frames;

    class Scan(SgmSOS _header, List<SgmDHT> _dhts, List<SgmDQT> _dqts)
    {
        public List<SgmDHT> dhts = _dhts;
        public List<SgmDQT> dqts = _dqts;
        public SgmSOS header = _header;
        public int nextMarkerPos = int.MaxValue;
    }

    public Jpg(
        ArraySegment<byte> _data,
        List<Marker> _markers) : base(Format.Jpeg, 0, 0)
    {
        status = Status.None;

        data = _data;
        markers = _markers;

        // create top-level structure

        List<SgmDHT> dhts = [];
        List<SgmDQT> dqts = [];
        metaInfos = [];
        frames = [];
        bool markScanEnd = false;
        for (int i = 0; i < markers.Count; i++)
        {
            var mrk = markers[i];
            var sgmType = mrk.type;
            var sgmName = sgmType.ToString();
            if (markScanEnd)
            {
                frames[^1].scans[^1].nextMarkerPos = mrk.pos;
                markScanEnd = false;
            }

            if (sgmName.StartsWith("SOF"))
                if (sgmName == "SOF0")
                {
                    frames.Add(new Frame((SgmSOF0)Decode(mrk, _data), dhts, dqts));
                    dhts = [];
                    dqts = [];
                } else
                    throw new NotImplementedException($"Jpg.Ctor : {sgmName} frame header found, only Baseline DCT is currently supported.");

            else if (sgmName == "SOS")
            {
                frames[^1].scans.Add(new Scan((SgmSOS)Decode(mrk, _data), dhts, dqts));
                dhts = [];
                dqts = [];
                markScanEnd = true;
            } else if (sgmName.StartsWith("APP")) metaInfos.Add(Decode(mrk, _data));

            else if (sgmType == SgmType.DQT) dqts.Add((SgmDQT)Decode(mrk, _data));
            else if (sgmType == SgmType.DHT) dhts.Add((SgmDHT)Decode(mrk, _data));

        }

        // check integrity

        if (frames.Count == 0)
        {
            status |= Status.CriticalSegmentMissing;
            return;
        }

        foreach (var fr in frames)
        {
            if (fr.scans.Count == 0 ||
                fr.dqts.Count == 0 && fr.scans[0].dqts.Count == 0 ||
                fr.dhts.Count == 0 && fr.scans[0].dhts.Count == 0)
            {
                status |= Status.CriticalSegmentMissing;
                return;
            }
        }

        if (frames[0].header is not SgmSOF0 header)
        {
            status |= Status.WrongHeaderFormat;
            return;
        }

        Width = header.samPerLine;
        Height = header.numLines;

        status |= Status.OK;
    }

    public static void RemoveUnknownChunks()
    {
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

    public static List<Jpg> FromFile(string path, string fname, bool verbose = true)
    {
        List<Jpg> JpgList = [];

        var data = Utils.ReadFileBytes(path, fname);
        var len = data.Length;

        var mArr = Enum.GetValues(typeof(SgmType));

        List<Marker> markers = [];

        if (verbose) Console.WriteLine("Jpg.FromFile : scanning for markers...");

        int ioff = 0;
        for (int i = 0; i < len - 1; i++)
        {
            if (data[i] != 0xFF) continue;

            int vmrk = 0xFF00 + data[i + 1];

            foreach (int mrk in mArr)
            {
                if (vmrk != mrk) continue;

                var smrk = (SgmType)mrk;

                if (verbose) Console.WriteLine($"{i:X8} {smrk}");

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

                i += rawLen; // FromFileRobust w/o this line ---------- DETECT EXIF LENGTH ??

                break;
            }
        }

        if (verbose) Console.WriteLine();

        return JpgList;
    }

    /// <summary>
    /// Decode scans (ECS') into MCUs & DUs containing original </br>
    /// comp (YcbCr) data (8x8 unscaled) and pass a list of ECS to Xjpg
    /// </summary>
    public Xjpg ToXjpg(bool verbose = true)
    {
        List<ECS> ecs = [];
        List<int> numChan = [];
        int bitDepth = 0;

        foreach (var fr in frames)
        {
            if (fr.header is not SgmSOF0 sof) throw new InvalidDataException
                    ("Jpg.ToXjpg : frame header is not SgmSOF0");

            bitDepth = sof.samPrec;

            foreach (var sc in fr.scans)
            {
                if (sc.header is not SgmSOS sos) throw new InvalidDataException
                    ("Jpg.ToXjpg : scan header is not SgmSOS");

                var begOff = sos.bitStrOff;
                // end of scan data is defined by a next segment marker or EOI
                var eoiOff = data.Offset + data.Count;
                var endOff = Math.Min(sc.nextMarkerPos, eoiOff);

                var dht = sc.dhts.Count > 0 ? sc.dhts : fr.dhts;
                var dqt = sc.dqts.Count > 0 ? sc.dqts : fr.dqts;

                numChan.Add(sos.numComp);
                var ec = new ECS(data.Array!, begOff, endOff, sof, sos, dht, dqt);
                if (ec.status == ECS.Status.OK) ecs.Add(ec);
            }
        }

        return new Xjpg(bitDepth, numChan, Width, Height, ecs);
    }

    public List<Segment> GetSegments(SgmType stype)
    {
        string sgmName = stype.ToString();
        Type cType = classMap[stype];
        List<Segment> sgmList = [];

        bool sgmMeta = sgmName.StartsWith("APP");
        bool sgmHeader = sgmName.StartsWith("SOF");
        bool sgmDHQ = sgmName.StartsWith("DHQ");
        bool sgmDHT = sgmName.StartsWith("DHT");
        bool sgmSOS = sgmName.StartsWith("SOS");

        if (sgmMeta)
            sgmList.AddRange(metaInfos.Where(sgm => sgm.GetType() == cType));

        foreach (var fr in frames)
        {
            if (sgmHeader)
                sgmList.Add(fr.header);
            else if (sgmDHQ)
                sgmList.AddRange(fr.dqts.Where(sgm => sgm.GetType() == cType));
            else if (sgmDHT)
                sgmList.AddRange(fr.dhts.Where(sgm => sgm.GetType() == cType));

            foreach (var scan in fr.scans)
            {
                if (sgmHeader)
                    sgmList.Add(scan.header);
                else if (sgmDHQ)
                    sgmList.AddRange(scan.dqts.Where(sgm => sgm.GetType() == cType));
                else if (sgmDHT)
                    sgmList.AddRange(scan.dhts.Where(sgm => sgm.GetType() == cType));
            }
        }

        return sgmList;
    }

    public override string ToString()
    {
        var raw = data.SneakPeek();

        string sgmStr = "";

        foreach (var meta in metaInfos) sgmStr += meta.ToString();

        foreach (var fr in frames)
        {
            sgmStr += fr.header.ToString();

            foreach (var dqt in fr.dqts) sgmStr += dqt.ToString();
            foreach (var dht in fr.dhts) sgmStr += dht.ToString();

            foreach (var scan in fr.scans)
            {
                sgmStr += scan.header.ToString();

                foreach (var dqt in scan.dqts) sgmStr += dqt.ToString();
                foreach (var dht in scan.dhts) sgmStr += dht.ToString();
            }
        }

        return
        $"""
        image
           raw data: {raw}
        {sgmStr} 
        status: {Utils.IntBitsToEnums((int)status, typeof(Status))}
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

    // added in ECS Ctor for existing comp

    public KeyValuePair<ushort, Symb>[][]? dcCodesToSymb; // from DHTs
    public List<int>? dcValidCodeLength;
    public KeyValuePair<ushort, Symb>[][]? acCodesToSymb;
    public List<int>? acValidCodeLength;
}

/// <summary>
/// Entropy-coded segment
/// </summary>
public class ECS
{
    public enum Status
    {
        None = 0,
        OK = 1,
        DHTNotFound = 2,
        ComponInfoNotFound = 4,
        BitStreamOutOfBounds = 8,
        HCodeNotFound = 16,
        UnknownSymbol = 32,
        DQTNotFound = 64,
        McuEndNotFound = 128,
    }
    public Status status;

    readonly Component[] comp;
    public readonly int[] compList;

    /// <summary>
    /// Represents one Data Unit (8x8 component data)
    /// </summary>
    public struct DataUnit
    {
        public int compId; // [1,1,1,1, 2,2, 3]
        public short[] zigZag;
        public short[,] table;
        public short[,] compData; // from inverse DCT

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

    readonly static double[] DCTArray;

    static ECS()
    {
        double PI16 = Math.PI / 16;
        double C0 = 1 / Math.Sqrt(2);

        int off = 0;
        DCTArray = new double[64];
        for (int i = 0; i < 8; i++)
        {
            for (int _i = 0; _i < 8; _i++)
            {
                double cr = _i != 0 ? 1 : C0;
                DCTArray[off + _i] = cr * Math.Cos((2 * i + 1) * _i * PI16);
            }
            off += 8;
        }
    }

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
        {
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
                while (zzInd < 63)
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

                        if (zzInd > 63)
                        {
                            Console.WriteLine($"---- MCU#{m}/duSeq#{ind} end not found ----");
                            status |= Status.McuEndNotFound;
                            return false;
                        }

                        zigZag[zzInd] = acVal;

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

    void DeQuantise(IEnumerable<SgmDQT> dqt)
    {
        var qTables = new Dictionary<int, ushort[,]>();
        foreach (var cInd in compList)
        {
            bool found = false;
            var qInd = comp[cInd].quanTableInd;
            foreach (var qt in dqt)
            {
                if (qt.HasMatch(qInd, out ushort[,]? qTable))
                {
                    found = true;
                    qTables.Add(cInd, qTable!);
                    break;
                }
            }
            if (!found)
            {
                status |= Status.DQTNotFound;
                return;
            }
        }

        foreach (var du in DUnits)
        {

            var qt = qTables[du.compId];

            for (int r = 0; r < 8; r++)
                for (int c = 0; c < 8; c++)
                    du.table[r, c] *= (short)qt[r, c];
        }
    }

    void InverseDCT()
    {
        for (int u = 0; u < DUnits.Length; u++)
        {
            var table = DUnits[u].table;
            var compData = DUnits[u].compData = new short[8, 8];

            int offr = 0;
            for (int r = 0; r < 8; r++)
            {
                int offc = 0;
                for (int c = 0; c < 8; c++)
                {
                    double sum = 0;

                    for (int _r = 0; _r < 8; _r++)
                    {
                        var dctr = DCTArray[offr + _r];

                        for (int _c = 0; _c < 8; _c++)
                            sum += dctr * DCTArray[offc + _c] * table[_r, _c];
                    }
                    compData[r, c] = (short)(sum / 4);

                    offc += 8;
                }

                offr += 8;
            }
        }
    }

    public ECS(
        byte[] _data,
        int _begOff,
        int _endOff,
        SgmSOF0 sof0,
        SgmSOS sos,
        List<SgmDHT> dht,
        List<SgmDQT> dqt)
    {
        numComp = sos.numComp;

        comp = sof0.comp;
        mcuWidthInDu = sof0.mcuWidthInDu;
        mcuHeightInDu = sof0.mcuHeightInDu;
        duSeq = sof0.duSeq;

        widthInMcu = sof0.scanWidthInMcu;
        heightInMcu = sof0.scanHeightInMcu;

        status = Status.OK;

        // check comp integrity as a subset

        compList = sos.compList;
        foreach (var sInd in compList)
            if (Array.IndexOf(sof0.compList, sInd) == -1)
            {
                status |= Status.ComponInfoNotFound;
                return;
            }

        if (status != Status.OK) return;

        AssignComponentTables(sos, dht);
        if (status != Status.OK) return;

        DecodeDataUnits(_data, _begOff, _endOff, out DUnits);
        if (status != Status.OK) return;

        // ENCODING TEST ONLY
        SgmDHT.Encode(DUnits);

        AssembleMCUs(out MCUs);
        if (status != Status.OK) return;

        DeQuantise(dqt);
        if (status != Status.OK) return;

        InverseDCT();
    }

    /// <summary>
    /// Scale DUs (un-subsample)
    /// </summary>
    /// <param name="useRGBSpace"> Convert YCbCr to RGB</param>
    public Rgba ToRGBA(bool useRGBSpace = true)
    {
        var mcuWidthInPix = mcuWidthInDu * 8;
        var mcuHeightInPix = mcuHeightInDu * 8;
        var scanWidth = widthInMcu * mcuWidthInPix;
        var scanHeight = heightInMcu * mcuHeightInPix;
        var pdWidth = scanWidth * 4; // RGBA
        var pdHeight = scanHeight;
        var size = pdWidth * pdHeight;
        var scanData = new short[size];

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
                    short[,] duScaled =
                        upscaleHor == 1 && upscaleVer == 1 ?
                        du.compData : Utils.ScaleArray(du.compData, upscaleHor, upscaleVer);

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
                            scanData[pdRnr + cOff3] = duScaled[tr, tc];
                        }
                    }
                }
            }

        var pixelData = new byte[size];

        if (useRGBSpace)
            for (int i = 0; i < pixelData.Length; i += 4)
            {
                var Y = scanData[i];
                var Cb = scanData[i + 1];
                var Cr = scanData[i + 2];
                var R = Math.Clamp(Y + 1.402 * Cr + 128, 0, 255);
                var G = Math.Clamp(Y - 0.34414 * Cb - 0.71414 * Cr + 128, 0, 255);
                var B = Math.Clamp(Y + 1.772 * Cb + 128, 0, 255);
                pixelData[i] = (byte)R;
                pixelData[i + 1] = (byte)G;
                pixelData[i + 2] = (byte)B;
            }
        else
            for (int i = 0; i < pixelData.Length; i++)
            {
                var si = Math.Clamp(scanData[i] + 128, 0, 255);
                pixelData[i] = (byte)si;
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
public partial class SgmSOF0 : Segment
{
    public readonly byte samPrec; // P
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

    protected override string ParsedData(int margin = 0)
    {
        string m = "".PadRight(margin);
        string compInfo = "";
        foreach (var ind in compList)
        {
            var ci = comp[ind];
            compInfo +=
            $"""
             {m}[{ind}]
             {m}subsampl. hor: {ci.samFactHor}
             {m}subsampl. ver: {ci.samFactVer}
             {m}quant table [{ci.quanTableInd}]

             """;
        }

        return
        $"""
        {m}sample precision: {samPrec}
        {m}max lines: {numLines}
        {m}max samples per line: {samPerLine}
        {m}components: {numComp}
        {compInfo}{m}status: {Utils.IntBitsToEnums(status, typeof(Status))}

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
public partial class SgmDHT : Segment
{
    public enum TblClass
    {
        DC = 0,
        AC = 1,
    }

    /// <summary>
    /// AC - 162 symb (0-15 zeroes * 1-10 bitlen + 00<eob>, F0<16z>)
    /// DC - 12 symb (0 zeros * 1-11 bitlen + 00<dc==0>)
    /// </summary>
    public struct Symb
    {
        public byte numZeroes; // AC 0-15, DC 0
        public byte valBitlen; // AC 1-10, DC 1-11
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

    protected override string ParsedData(int margin = 0)
    {
        int maxLineLen = 100;

        string m = "".PadRight(margin);
        string outp = "";

        for (int i = 0; i < huffTableInd.Count; i++)
        {
            var _codesPerLen = codesPerLen[i];
            var _codesToSymb = codesToSymb[i];

            string tInfo = m + "codebitlen(totalSymb) code:numZrs/valBitlen, ..\n";
            for (int k = 0; k < 16; k++)
            {
                var cts = _codesToSymb[k];
                var len = _codesPerLen[k];
                var pref = $"{m}{k + 1,2}({len})".PadRight(13);
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
            {m}table type: {type[i]}
            {m}table id [{huffTableInd[i]}]
            {tInfo}{m}status: {Utils.IntBitsToEnums(status, typeof(Status))}

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

    public bool HasMatch(byte ind, out ushort[,]? qTable)
    {
        int k = quanTableInd.IndexOf(ind);
        bool OK = k != -1;
        qTable = OK ? QTable[k] : null;
        return OK;
    }

    protected override string ParsedData(int margin = 0)
    {
        string m = "".PadRight(margin);
        string outp = "";

        for (int i = 0; i < QTable.Count; i++)
        {
            var _QTable = QTable[i];
            string qtInfo = "";
            for (int k = 0; k < 8; k++)
            {
                qtInfo += m;
                for (int j = 0; j < 8; j++) qtInfo += _QTable[k, j].ToString().PadLeft(4, ' ');
                qtInfo += "\n";
            }
            outp +=
            $"""
            {m}table precision: {(qtPrec[i] + 1) * 8} bit
            {m}table id [{quanTableInd[i]}]
            {qtInfo}{m}status: {Utils.IntBitsToEnums(status, typeof(Status))}

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

    protected override string ParsedData(int margin = 0)
    {
        string m = "".PadRight(margin);
        string compInfo = "";
        foreach (var ind in compList)
        {
            var ti = tableInd[ind];
            compInfo +=
            $"""
             {m}[{ind}]
             {m}DC table [{ti.dc}]
             {m}AC table [{ti.ac}]

             """;
        }

        return
        $"""
        {m}components: {numComp}
        {m}selection: {selBeg} - {selEnd}
        {m}bit pos high: {bitPosHigh}
        {m}bit pos low:  {bitPosLow}
        {compInfo}{m}status: {Utils.IntBitsToEnums(status, typeof(Status))}

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

    protected override string ParsedData(int margin = 0)
    {
        string m = "".PadRight(margin);
        string tn = thumb != null ?
            $"{thWidth}x{thHeight} {thumb.pixelData.SneakPeek()}" : "none";

        return
        $"""
        {m}id: {Id}
        {m}ver {mjVer}.{mnVer.ToString().PadLeft(2, '0')}
        {m}density: {denX}x{denY} {denU}
        {m}thumbnail: {tn}
        {m}status: {Utils.IntBitsToEnums(status, typeof(Status))}

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

    protected override string ParsedData(int margin = 0)
    {
        string m = "".PadRight(margin);

        string byteOrd = isLE ? "LittleEndian" : "BigEndinan";
        string ifdStr = "";
        foreach (var ifd in ifds)
            ifdStr += $"{ifd}";
        return
        $"""
        {m}id: {Id}
        {m}      byte order: {byteOrd}
        {m}{ifdStr.TrimEnd('\n', '\r')}
        {m}      status: {Utils.IntBitsToEnums(status, typeof(Status))}

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

public class SgmUnsupported(Jpg.Marker _marker, ArraySegment<byte> _data) : Segment(_marker, _data)
{
}

public abstract class Segment(Jpg.Marker _marker, ArraySegment<byte> _data)
{
    public enum SgmType
    {
        // Start of frame, non-differential, Huffman coding
        SOF0 = 0xFFC0, // Baseline DCT
        SOF1 = 0xFFC1, // Extended sequential DCT
        SOF2 = 0xFFC2, // Progressive DCT
        SOF3 = 0xFFC3, // Lossless (sequential)
        // Start of frame, differential, Huffman coding
        SOF5 = 0xFFC5, // Differential sequential DCT
        SOF6 = 0xFFC6, // Differential progressive DCT
        SOF7 = 0xFFC7, // Differential lossless (sequential)
        // Start of frame, non-differential, arithmetic coding
        JPG = 0xFFC8, // Reserved for JPEG extensions
        SOF9 = 0xFFC9, // Extended sequential DCT
        SOF10 = 0xFFCA, // Progressive DCT
        SOF11 = 0xFFCB, // Lossless (sequential)
        // Start of frame, differential, arithmetic coding
        SOF13 = 0xFFCD, // Differential sequential DCT
        SOF14 = 0xFFCE, // Differential progressive DCT
        SOF15 = 0xFFCF, // Differential lossless (sequential)

        DHT = 0xFFC4, // Define Huffman table(s)
        DAC = 0xFFCC, // Define arithmetic coding conditioning(s)

        // Restart interval termination
        RST0 = 0xFFD0,
        RST1 = 0xFFD1,
        RST2 = 0xFFD2,
        RST3 = 0xFFD3,
        RST4 = 0xFFD4,
        RST5 = 0xFFD5,
        RST6 = 0xFFD6,
        RST7 = 0xFFD7,

        SOI = 0xFFD8, // Start of image
        EOI = 0xFFD9, // End of image
        SOS = 0xFFDA, // Start of scan
        DQT = 0xFFDB, // Define quantization table(s)
        DNL = 0xFFDC, // Define number of lines
        DRI = 0xFFDD, // Define restart interval
        DHP = 0xFFDE, // Define hierarchical progression
        EXP = 0xFFDF, // Expand reference component(s)

        // Application segments
        APP0 = 0xFFE0, // JFIF
        APP1 = 0xFFE1, // Exif
        APP2 = 0xFFE2,
        APP3 = 0xFFE3,
        APP4 = 0xFFE4,
        APP5 = 0xFFE5,
        APP6 = 0xFFE6,
        APP7 = 0xFFE7,
        APP8 = 0xFFE8,
        APP9 = 0xFFE9,
        APP10 = 0xFFEA,
        APP11 = 0xFFEB,
        APP12 = 0xFFEC,
        APP13 = 0xFFED,
        APP14 = 0xFFEE,
        APP15 = 0xFFEF,

        // Extention segments
        JPG0 = 0xFFF0,
        JPG1 = 0xFFF1,
        JPG2 = 0xFFF2,
        JPG3 = 0xFFF3,
        JPG4 = 0xFFF4,
        JPG5 = 0xFFF5,
        JPG6 = 0xFFF6,
        JPG7 = 0xFFF7,
        JPG8 = 0xFFF8,
        JPG9 = 0xFFF9,
        JPG10 = 0xFFFA,
        JPG11 = 0xFFFB,
        JPG12 = 0xFFFC,
        JPG13 = 0xFFFD,

        COM = 0xFFFE, // Comment

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

    public static Segment Decode(Jpg.Marker _marker, ArraySegment<byte> _data)
    {
        if (!classMap.TryGetValue(_marker.type, out Type? clsType))
            return new SgmUnsupported(_marker, _data.Slice(_marker.pos, _marker.len));

        object[] param = [_marker, _data.Slice(_marker.pos, _marker.len)];
        var obj = Activator.CreateInstance(clsType, param);

        return obj == null ? throw new Exception
                ($"Segment.Create : couldn't create instance of type '{clsType}'") : (Segment)obj;
    }

    public int GetOffset() => marker.pos;

    public override string ToString()
    {
        int margin = 3;
        string m = "".PadRight(margin);
        return $"""
               {marker.type}
               {m}raw data: {data.SneakPeek()}
               {m}pos: 0x{marker.pos.HexStr()}
               {m}len: {marker.len}
               {ParsedData(margin)}
               """;
    }

    virtual protected string ParsedData(int margin = 0) => "";
}
