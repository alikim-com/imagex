
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Hashing;



namespace imagex;

internal class Program
{
    static void Main(string[] args)
    {
        //var png = PNG.FromFile(
        //    "../../../testImages", 
        //    "redDot_1x1.png");

        byte[] data = [
            // 0b00110100, 0b11101100

            //0x89, 0x50, 0x4e, 0x48, 0x0d, 0x63, 0xf8, 0x90,
            //0x54, 0x08, 0xd7, 0x63, 0xf8, 0xcf, 0x00

            0xff
            //0x01, 0x02, 0x03, 0x04
            //0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 
            //0x00, 0x00, 0x00, 0x01, 0x08, 0x02, 0x00, 0x00, 0x00

        ];

        ulong res = Utils.CRC32(data);

        //Utils.Log($"{res:X} ");
        var strRes = Convert.ToString((long)res, 2).PadLeft(32, '0');
        var prn = string.Concat(strRes.Select((ch, i) => (i + 1) % 8 == 0 ? ch + " " : ch.ToString()));
        Utils.Log(prn);

        Utils.Log("-----");

        var dataNeg = data.Select(b => (byte)~b).ToArray();
        byte[] checksum = Crc32.Hash(dataNeg);
        checksum[^1] = (byte)~checksum[^1];


        string outp = "";
        foreach (byte b in checksum)
        {
            //outp += $"{b:X2} ";
            outp += Convert.ToString(b, 2).PadLeft(8, '0') + " ";
        }
        Utils.Log(outp);
    }

    static void SpeedTest()
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