
namespace imagex;

public class Image
{
    public enum Format
    {
        Jpeg,
        Png,
        Bmp,
        Xdat,
        Rgba,
    }

    public readonly Format format;

    private int _Width;
    public int Width
    {
        get => _Width;
        protected set { _Width = value; }
    }

    private int _Height;
    public int Height
    {
        get => _Height;
        protected set { _Height = value; }
    }

    public Image (Format _format, int _width, int _height)
    {
        Width = _width;
        Height = _height;
        format = _format;
    }
}









