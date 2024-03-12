
using static imagex.Png;

namespace imagex;

/// <summary>
/// Contains decompressed pixel data while preserving
/// original pixel packing of PNG format
/// </summary>
public class Xpng(
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

    public void ToFile(string path, string fname)
    {
        byte[][] xData =
        [
            Width.BytesLeftToRight(),
            Height.BytesLeftToRight(),
            [(byte)numChan, (byte)bitDepth, 0, 0],
            pixelData
        ];
        Utils.WriteFileBytes(path, fname + ".xdat", xData);
    }

    public Rgba ToRgba()
    {
        var len = pixelData.Length;
        var rgbaData = new byte[4 * Width * Height];
        int rgbaOff;

        if (bitDepth == 8)
        {
            switch (numChan)
            {
                case 4:
                    Array.Copy(pixelData, 0, rgbaData, 0, len);
                    break;
                case 3:
                    Array.Fill<byte>(rgbaData, 0xFF);
                    rgbaOff = 0;
                    for (int i = 0; i < len; i += 3)
                    {
                        Array.Copy(pixelData, i, rgbaData, rgbaOff, 3);
                        rgbaOff += 4;
                    }
                    break;
                case 2:
                    rgbaOff = 0;
                    for (int i = 0; i < len; i += 2)
                    {
                        rgbaData[rgbaOff] =
                        rgbaData[rgbaOff + 1] =
                        rgbaData[rgbaOff + 2] = pixelData[i];
                        rgbaData[rgbaOff + 3] = pixelData[i + 1];
                        rgbaOff += 4;
                    }
                    break;
                case 1:
                    Array.Fill<byte>(rgbaData, 0xFF);
                    rgbaOff = 0;
                    for (int i = 0; i < len; i++)
                    {
                        rgbaData[rgbaOff] =
                        rgbaData[rgbaOff + 1] =
                        rgbaData[rgbaOff + 2] = pixelData[i];
                        rgbaOff += 4;
                    }
                    break;
            }
        } 
        else if (bitDepth < 8)
        {

        } 
        else
        {
            throw new NotImplementedException
                ($"Rgba doesn't support bitdepth '{bitDepth}'");
        }

        return new Rgba(Width, Height, rgbaData);
    }

    public override string ToString()
    {
        return
        $"""
        cType: {cType}
        bitDepth: {bitDepth}
        channels: {numChan}
        width: {Width}
        height: {Height}
          
        """;
    }
}

