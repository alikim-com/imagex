
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Hashing;

namespace imagex;

internal class Program
{
    static void Main()
    {
        //TestCRC32();
        TestLoad();
    }

    static void TestCRC32()
    {
        byte[] data = [
            // 0b00110100, 0b11101100
            0x89, 0x50, 0x4e, 0x48, 0x0d, 0x63, 0xf8, 0x90,
            0x54, 0x08, 0xd7, 0x63, 0xf8, 0xcf, 0x00

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

    static void TestLoad()
    {
        var path = "../../../testImages";

        Console.WriteLine("Please enter file name to decode:");
        string fname = Console.ReadLine() ?? "";
        if(fname == "") fname = "rgb_3x3.png";

        var png = Png.FromFile(path, fname);
        Console.WriteLine(png);
        Console.WriteLine("decoding '" + fname + "'..");
        var xdat = Png.ToXdat(png);
        xdat.ToFile(path, fname);

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