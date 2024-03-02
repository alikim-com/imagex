
namespace imagex;

/// <summary>
/// Contains pixel data in universal RGBA format: 
/// 4 channels, 8 bit per channel
/// </summary>
public class Rgba(
    int _width,
    int _height,
    byte[] _pixelData) : Image(Format.Rgba, _width, _height)
{
    public readonly byte[] pixelData = _pixelData;

    public void ToFile(string path, string fname)
    {
        byte[][] rgbaData =
        [
            width.BytesLeftToRight(),
            height.BytesLeftToRight(),
            pixelData
        ];
        Utils.WriteFileBytes(path, fname + ".rgba", rgbaData);
    }
}