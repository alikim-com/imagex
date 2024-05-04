

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
    static readonly Dictionary<Symb, int> dcSymbFreq = [];
    static readonly Dictionary<Symb, int> acSymbFreq = [];

    static SgmDHT()
    {
        for (byte vbl = 0; vbl < 12; vbl++)
            dcSymbFreq.Add(new Symb { numZeroes = 0, valBitlen = vbl }, 0);

        acSymbFreq.Add(new Symb { numZeroes = 0, valBitlen = 0 }, 0);
        acSymbFreq.Add(new Symb { numZeroes = 0xF, valBitlen = 0 }, 0);
        for (byte nz = 0; nz < 16; nz++)
            for (byte vbl = 1; vbl < 11; vbl++)
                acSymbFreq.Add(new Symb { numZeroes = nz, valBitlen = vbl }, 0);
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

        KeyValuePair<ushort, Symb>[][] dcCodesToSymb;
        KeyValuePair<ushort, Symb>[][] acCodesToSymb;

        foreach (var kv in dcSymbFreq) acSymbFreq[kv.Key] = 0;
        foreach (var kv in acSymbFreq) acSymbFreq[kv.Key] = 0;

        int[] dcDiff = new int[8];

        foreach (var du in DUnits)
        {
            var compId = du.compId;
            var zigZag = du.zigZag;

            short dcVal = (short)(zigZag[0] - dcDiff[compId]);
            dcDiff[compId] = dcVal;
            ushort udcVal = (ushort)Math.Abs(dcVal);

            byte vbl = (byte)(32 - BitOperations.LeadingZeroCount(udcVal));
            var symb = new Symb { numZeroes = 0, valBitlen = vbl };

            if (!dcSymbFreq.TryGetValue(symb, out int val))
            {
                status |= Status.SymbolNotInDictionary;
                return false;
            }
            dcSymbFreq[symb] = val + 1;

            int nz = 0;
            for (int i = 1; i < 64; i++)
            {
                short acVal = zigZag[i];
                if(acVal == 0)
                {

                }
                ushort uacVal = (ushort)Math.Abs(acVal);

                vbl = (byte)(32 - BitOperations.LeadingZeroCount(uacVal));
                symb = new Symb { numZeroes = 0, valBitlen = vbl };

                if (!acSymbFreq.TryGetValue(symb, out val))
                {
                    status |= Status.SymbolNotInDictionary;
                    return false;
                }
                acSymbFreq[symb] = val + 1;
            }
        }

        foreach (var du in DUnits)
            Console.WriteLine(Utils.TableToStr(du.table, 4, 5));

        string dicStr = "DC[0]\n";
        foreach (var kv in dcSymbFreq)
        {
            var symb = kv.Key;
            var freq = kv.Value;
            var numZrs = symb.numZeroes;
            var valBitlen = symb.valBitlen;
            dicStr += $"{numZrs:X}/{valBitlen:X} - {freq}\n";
        }
        dicStr += "\nAC[0]\n";
        foreach (var kv in acSymbFreq)
        {
            var symb = kv.Key;
            var freq = kv.Value;
            var numZrs = symb.numZeroes;
            var valBitlen = symb.valBitlen;
            dicStr += $"{numZrs:X}/{valBitlen:X} - {freq}\n";
        }

        Console.WriteLine(dicStr);

        return true;
    }
}