
using System.Buffers.Binary;
using System.Diagnostics;
using System.Numerics;

namespace utils;

public class Utils
{
    static public ulong CRC32(byte[] data)
    {
        //           0b10000010_01100000_10001110_11011011_10000000_00000000_00000000_00000000;
        ulong dsor =    0b10110000_00000000_00000000_00000000_00000000_00000000_00000000_00000000;
        ulong crcMask = 0b00000000_00000000_00000000_00000000_00000000_00000000_00000000_00000111;

        int len = data.Length;
        int maxDataOff = len - 1;
        int bitLen = len * 8;
        int allDataInOff = bitLen - 4;
        int maxShift = bitLen - 1;
        int safeTailZeros = 64 - 4;
        int accShift = 0;

        ulong divend = 0;
        var initBytes = Math.Min(8, len);
        if (initBytes < 8)
        {
            for (int i = 0; i < len; i++)
            {
                divend <<= 8;
                divend |= data[i];
            }
            divend <<= 64 - len * 8;
        } else
        {
            var sp = new ReadOnlySpan<byte>(data, 0, initBytes);
            divend = BinaryPrimitives.ReadUInt64BigEndian(sp);
        }
        var dataOff = initBytes;
        int tailZeros = 64 - initBytes * 8;

        int bitDataIn;
        do
        {
            Log("div " + Convert.ToString((long)divend, 2).PadLeft(64, '0'));

            var leadZeros = BitOperations.LeadingZeroCount(divend);
            accShift += leadZeros;
            //if (accShift > maxShift)
            //{
            //    Log("safeguard");
            //    break;
            //}
            divend <<= leadZeros;

            Log("zrs " + Convert.ToString((long)divend, 2).PadLeft(64, '0'));

            tailZeros += leadZeros;
            if (tailZeros > safeTailZeros)
            {
                var wholeBytes = Math.Min(tailZeros / 8, maxDataOff - dataOff);
                if (wholeBytes > 0)
                {
                    var sp = new ReadOnlySpan<byte>(data, dataOff, wholeBytes);
                    ulong divendTail = BinaryPrimitives.ReadUInt64BigEndian(sp);
                    dataOff += wholeBytes;
                    tailZeros -= wholeBytes * 8;
                    divend |= divendTail << tailZeros;

                    Log("tal " + Convert.ToString((long)divend, 2).PadLeft(64, '0'));
                }
            }
            divend ^= dsor;

            bitDataIn = accShift - allDataInOff;

            Log("dsr " + Convert.ToString((long)dsor, 2).PadLeft(64, '0'));
            
        } while (bitDataIn < 0 && divend >> (safeTailZeros + bitDataIn) != 0);

        return divend & crcMask;
    }

    static public byte[] ReadFileBytes(string path, string name)
    {
        try
        {
            return File.ReadAllBytes(Path.Combine(path, name));
        }
        catch (Exception ex)
        {
            Log($"Utils.ReadFile : exception '{ex.Message}'");
            return Array.Empty<byte>();
        }
    }

    static public void WriteFileBytes(string path, string name, byte[] outp)
    {
        try
        {
            File.WriteAllBytes(Path.Combine(path, name), outp);
        }
        catch (Exception ex)
        {
            Log($"Utils.WriteFile : exception '{ex.Message}'");
        }
    }

    public enum LogMode
    {
        Console,
        Debug,
    }

    static readonly LogMode logMode = LogMode.Console;
    static Stopwatch? stopwatch;
    static public void LogWithTime(string log)
    {
        stopwatch ??= Stopwatch.StartNew();
        double elapsedSec = stopwatch.Elapsed.TotalSeconds;
        switch (logMode)
        {
            case LogMode.Console:
                Console.WriteLine(elapsedSec.ToString("0.00000") + " " + log);
                break;
            case LogMode.Debug:
                Debug.WriteLine(elapsedSec.ToString("0.00000") + " " + log);
                break;
            default:
                throw new NotImplementedException($"Utils.Log : logMode '{logMode}' is not supported");
        }

    }

    static public void Log(string log)
    {
        switch (logMode)
        {
            case LogMode.Console:
                Console.WriteLine(log);
                break;
            case LogMode.Debug:
                Debug.WriteLine(log);
                break;
            default:
                throw new NotImplementedException($"Utils.Log : logMode '{logMode}' is not supported");
        }

    }
}


