

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

    public Rgba ScanToRGBA(int index = 0, bool useRGBSpace = true) => ecs[index].ToRGBA(useRGBSpace);

}

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