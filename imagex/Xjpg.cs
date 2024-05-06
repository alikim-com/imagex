

using System.Numerics;

namespace imagex;

/// <summary>
/// Contains decompressed MCU / DU data while preserving
/// original data packing
/// </summary>
public class Xjpg(
    int _bitDepth, // sof P
    List<int> _numChan, // sof Nf || sos Ns
    int _width, // sof X
    int _height, // sof Y
    List<ECS> _ecs) : Image(Format.Xdat, _width, _height)
{
    public readonly int bitDepth = _bitDepth;
    public readonly List<int> numChan = _numChan;
    public readonly List<ECS> ecs = _ecs;
    //public readonly int bitsPerPixel = _numChan * _bitDepth;
    //public readonly byte[] pixelData = _pixelData;

    public Rgba ScanToRGBA(int index = 0, bool useRGBSpace = true) => ecs.Count > index ? ecs[index].ToRGBA(useRGBSpace) : new Rgba(Width, Height, null);
}

// encoding segments

public partial class SgmSOF0
{
    /// <summary>
    /// Encode segment byte data
    /// </summary>
    public SgmSOF0(
        Component[] _comp,
        int[] _compList,
        int[] _duSeq) :
    base(
        new Jpg.Marker { len = 0, pos = 0, type = SgmType.SOF0 },
        new ArraySegment<byte>([], 0, 0))
    {
        comp = _comp;
        compList = _compList;
        duSeq = _duSeq;
    }
}

public partial class SgmDHT
{
    struct HuffNode
    {
        public Symb symb;
        public int freq;
        public int[] chIndex;
    }

    static readonly HuffNode[] _dc = new HuffNode[12];
    static readonly HuffNode[] _ac = new HuffNode[162];

    // reusable statistics/tree containers for each compId

    static readonly HuffNode[][] compDc = new HuffNode[4][];
    static readonly HuffNode[][] compAc = new HuffNode[4][];

    /// <summary>
    /// Create templates
    /// </summary>
    static SgmDHT()
    {
        // DC

        int cnt = 0;
        for (byte vbl = 0; vbl < 12; vbl++)
            _dc[cnt++] = new HuffNode
            {
                symb = new Symb { numZeroes = 0, valBitlen = vbl },
                freq = 0,
                chIndex = [-1, -1],
            };

        // AC

        // end of block
        _ac[0] = new HuffNode
        {
            symb = new Symb { numZeroes = 0, valBitlen = 0 },
            freq = 0,
            chIndex = [-1, -1],
        };
        // 16 zero span
        _ac[1] = new HuffNode
        {
            symb = new Symb { numZeroes = 0xF, valBitlen = 0 },
            freq = 0,
            chIndex = [-1, -1],
        };
        cnt = 2;
        for (byte nz = 0; nz < 16; nz++)
            for (byte vbl = 1; vbl < 11; vbl++)
                _ac[cnt++] = new HuffNode
                {
                    symb = new Symb { numZeroes = nz, valBitlen = vbl },
                    freq = 0,
                    chIndex = [-1, -1],
                };
    }

    public new enum Status
    {
        None = 0,
        OK = 1,
        SymbolNotInDictionary = 2,

    }

    public static bool Encode(ECS.DataUnit[] DUnits)
    {
        Status status = Status.None;

        for (int c = 0; c < 4; c++)
        {
            compDc[c] = [.. _dc];
            compAc[c] = [.. _ac];
        }

        int[] dcDiff = new int[4];

        // gather frequency statistics

        int cnt = 0;
        foreach (var du in DUnits)
        {
            if (cnt++ > 3) break;

            var compId = du.compId;
            var zigZag = du.zigZag;

            // DC

            var dc = compDc[0];

            short dcVal = (short)(zigZag[0] - dcDiff[compId]);
            dcDiff[compId] = dcVal;
            ushort udcVal = (ushort)Math.Abs(dcVal);

            byte vbl = (byte)(32 - BitOperations.LeadingZeroCount(udcVal));

            int ind = Array.FindIndex(dc, n => n.symb.numZeroes == 0 && n.symb.valBitlen == vbl);
            if (ind == -1)
            {
                status |= Status.SymbolNotInDictionary;
                return false;
            }
            dc[ind].freq++;

            // AC

            var ac = compAc[0];

            byte nz = 0;
            for (int i = 1; i < 64; i++)
            {
                short acVal = zigZag[i];
                if (acVal == 0)
                {
                    nz++;
                    if (nz == 16)
                    {
                        nz = 0;
                        ac[1].freq++; // 16zz
                    }
                    continue;
                }
                ushort uacVal = (ushort)Math.Abs(acVal);

                vbl = (byte)(32 - BitOperations.LeadingZeroCount(uacVal));

                ind = Array.FindIndex(ac, n => n.symb.numZeroes == nz && n.symb.valBitlen == vbl);
                if (ind == -1)
                {
                    status |= Status.SymbolNotInDictionary;
                    return false;
                }

                nz = 0;

                ac[ind].freq++;
            }
            if (nz != 0) ac[0].freq++; // eob   
        }


        // build Huffman tree to find codes' length
        // tree nodes FF added to the array <- COPY SIZE CHK


        foreach (var du in DUnits)
            Console.WriteLine(Utils.TableToStr(du.table, 4, 5));

        string dicStr = "";

        for (int c = 0; c < 4; c++)
        {
            var dc = compDc[c];

            dicStr += $"\nDC[{c}]\n";
            foreach (var node in dc)
            {
                var symb = node.symb;
                var freq = node.freq;
                var numZrs = symb.numZeroes;
                var valBitlen = symb.valBitlen;
                dicStr += $"{numZrs:X}/{valBitlen:X} - {freq}\n";
            }
        }

        for (int c = 0; c < 4; c++)
        {
            var ac = compAc[c];
            dicStr += $"\nAC[{c}]\n";
            foreach (var node in ac)
            {
                var symb = node.symb;
                var freq = node.freq;
                var numZrs = symb.numZeroes;
                var valBitlen = symb.valBitlen;
                dicStr += $"{numZrs:X}/{valBitlen:X} - {freq}\n";
            }
        }

        Console.WriteLine(dicStr);

        return true;
    }
}