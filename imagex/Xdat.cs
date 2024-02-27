
using static imagex.Png;

namespace imagex;

/// <summary>
/// Contains decompressed pixel data while preserving
/// original bitDepth packing of PNG format
/// </summary>
public class Xdat(
    ColorType _cType,
    int _bitDepth,
    int _numChan,
    int _width,
    int _height,
    byte[] _pixelData) : Image(Format.Xdat, _width, _height)
{
    public readonly ColorType cType = _cType;
    public readonly int bitDepth = _bitDepth;
    public readonly int numChan = _numChan;
    public readonly int bitsPerPixel = _numChan * _bitDepth;
    public readonly byte[] pixelData = _pixelData;

    public static void ToFile(Xdat xdat, string path, string fname)
    {
        byte[][] xData =
        [
            xdat.width.BytesRightToLeft(),
            xdat.height.BytesRightToLeft(),
            [(byte)xdat.numChan, (byte)xdat.bitDepth, 0, 0],
            xdat.pixelData
        ];
        Utils.WriteFileBytes(path, fname + ".xdat", xData);
    }

    public override string ToString()
    {
        return
        $"""
        cType: {cType}
        bitDepth: {bitDepth}
        channels: {numChan}
        width: {width}
        height: {height}
          
        """;
    }
}

