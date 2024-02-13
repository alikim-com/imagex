﻿
using System.Buffers.Binary;
using System.Diagnostics;
using System.Numerics;

namespace utils;

public class Utils
{
    /// <summary>
    /// Can compute CRC[1..55], depending on hardcoded polynomial;<br/>
    /// uses 64 bit ulong as a container<br/>
    /// zero initial CRC, Big Endian data order
    /// </summary>
    /// <param name="dsor">divisor polymonial loaded into highest bits of ulong; <br/>
    /// for CRC32: x32 + x26 + x23 + x22 + x16 + x12 + x11 + x10 + x8 + x7 + x5 + x4 + x2 + x1 + x0
    /// </param>
    /// <param name="dsorLen">actual bit-length of dsor; N + 1 for CRC[N]</param>
    /// <param name="divend">container holding up to 8 bytes of data being processed</param>
    /// <param name="payload">the number of data bits currently in divend, counting from msb</param>
    static public ulong CRC(byte[] data)
    {
        ulong divend = 0;
        // CRC32
        ulong dsor = 0b10000010_01100000_10001110_11011011_10000000_00000000_00000000_00000000;
        int dsorLen = 33;  

        // CRC3
        //ulong dsor = 0b10110000_00000000_00000000_00000000_00000000_00000000_00000000_00000000;
        //int dsorLen = 4;

        int len = data.Length;

        // initial load
        int bytesLoaded = Math.Min(8, len);
        if (bytesLoaded < 8)
        {
            for (int i = 0; i < len; i++)
            {
                divend <<= 8;
                divend |= data[i];
            }
            divend <<= 64 - len * 8;
        } else
        {
            var sp = new ReadOnlySpan<byte>(data, 0, bytesLoaded);
            divend = BinaryPrimitives.ReadUInt64BigEndian(sp);
        }

        int payload = bytesLoaded * 8;

        while (true)
        {
            //PrintDivendBlocks(divend, payload, "div");

            int leadZeros = BitOperations.LeadingZeroCount(divend);

            int bytesToLoad = len - bytesLoaded;
            bool zeroPayload = leadZeros >= payload;

            if (bytesToLoad == 0 && zeroPayload) break;

            if (zeroPayload)
            {
                payload = 0;
            } 
            else
            {
                payload -= leadZeros;
                divend <<= leadZeros;

                //PrintDivendBlocks(divend, payload, "zrs");
            }

            if (payload < dsorLen && bytesToLoad != 0)
            {
                var availPayloadLen = 64 - payload;
                var wholeBytes = Math.Min(availPayloadLen / 8, bytesToLoad);
                
                ulong newPayload = 0;
                for (int i = 0; i < wholeBytes; i++)
                {
                    newPayload <<= 8;
                    newPayload |= data[bytesLoaded++];
                }
                var actPayloadLen = wholeBytes * 8;
                newPayload <<= availPayloadLen - actPayloadLen;
                divend |= newPayload;

                payload += actPayloadLen;

                //Log("tal " + Convert.ToString((long)divend, 2).PadLeft(64, '0'));
            }

            divend ^= dsor;

            //Log("dsr " + Convert.ToString((long)dsor, 2).PadLeft(64, '0'));

        }

        //Log("fin " + Convert.ToString((long)divend, 2).PadLeft(64, '0'));

        return divend >> (64 - payload - (dsorLen - 1));
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
            return [];
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

    static public void PrintLongFmt(ulong res)
    {
        var strRes = Convert.ToString((long)res, 2).PadLeft(32, '0');
        var prn = string.Concat(strRes.Select((ch, i) => (i + 1) % 8 == 0 ? ch + " " : ch.ToString()));
        Log(prn);
    }

    static public void PrintDivendBlocks(ulong divend, int payload, string pref)
    {
        Console.Write(pref + " ");
        var clr = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Green;
        string divstr = Convert.ToString((long)divend, 2).PadLeft(64, '0');
        Console.Write(divstr[0..payload]);
        Console.ForegroundColor = clr;
        Console.WriteLine(divstr[payload..]);
    }
}


