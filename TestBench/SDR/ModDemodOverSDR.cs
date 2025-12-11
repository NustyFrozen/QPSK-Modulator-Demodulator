using QPSK.Models;
using NetMQ;
using NetMQ.Sockets;
using Pothosware.SoapySDR;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;
using QPSK;
namespace TestBench.SDR
{
    public class ModDemodOverSDR
    {
         static void SetupSoapyEnvironment()
        {
            var currentPath = Directory.GetCurrentDirectory();
            var soapyPath = Path.Combine(currentPath, @"SDR\SoapySDR");
            var libsPath = Path.Combine(soapyPath, @"Libs");
            Environment.SetEnvironmentVariable("SOAPY_SDR_PLUGIN_PATH",
                Path.Combine(currentPath, @"SDR\SoapySDR\root\SoapySDR\lib\SoapySDR\modules0.8-3\"));
            Environment.SetEnvironmentVariable("SOAPY_SDR_ROOT", Path.Combine(currentPath, @"SDR\SoapySDR\root\SoapySDR"));
            Environment.SetEnvironmentVariable("PATH",
                $"{Environment.GetEnvironmentVariable("PATH")};{soapyPath};{libsPath}");
        }

        public static unsafe void runTest(PublisherSocket pub)
        {
            SetupSoapyEnvironment();
            
            //testing on UHD 4.8 usrp b205
            Device Device = new Device("driver=uhd");
            int sampleRate = 1_000_000;
            var frequency = 150e6;
            var SymbolRate = sampleRate / 2;
            const float RRCAlpha = .6f;
            const int rrcSpan = 10;
            QPSKModulator mod = new QPSKModulator(sampleRate, SymbolRate, RRCAlpha,rrcSpan);
            QPSKDeModulator demod = new QPSKDeModulator(sampleRate, SymbolRate, RRCAlpha, rrcSpan);
            
            
            Device.SetSampleRate(Direction.Rx, 0, sampleRate);
            Device.SetSampleRate(Direction.Tx, 0, sampleRate);
            Device.SetFrequency(Direction.Rx, 0,
                frequency);
            Device.SetFrequency(Direction.Tx, 0,
                frequency);
            Device.SetGain(Direction.Tx, 0, 60);
           // Device.SetGainMode(Direction.Rx, 0, true);
             Device.SetGain(Direction.Rx, 0,35);
            var rxStream = Device.SetupRxStream(StreamFormat.ComplexFloat32,
                new[] { (uint)0 }, "");
            var txStream = Device.SetupTxStream(StreamFormat.ComplexFloat32,
                new[] { (uint)0 }, "");
            
            var rxMtu = rxStream.MTU;
            var txMtu = txStream.MTU;
            var results = new StreamResult();
            var rxFloatBuffer = new float[rxMtu * 2];
            int sps = sampleRate / SymbolRate;


            int rrcLen = RRCFilter.generateCoefficents(rrcSpan, RRCAlpha, sampleRate, SymbolRate).Length;
            int extra = 2 * (rrcLen - 1);        // 2*(M-1) from the math above

            int desiredComplex = (int)txMtu;          // want this many complex samples
            int upsampledLenWithoutFilter = desiredComplex - extra;
            int nSym = upsampledLenWithoutFilter / sps;
            int nBits = nSym * 2;

            var sb = new StringBuilder(nBits);
            var rand = new Random();
            for (int i = 0; i < nBits; i++)
                sb.Append((char)('0' + rand.Next(0, 2)));

            var modulated = mod.Modulate(sb.ToString());
            var modulatedQPSKSignal = modulated.toFloatInterleaved();

            var rxBufferHandle = GCHandle.Alloc(rxFloatBuffer, GCHandleType.Pinned);
            var txBufferHandle = GCHandle.Alloc(modulatedQPSKSignal, GCHandleType.Pinned);

            var sw = new Stopwatch();
            var keepTransmission = true;
            var transmitThread = new Thread(() =>
            {
                txStream.Activate();
                fixed (float* bufferPtr = modulatedQPSKSignal)
                {
                    while (keepTransmission)
                    {
                        var errorCode = txStream.Write((nint)bufferPtr, (uint)txMtu, StreamFlags.None, 0, 10_000_000,
                            out results);
                        if (errorCode is not Pothosware.SoapySDR.ErrorCode.None || results is null)
                        {
                            Console.WriteLine($"TXSTREAM -->{errorCode}");
                        }
                    }
                }
            })
            { Priority = ThreadPriority.Highest };
            transmitThread.Start();

            var samples = new List<float>();
            var totalSamples = 0;

            var readingThread = new Thread(() =>
            {
                Stopwatch sw = new Stopwatch();
                var rx_floatBuffer_asspan = rxFloatBuffer.AsSpan();
                rxStream.Activate();
                fixed (float* bufferPtr = rxFloatBuffer)
                {
                    while (keepTransmission)
                    {

                        sw.Restart();
                        var errorCode = rxStream.Read((nint)bufferPtr, (uint)rxMtu, 10_000_000, out results);

                        if (errorCode is not Pothosware.SoapySDR.ErrorCode.None || results is null)
                        {
                            Console.WriteLine($"RXSTREAM -->{errorCode}");
                            continue;
                        }

                        var IncomingSamples = rx_floatBuffer_asspan.Slice(0, (int)results.NumSamples * 2).ToArray();
                        Complex[] symbolsResults;
                        symbolsResults = demod.deModulateConstellation(IncomingSamples.toComplex());

                        byte[] frame = new byte[IncomingSamples.Length * 4];
                        Buffer.BlockCopy(IncomingSamples, 0, frame, 0, frame.Length);

                        byte[] frame2 = new byte[symbolsResults.Length * 2 * 4];
                        Buffer.BlockCopy(symbolsResults.toFloatInterleaved(), 0, frame2, 0, frame2.Length);


                        // Send single ZMQ frame without topic
                        pub.SendMoreFrame("baseband").SendFrame(frame);
                        pub.SendMoreFrame("baseband_demodulated").SendFrame(frame2);

                        sw.Stop();
                        double loopTimeSec = sw.Elapsed.TotalSeconds;

                        // 4. Compute remaining time until the "ideal" next send
                        double sleepSec = (results.NumSamples / sampleRate) - loopTimeSec;
                        if (sleepSec > 0)
                        {
                            int sleepMs = (int)(sleepSec * 1000.0);
                            if (sleepMs > 0)
                            {
                                Thread.Sleep(sleepMs);
                            }
                            double remainderSec = sleepSec - sleepMs / 1000.0;
                            if (remainderSec > 0)
                            {
                                // Short spin-wait for a tiny extra bit
                                var spin = new SpinWait();
                                var targetTicks = Stopwatch.GetTimestamp() +
                                                  (long)(remainderSec * Stopwatch.Frequency);
                                while (Stopwatch.GetTimestamp() < targetTicks)
                                    spin.SpinOnce();
                            }
                        }
                    }
                }
            }
            )
            { Priority = ThreadPriority.Highest};
            readingThread.Start();
           
            Console.ReadKey();
            keepTransmission = false;
            Thread.Sleep(200);
            rxStream.Close();
            txStream.Close();
           
        }
        static void beginRX()
        {

        }
        static void beginTX()
        {

        }
    }
}
