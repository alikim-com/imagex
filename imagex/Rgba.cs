﻿
namespace imagex;

/// <summary>
/// Contains pixel data in universal RGBA format: 
/// 4 channels, 8 bit per channel
/// </summary>
public class Rgba(
    int _width,
    int _height,
    byte[]? _pixelData) : Image(Format.Rgba, _width, _height)
{
    public readonly byte[] pixelData =
        _pixelData ?? new byte[4 * _width * _height];

    public void ToFile(string path, string fname)
    {
        byte[][] rgbaData =
        [
            Width.BytesLeftToRight(),
            Height.BytesLeftToRight(),
            pixelData
        ];
        fname += ".rgba";
        Console.Write($"writing to '{Path.Combine(path, fname)}'... ");
        Utils.WriteFileBytes(path, fname, rgbaData);
        Console.WriteLine("OK");
    }

    /// <summary>
    /// Reformat rows:
    /// RGBA, RGBA, ... -> BGR, BGR, padding to mult of 4 bytes, ....
    /// and rewrite from bottom left corner
    /// </summary>
    /// <returns></returns>
    public Bmp ToBmp()
    {
        int bytesPerRowRGB = 3 * Width;
        int bytesPerRowBMP = 4 * ((bytesPerRowRGB + 3) / 4);
        int pixelArraySize = bytesPerRowBMP * Height;
        byte[] dataBMP = new byte[pixelArraySize];
        int rowPadding = bytesPerRowBMP - bytesPerRowRGB;

        int padding = 0;
        int bytesPerRowRGBA = 4 * Width;
        for(int rRGBA = 0; rRGBA < Height; rRGBA++)
        {
            int offRGBA = rRGBA * bytesPerRowRGBA;
            int offBMP = (Height - 1 - rRGBA) * bytesPerRowBMP + padding;
            for(int offPix = 0; offPix < bytesPerRowRGB; offPix += 3)
            {
                int off0 = offPix;
                int off1 = offPix + 1;
                int off2 = offPix + 2;
                dataBMP[offBMP + off2] = pixelData[offRGBA + off0];
                dataBMP[offBMP + off1] = pixelData[offRGBA + off1];
                dataBMP[offBMP + off0] = pixelData[offRGBA + off2];
                offRGBA++;
            }
            padding += rowPadding;
        }

        return new Bmp(Width, Height, Bmp.PixelFormat.BGR24, dataBMP);
    }

    public enum BlendMode
    {
        Custom,
        Overwrite,
        TransparentOverTransparent,
        TransparentOverOpaque,
    }

    /// <summary>
    /// Width, height and coordinates are in pixels
    /// </summary>
    static public bool BlockCopy(
        Rgba src,
        Rgba dst,
        int width,
        int height,
        int srcX,
        int srcY,
        int dstX,
        int dstY,
        BlendMode mode,
        out string msg,
        Func<byte[], byte[], byte[]>? customFunc = null
        )
    {
        msg = "";

        var sPix = src.pixelData;
        var dPix = dst.pixelData;

        // in bytes
        int bWidth = width * 4;
        int srcOff = (srcY * src.Width + srcX) * 4;
        int dstOff = (dstY * dst.Width + dstX) * 4;
        int srcSkip = (src.Width - width) * 4;
        int dstSkip = (dst.Width - width) * 4;
        int srcLastLine = srcOff + src.Width * (height - 1) * 4;

        int dOff;
        switch (mode)
        {
            case BlendMode.Custom:

                if (customFunc == null) break;

                dOff = dstOff;
                for (int sOff = srcOff; sOff < srcLastLine; sOff += srcSkip)
                {
                    for (int bOff = sOff; bOff < sOff + bWidth; bOff += 4)
                    {
                        byte[] _sPix = new byte[4];
                        byte[] _dPix = new byte[4];
                        Buffer.BlockCopy(sPix, bOff, _sPix, dOff, 4);
                        Buffer.BlockCopy(dPix, dOff, _dPix, dOff, 4);
                        byte[] resPix = customFunc(_sPix, _dPix);
                        Buffer.BlockCopy(resPix, 0, dPix, dOff, 4);
                    }
                    dOff += dstSkip;
                }
                break;

            case BlendMode.Overwrite:

                dOff = dstOff;
                for (int sOff = srcOff; sOff < srcLastLine; sOff+= srcSkip) 
                {
                    Buffer.BlockCopy(sPix, sOff, dPix, dOff, bWidth);
                    dOff += dstSkip;
                }
                break;

            case BlendMode.TransparentOverTransparent:

                dOff = dstOff;
                for (int sOff = srcOff; sOff < srcLastLine; sOff += srcSkip)
                {
                    for(int bOff = sOff; bOff < sOff + bWidth; bOff+=4)
                    {
                        int srcA = sPix[bOff + 3];
                        int dstA = dPix[dOff + 3];
                        int scrF = srcA;
                        int dstF = dstA * (1 - srcA);
                        int A = scrF + dstF;

                        dPix[dOff + 3] = (byte)A;
                        for (int ch = 0; ch < 3; ch++)
                        {
                            int dOffCh = dOff + ch;
                            dPix[dOffCh] = (byte)((scrF * sPix[sOff + ch] + dstF * dPix[dOffCh]) / A);
                        }
                    }
                    dOff += dstSkip;
                }
                break;

            case BlendMode.TransparentOverOpaque:

                dOff = dstOff;
                for (int sOff = srcOff; sOff < srcLastLine; sOff += srcSkip)
                {
                    for(int bOff = sOff; bOff < sOff + bWidth; bOff+=4)
                    {
                        int srcA = sPix[bOff + 3];
                        int scrF = srcA;
                        int dstF = 1 - srcA;

                        for (int ch = 0; ch < 3; ch++)
                        {
                            int dOffCh = dOff + ch;
                            dPix[dOffCh] = (byte)(scrF * sPix[sOff + ch] + dstF * dPix[dOffCh]);
                        }
                    }
                    dOff += dstSkip;
                }
                break;

            default:
                msg = $"Rgba.BlockCopy : blend mode '{mode}' not found";
                return false;

        }

        return true;
    }


}