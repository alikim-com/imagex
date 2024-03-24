
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Hashing;

namespace imagex;

internal class Program
{
    static void Main()
    {
        //TestCRC32();
        //TestLoadPng();
        //TestLoadJpg();
        //StudyQTables();
        PrintImageInfo();
        //TestReadHuffmanValues();
    }

    static void TestReadHuffmanValues()
    {
        // 000 001 010 011 100 101 110 111
        // -7  -6  -5  -4    4   5   6   7

        // ref_1
        // 001 -??-> -6
        // -------------------------
        // 001000     == -6 huff
        // 110111 ~
        // 000110 >>  == +6 2's comp
        // 111001 ~
        // 111010 ++  == -6 2's comp

        // test
        // 010 111 011 101 110 111 011 101 110 101 101 101 101 111 101 111 011 011 011 011 010 110 100 101
        // -5  7   -4  5   6   7   -4  5   6   5   5   5   5   7   5   7   -4  -4  -4  -4  -5  6   4   5

        byte[] data = [
            0b01011101,
            0b11011101,
            0b11011101,
            0b11010110,
            0b11011011,
            0b11101111,
            0b01101101,
            0b10110101,
            0b10100101];

        var vbitLen = 3;
        var minPayload = 3;
        var vLen = 24;

        var arrLen = data.Length;

        short[] val = new short[vLen]; // must be 16 bit

        int bytesLoaded;
        int payload;

        ulong cont; // must be 64 bit

        // initial load
        cont = 0;
        bytesLoaded = Math.Min(8, arrLen);
        if (bytesLoaded >= 8)
        {
            var sp = new ReadOnlySpan<byte>(data, 0, 8);
            cont = BinaryPrimitives.ReadUInt64BigEndian(sp);
        } else
        {
            for (int i = 0; i < arrLen; i++)
            {
                cont <<= 8;
                cont |= data[i];
            }
            cont <<= 64 - arrLen * 8;
        }
        payload = bytesLoaded * 8;

        int vind = 0;
        int shft = 64 - vbitLen;
        while (true)
        {
            int bytesToLoad = arrLen - bytesLoaded;
            if (vind > vLen - 1 || bytesToLoad == 0 && payload < minPayload) break;

            var neg = (cont >> 63) == 0;
            val[vind++] = neg ? (short)(~((~cont) >> shft) + 1) : (short)(cont >> shft); // ref_1

            cont <<= vbitLen;
            payload -= vbitLen;

            if (payload >= minPayload) continue;

            // refill
            ulong payloadRefill = 0; // must be 64 bit
            var availLen = 64 - payload;
            var refillBytes = Math.Min(availLen / 8, bytesToLoad);
            var refillBits = refillBytes * 8;
            for (int i = 0; i < refillBytes; i++)
            {
                payloadRefill <<= 8;
                payloadRefill |= data[bytesLoaded++];
            }
            payloadRefill <<= availLen - refillBits;
            cont |= payloadRefill;

            payload += refillBits;
        }

        Console.WriteLine(string.Join(" ", val));

    }

    static void StudyQTables()
    {
        var path = "../../../testImages";

        Console.WriteLine("Please enter file name to decode:");
        string fname = Console.ReadLine() ?? "";

        int row = 4;
        int col = 6;
        string plotStr = "plot: ";
        if (fname == "")
            for (int i = 0; i <= 100; i += 5)
            {
                fname = $"q1_{i}.jpg";
                Console.WriteLine("decoding '" + fname + "'..");
                var images = Jpg.FromFile(path, fname);
                var lst = images[0].GetSegments(Segment.SgmType.DQT);
                if (lst[0] is SgmDQT dqt)
                    if (dqt.GetQTableByIndex(0, out ushort[,]? qTable) && qTable != null)
                    {
                        plotStr += $"({i},{qTable[row, col] / 1.03}),";
                        string qtInfo = "";
                        for (int k = 0; k < 8; k++)
                        {
                            qtInfo += "     ";
                            for (int j = 0; j < 8; j++) qtInfo += qTable[k, j].ToString().PadLeft(4, ' ');
                            qtInfo += "\n";
                        }
                        Console.WriteLine(qtInfo);
                    }
            }
        Console.WriteLine(plotStr);
    }

    static void PrintImageInfo()
    {
        var path = "../../../testImages";

        Console.WriteLine("Please enter file name to decode:");
        string fname = Console.ReadLine() ?? "";
        if (fname == "") fname = "baloon.jpg";
        Console.WriteLine("decoding '" + fname + "'..");

        var images = Jpg.FromFile(path, fname);
        foreach (var jpg in images) Console.WriteLine(jpg);
    }

    static void TestCRC32()
    {
        byte[] data = [
            // 0b00110100, 0b11101100
            0x89,
            0x50,
            0x4e,
            0x48,
            0x0d,
            0x63,
            0xf8,
            0x90,
            0x54,
            0x08,
            0xd7,
            0x63,
            0xf8,
            0xcf,
            0x00

            //0xff
            //0x01, 0x02, 0x03, 0x04
            //0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 
            //0x00, 0x00, 0x00, 0x01, 0x08, 0x02, 0x00, 0x00, 0x00

        ];

        Stopwatch watch = new();
        watch.Start();

        ulong res = Utils.CRC(data);

        watch.Stop();
        Console.WriteLine(watch.Elapsed);

        Utils.PrintLongFmt(res);

        Utils.Log("-----");

        // speed test against generic Utils.CRC, which is incompatible
        // with the version of System.IO.Hashing.Crc32 used in PNG
        // https://www.w3.org/TR/png/#D-CRCAppendix

        watch.Reset();
        watch.Start();

        byte[] checksum = Crc32.Hash(data);

        watch.Stop();
        Console.WriteLine(watch.Elapsed);

        string outp = "";
        foreach (byte b in checksum)
            outp += Convert.ToString(b, 2).PadLeft(8, '0') + " ";
        Utils.Log(outp);

        // Utils.CRC is sligthly slower

        // 00:00:00.0039221
        // 11010101 11101100 00110110 10001010
        // -----
        // 00:00:00.0033360
        // 00111000 00001001 11111001 10010100
    }

    static void TestLoadJpg()
    {
        var path = "../../../testImages";

        Console.WriteLine("Please enter file name to decode:");
        string fname = Console.ReadLine() ?? "";
        if (fname == "") fname = "42.jpg";// "baloon.jpg";

        var images = Jpg.FromFile(path, fname);
    }

    static void TestLoadPng()
    {
        var path = "../../../testImages";

        Console.WriteLine("Please enter file name to decode:");
        string fname = Console.ReadLine() ?? "";
        if (fname == "") fname = "rgb_3x3.png";

        var png = Png.FromFile(path, fname);
        Console.WriteLine(png);

        Console.WriteLine("decoding '" + fname + "'..");
        var xdat = Png.ToXpng(png);
        xdat.ToFile(path, fname);

        Console.WriteLine("translating to rgba..");
        var rgbaDat = xdat.ToRgba();
        rgbaDat.ToFile(path, fname);

        // png.RemoveUnknownChunks();
        // png.ToFile(path, fname);

    }

    static void TestSpeed()
    {
        var beData = Utils.ReadFileBytes("../../../testImages", "redDot_1x1.png");

        int offset = 8;

        Stopwatch watch = new();
        watch.Start();

        var sp = new ReadOnlySpan<byte>(beData, offset, 4);
        int value = BinaryPrimitives.ReadInt32BigEndian(sp);

        watch.Stop();
        Console.WriteLine(watch.Elapsed);

        Console.WriteLine($"Value: {value:X}");
    }

}