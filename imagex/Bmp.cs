
using System.Buffers.Binary;

namespace imagex;

public class Bmp : Image
{
    private struct HeaderBMP
    {
        internal byte[] id;
        internal int fileSize;
        internal int pixelArrayOffset;
    }
    private readonly HeaderBMP headerBMP;

    private struct HeaderDIB
    {
        internal int size;
        internal int imageWidth;
        internal int imageHeight;
        internal short colorPlanes;
        internal short bitsPerPixel;
        internal int compression;
        internal int pixelArraySize;
        internal int horizontalDPI;
        internal int verticalDPI;
        internal int paletteColors;
        internal int importantColors;
    }
    private readonly HeaderDIB headerDIB;

    private readonly byte[] pixelArray;

    public enum PixelFormat
    {
        BGR24,
        ABGR32,
    }

    enum Status
    {
        None = 0,
        OK = 1,
        InputArrayTooShort = 2,
    }
    readonly Status status;

    public Bmp(
        int _imageWidth,
        int _imageHeight,
        PixelFormat fmt,
        byte[]? _pixelArray = null,
        bool copyData = false) : base(Format.Bmp, _imageWidth, _imageHeight)
    {
        status = Status.None;

        int bytesPerPix = fmt == PixelFormat.BGR24 ? 3 : 4;
        int bytesPerRow = 4 * ((bytesPerPix * Width + 3) / 4);
        int pixelArraySize = bytesPerRow * Height;
        pixelArray = [];

        if (_pixelArray != null)
        {
            var len = _pixelArray.Length;
            if (len < pixelArraySize)
            {
                Console.WriteLine
                    ($"Bmp.Ctor : provided pixel array is shorter ({len}) than required ({pixelArraySize})");
                status = Status.InputArrayTooShort;
                return;
            }
            if (copyData)
            {
                pixelArray = new byte[pixelArraySize];
                Buffer.BlockCopy(_pixelArray, 0, pixelArray, 0, pixelArraySize);

            } else pixelArray = _pixelArray;

        } else pixelArray = new byte[pixelArraySize];

        int hdrBMPsize = 14;
        int hdrDIBsize = 40;

        headerBMP = new HeaderBMP
        {
            id = [(byte)'B', (byte)'M'],
            fileSize = hdrBMPsize + hdrDIBsize + pixelArraySize,
            pixelArrayOffset = hdrBMPsize + hdrDIBsize,
        };

        headerDIB = new HeaderDIB
        {
            size = hdrDIBsize,
            imageWidth = Width,
            imageHeight = Height,
            colorPlanes = 1,
            bitsPerPixel = (short)(bytesPerPix * 8),
            compression = 0,
            pixelArraySize = pixelArraySize,
            horizontalDPI = 2835, // 72 DPI
            verticalDPI = 2835,
            paletteColors = 0,
            importantColors = 0,
        };

        status = Status.OK;
    }

    /// <summary>
    /// Only reads files in the format created by ToFile()
    /// </summary>
    public static Bmp FromFile(string path, string fname, bool trimPixelData = false)
    {
        var data = Utils.ReadFileBytes(path, fname);

        int pixArrayOffset = BinaryPrimitives.ReadInt32LittleEndian
                (new ReadOnlySpan<byte>(data, 0x0a, 4));
        int width = BinaryPrimitives.ReadInt32LittleEndian
                (new ReadOnlySpan<byte>(data, 0x12, 4));
        int height = BinaryPrimitives.ReadInt32LittleEndian
                (new ReadOnlySpan<byte>(data, 0x16, 4));
        short bitsPerPixel = BinaryPrimitives.ReadInt16LittleEndian
                (new ReadOnlySpan<byte>(data, 0x1c, 2));
        int pixelArraySize = BinaryPrimitives.ReadInt16LittleEndian
                (new ReadOnlySpan<byte>(data, 0x22, 4));

        var fmt = bitsPerPixel == 32 ? PixelFormat.ABGR32 : PixelFormat.BGR24;
        var pixelArray = new byte[pixelArraySize];
        Buffer.BlockCopy(data, pixArrayOffset, pixelArray, 0, pixelArraySize);

        return new Bmp(width, height, fmt, pixelArray, trimPixelData);
    }

    public void ToFile(string path, string fname)
    {
        byte[] reserved = [0, 0, 0, 0];
        byte[][] fileData =
        [
            headerBMP.id,
            headerBMP.fileSize.BytesRightToLeft(),
            reserved,
            headerBMP.pixelArrayOffset.BytesRightToLeft(),

            headerDIB.size.BytesRightToLeft(),
            headerDIB.imageWidth.BytesRightToLeft(),
            headerDIB.imageHeight.BytesRightToLeft(),
            headerDIB.colorPlanes.BytesRightToLeft(),
            headerDIB.bitsPerPixel.BytesRightToLeft(),
            headerDIB.compression.BytesRightToLeft(),
            headerDIB.pixelArraySize.BytesRightToLeft(),
            headerDIB.horizontalDPI.BytesRightToLeft(),
            headerDIB.verticalDPI.BytesRightToLeft(),
            headerDIB.paletteColors.BytesRightToLeft(),
            headerDIB.importantColors.BytesRightToLeft(),

            pixelArray,
        ];

        if(!fname.EndsWith(".bmp")) fname += ".bmp";
        Console.Write($"writing to '{Path.Combine(path, fname)}'... ");
        Utils.WriteFileBytes(path, fname, fileData);
        Console.WriteLine("OK");
    }

    public override string ToString()
    {
        var raw = pixelArray.SneakPeek();

        return
        $"""
        BMP image
           pixel data offset: {headerBMP.pixelArrayOffset:X8}
           raw pixel data: {raw}
           DIB header
              width: {headerDIB.imageWidth}
              height: {headerDIB.imageHeight}
              bits per pixel: {headerDIB.bitsPerPixel}
        status: {Utils.IntBitsToEnums((int)status, typeof(Status))}
        """;
    }
}
