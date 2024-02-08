
using System.Buffers.Binary;
using System.Diagnostics;

namespace imagex;

internal class Program
{
    static void Main(string[] args)
    {
        var png = PNG.FromFile(
            "../../../testImages", 
            "redDot_1x1.png");

        
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