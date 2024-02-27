
namespace imagex;

public class Image(Image.Format _format, int _width, int _height)
{
    public enum Format
    {
        Jpeg,
        Png,
        Xdat,
        Rgba,
    }

    public readonly Format format = _format;
    public readonly int width = _width;
    public readonly int height = _height;
}









