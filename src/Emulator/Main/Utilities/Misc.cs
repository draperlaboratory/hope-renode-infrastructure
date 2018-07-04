//
// Copyright (c) 2010-2018 Antmicro
// Copyright (c) 2011-2015 Realtime Embedded
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals;
using System.IO;
using Dynamitey;
using System.Text;
using System.Runtime.InteropServices;
using System.Linq.Expressions;
using System.Drawing;
using Antmicro.Renode.Network;
using System.Diagnostics;
using Antmicro.Renode.Core.Structure.Registers;
using System.Threading;
using Antmicro.Renode.Debugging;

namespace Antmicro.Renode.Utilities
{
    public static class Misc
    {
        //TODO: isn't it obsolete?
        //TODO: what if memory_size should be long?
        public static List<UInt32> CreateAtags(string bootargs, uint memorySize)
        {
            var atags = new List<UInt32>
                            {
                                5u,
                                0x54410001u,
                                1u,
                                0x1000u,
                                0u,
                                4u,
                                0x54410002u,
                                memorySize,
                                0u,
                                (uint)((bootargs.Length >> 2) + 3),
                                0x54410009u
                            };

            //TODO: should be padded
            var ascii = new ASCIIEncoding();
            var bootargsByte = new List<byte>();
            bootargsByte.AddRange(ascii.GetBytes(bootargs));
            int i;
            if((bootargs.Length % 4) != 0)
            {
                for(i = 0; i < (4 - (bootargs.Length%4)); i++)
                {
                    bootargsByte.Add(0); // pad with zeros
                }
            }
            for(i = 0; i < bootargsByte.Count; i += 4)
            {
                atags.Add(BitConverter.ToUInt32(bootargsByte.ToArray(), i));
            }
            atags.Add(0u);

            atags.Add(0u); // ATAG_NONE
            return atags;
        }

        public static bool IsPeripheral(object o)
        {
            return o is IPeripheral;
        }

        public static bool IsPythonObject(object o)
        {
            return o.GetType().GetFields().Any(x => x.Name == ".class");
        }

        public static bool IsPythonType(Type t)
        {
            return t.GetFields().Any(x => x.Name == ".class");
        }

        public static string GetPythonName(object o)
        {
            var cls = Dynamic.InvokeGet(o, ".class");
            return cls.__name__;
        }

        public static IEnumerable<MethodInfo> GetAllMethods(this Type t, bool recursive = true)
        {
            if(t == null)
            {
                return Enumerable.Empty<MethodInfo>();
            }
            if(recursive)
            {
                return t.GetMethods(DefaultBindingFlags).Union(GetAllMethods(t.BaseType));
            }
            return t.GetMethods(DefaultBindingFlags);
        }

        public static IEnumerable<FieldInfo> GetAllFields(this Type t, bool recursive = true)
        {
            if(t == null)
            {
                return Enumerable.Empty<FieldInfo>();
            }
            if(recursive)
            {
                return t.GetFields(DefaultBindingFlags).Union(GetAllFields(t.BaseType));
            }
            return t.GetFields(DefaultBindingFlags);
        }


        public static byte[] ReadBytes(this Stream stream, int count)
        {
            var buffer = new byte[count];
            var read = 0;
            while(read < count)
            {
                var readInThisIteration = stream.Read(
                    buffer,
                    read,
                    count - read
                );
                if(readInThisIteration == 0)
                {
                    throw new EndOfStreamException(string.Format(
                        "End of stream encountered, only {0} bytes could be read.",
                        read
                    )
                    );
                }
                read += readInThisIteration;
            }
            return buffer;
        }

        public static int KB(this int value)
        {
            return 1024 * value;
        }

        public static int MB(this int value)
        {
            return 1024 * 1024 * value;
        }

        /// <summary>
        /// Computes which power of two is given number. You can only use this function if you know
        /// that this number IS a power of two.
        /// </summary>
        public static int Logarithm2(int value)
        {
            return MultiplyDeBruijnBitPosition2[(int)((uint)(value * 0x077CB531u) >> 27)];
        }

        public static byte[] AsRawBytes<T>(this T structure) where T : struct
        {
            var size = Marshal.SizeOf(typeof(T));
            var result = new byte[size];
            var bufferPointer = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(structure, bufferPointer, false);
            Marshal.Copy(bufferPointer, result, 0, size);
            Marshal.FreeHGlobal(bufferPointer);
            return result;
        }

        public static String NormalizeBinary(double what)
        {
            var prefix = (what < 0) ? "-" : "";
            if(what == 0)
            {
                return "0";
            }
            if(what < 0)
            {
                what = -what;
            }
            var power = (int)Math.Log(what, 2);
            var index = power / 10;
            if(index >= BytePrefixes.Length)
            {
                index = BytePrefixes.Length - 1;
            }
            what /= Math.Pow(2, 10 * index);
            return string.Format(
                "{0}{1:0.##}{2}",
                prefix,
                what,
                BytePrefixes[index]
            );
        }

        public static void ByteArrayWrite(long offset, uint value, byte[] array)
        {
            var index = (int)(offset);
            var bytes = BitConverter.GetBytes(value);
            for(var i = 0; i < 4; i++)
            {
                array[index + i] = bytes[i];
            }
        }

        public static uint ByteArrayRead(long offset, byte[] array)
        {
            var index = (int)(offset);
            var bytes = new byte[4];
            for(var i = 0; i < 4; i++)
            {
                bytes[i] = array[index + i];
            }
            return BitConverter.ToUInt32(bytes, 0);
        }

        public static String NormalizeDecimal(double what)
        {
            var prefix = (what < 0) ? "-" : "";
            if(what == 0)
            {
                return "0";
            }
            if(what < 0)
            {
                what = -what;
            }
            var digits = Convert.ToInt32(Math.Floor(Math.Log10(what)));
            var power = (long)(3 * Math.Round((digits / 3.0)));
            var index = power / 3 + ZeroPrefixPosition;
            if(index < 0)
            {
                index = 0;
                power = 3 * (1 + ZeroPrefixPosition - SIPrefixes.Length);
            } else if(index >= SIPrefixes.Length)
            {
                index = SIPrefixes.Length - 1;
                power = 3 * (SIPrefixes.Length - ZeroPrefixPosition - 1);
            }
            what /= Math.Pow(10, power);
            var unit = SIPrefixes[index];
            what = Math.Round(what, 2);
            return prefix + what + unit;
        }

        public static string GetShortName(object o)
        {
            if(Misc.IsPythonObject(o))
            {
                return Misc.GetPythonName(o);
            }
            var type = o.GetType();
            return type.Name;
        }

        public static int NextPowerOfTwo(int value)
        {
            value--;
            value |= value >> 1;
            value |= value >> 2;
            value |= value >> 4;
            value |= value >> 8;
            value |= value >> 16;
            value++;
            return value;
        }

        public static void Times(this int times, Action<int> action)
        {
            for(var i = 0; i < times; i++)
            {
                action(i);
            }
        }

        public static void Times(this int times, Action action)
        {
            for(var i = 0; i < times; i++)
            {
                action();
            }
        }

        public static String Indent(this String value, int count)
        {
            return "".PadLeft(count) + value;
        }

        public static String Indent(this String value, int count, char fill)
        {
            return "".PadLeft(count, fill) + value;
        }

        public static String Outdent(this String value, int count)
        {
            return value + "".PadLeft(count);
        }

        public static Boolean StartsWith(this String source, char value)
        {
            if (source.Length > 0)
            {
                return source[0] == value;
            }
            return false;
        }

        public static Boolean EndsWith(this String source, char value)
        {
            if (source.Length > 0)
            {
                return source[source.Length - 1] == value;
            }
            return false;
        }

        public static String Trim(this String value, String toCut)
        {
            if (!value.StartsWith(toCut))
            {
                return value;
            }
            return value.Substring(toCut.Length);
        }

        public static int IndexOf<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            var i = 0;
            foreach (var element in source)
            {
                if (predicate(element))
                    return i;

                i++;
            }
            return -1;
        }

        public static int LastIndexOf<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            var revSource = source.Reverse();
            var i = revSource.Count() - 1;
            foreach (var element in revSource)
            {
                if (predicate(element))
                    return i;

                i--;
            }
            return -1;
        }

        public static string Stringify<TSource>(this IEnumerable<TSource> source, string separator = " ")
        {
            return Stringify(source.Select(x => x == null ? String.Empty : x.ToString()), separator);
        }

        public static string Stringify(this IEnumerable<string> source, string separator = " ")
        {
            if(source.Any())
            {
                return source.Aggregate((x, y) => x + separator + y);
            }
            return String.Empty;
        }

        // MoreLINQ - Extensions to LINQ to Objects
        // Copyright (c) 2008 Jonathan Skeet. All rights reserved.
        public static IEnumerable<TSource> DistinctBy<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector,
            IEqualityComparer<TKey> comparer = null)
        {
            if(source == null)
            {
                throw new ArgumentNullException("source");
            }
            if(keySelector == null)
            {
                throw new ArgumentNullException("keySelector");
            }
            var knownKeys = new HashSet<TKey>(comparer);
            foreach (var element in source)
            {
                if (knownKeys.Add(keySelector(element)))
                {
                    yield return element;
                }
            }
        }

        /// <summary>Adds elements to the end of an IEnumerable.</summary>
        /// <typeparam name="T">Type of enumerable to return.</typeparam>
        /// <returns>IEnumerable containing all the input elements, followed by the
        /// specified additional elements.</returns>
        public static IEnumerable<T> Append<T>(this IEnumerable<T> source, params T[] element)
        {
            if(source == null)
            {
                throw new ArgumentNullException("source");
            }
            return ConcatIterator(element, source, false);
        }

        /// <summary>Adds elements to the start of an IEnumerable.</summary>
        /// <typeparam name="T">Type of enumerable to return.</typeparam>
        /// <returns>IEnumerable containing the specified additional elements, followed by
        /// all the input elements.</returns>
        public static IEnumerable<T> Prepend<T>(this IEnumerable<T> tail, params T[] head)
        {
            if(tail == null)
            {
                throw new ArgumentNullException("tail");
            }
            return ConcatIterator(head, tail, true);
        }

        private static IEnumerable<T> ConcatIterator<T>(T[] extraElements,
            IEnumerable<T> source, bool insertAtStart)
        {
            if(insertAtStart)
            {
                foreach(var e in extraElements)
                {
                    yield return e;
                }
            }
            foreach(var e in source)
            {
                yield return e;
            }
            if(!insertAtStart)
            {
                foreach(var e in extraElements)
                {
                    yield return e;
                }
            }
        }

        public static byte HiByte(this UInt16 value)
        {
            return (byte)((value >> 8) & 0xFF);
        }

        public static byte LoByte(this UInt16 value)
        {
            return (byte)(value & 0xFF);
        }

        public static string FromResourceToTemporaryFile(this Assembly assembly, string resourceName)
        {
            Stream libraryStream = assembly.GetManifestResourceStream(resourceName);
            if(libraryStream == null)
            {
                if(File.Exists(resourceName))
                {
                    libraryStream = new FileStream(resourceName, FileMode.Open, FileAccess.Read, FileShare.None);
                }
                if(libraryStream == null)
                {
                    throw new ArgumentException(string.Format("Cannot find library {0}", resourceName));
                }
            }
            return CopyToFile(libraryStream, resourceName);
        }

        public static void Copy(this Stream from, Stream to)
        {
            var buffer = new byte[4096];
            int read;
            do
            {
                read = from.Read(buffer, 0, buffer.Length);
                if(read <= 0) // to workaround ionic zip's bug
                {
                    break;
                }
                to.Write(buffer, 0, read);
            }
            while(true);
        }

        public static void GetPixelFromPngImage(string fileName, int x, int y, out byte r, out byte g, out byte b)
        {
            Bitmap bitmap;
            if(fileName == LastBitmapName)
            {
                bitmap = LastBitmap;
            }
            else
            {
                bitmap = new Bitmap(fileName);
                LastBitmap = bitmap;
                LastBitmapName = fileName;
            }
            var color = bitmap.GetPixel(x, y);
            r = color.R;
            g = color.G;
            b = color.B;
        }

        public static bool TryGetRootDirectory(out string directory)
        {
            return TryGetRootDirectory(AppDomain.CurrentDomain.BaseDirectory, out directory);
        }

        public static bool TryGetRootDirectory(string baseDirectory, out string directory)
        {
            directory = null;
            var currentDirectory = new DirectoryInfo(baseDirectory);
            while(currentDirectory != null)
            {
                var indicatorFiles = Directory.GetFiles(currentDirectory.FullName, ".renode-root");
                if(indicatorFiles.Length == 1)
                {
                    var content = File.ReadAllLines(Path.Combine(currentDirectory.FullName, indicatorFiles[0]));
                    if(content.Length == 1 && content[0] == "5344ec2a-1539-4017-9ae5-a27c279bd454")
                    {
                        directory = currentDirectory.FullName;
                        return true;
                    }
                }
                currentDirectory = currentDirectory.Parent;
            }
            return false;
        }

        public static TimeSpan Multiply(this TimeSpan multiplicand, int multiplier)
        {
            return TimeSpan.FromTicks(multiplicand.Ticks * multiplier);
        }

        public static TimeSpan Multiply(this TimeSpan multiplicand, double multiplier)
        {
            return TimeSpan.FromTicks((long)(multiplicand.Ticks * multiplier));
        }

        public static String FormatWith(this String @this, params object[] args)
        {
            if(@this == null)
            {
                throw new ArgumentNullException("this");
            }
            if(args == null)
            {
                throw new ArgumentNullException("args");
            }
            return String.Format(@this, args);

        }

        private static string CopyToFile(Stream libraryStream, string fileNameSuffix = null)
        {
            try
            {
                var libraryFile = TemporaryFilesManager.Instance.GetTemporaryFile(fileNameSuffix);
                using(var destination = new FileStream(libraryFile, FileMode.Open, FileAccess.Write, FileShare.None))
                {
                    libraryStream.Copy(destination);
                    Logger.Noisy(String.Format("Library copied to {0}.", libraryFile));
                }
                return libraryFile;
            }
            catch(IOException e)
            {
                throw new InvalidOperationException(String.Format("Error while copying file: {0}.", e.Message));
            }
            finally
            {
                if(libraryStream != null)
                {
                    libraryStream.Close();
                }
            }
        }

        private static ushort ComputeHeaderIpChecksum(byte[] header, int start, int length)
        {
            ushort word16;
            var sum = 0L;
            for (var i = start; i < (length + start); i+=2)
            {
                if(i - start == 10)
                {
                    //These are IP Checksum fields.
                    continue;
                }
                word16 = (ushort)(((header[i] << 8 ) & 0xFF00)
                    + (header[i + 1] & 0xFF));
                sum += (long)word16;
            }

            while ((sum >> 16) != 0)
            {
                sum = (sum & 0xFFFF) + (sum >> 16);
            }
            sum = ~sum;
            return (ushort)sum;
        }

        // Calculates the TCP checksum using the IP Header and TCP Header.
        // Ensure the TCPHeader contains an even number of bytes before passing to this method.
        // If an odd number, pad with a 0 byte just for checksumming purposes.
        static ushort GetPacketChecksum(byte[] packet, int startOfIp, int startOfPayload, bool withPseudoHeader)
        {
            var sum = 0u;
            // Protocol Header
            for (var x = startOfPayload; x < packet.Length - 1; x += 2)
            {
                sum += Ntoh(BitConverter.ToUInt16(packet, x));
            }
            if((packet.Length - startOfPayload) % 2 != 0)
            {
                //odd length
                sum += (ushort)((packet[packet.Length - 1] << 8) | 0x00);
            }
            if(withPseudoHeader)
            {
                // Pseudo header - Source Address
                sum += Ntoh(BitConverter.ToUInt16(packet, startOfIp + 12));
                sum += Ntoh(BitConverter.ToUInt16(packet, startOfIp + 14));
                // Pseudo header - Dest Address
                sum += Ntoh(BitConverter.ToUInt16(packet, startOfIp + 16));
                sum += Ntoh(BitConverter.ToUInt16(packet, startOfIp + 18));
                // Pseudo header - Protocol
                sum += Ntoh(BitConverter.ToUInt16(new byte[] { 0, packet[startOfIp + 9] }, 0));
                // Pseudo header - TCP Header length
                sum += (ushort)(packet.Length - startOfPayload);
            }
            // 16 bit 1's compliment
            while ((sum >> 16) != 0) 
            { 
                sum = ((sum & 0xFFFF) + (sum >> 16)); 
            }
            return (ushort)~sum;
        }

        private static ushort Ntoh(UInt16 input)
        {
            int x = System.Net.IPAddress.NetworkToHostOrder(input);
            return (ushort) (x >> 16);
        }


        public enum TransportLayerProtocol 
        {
            ICMP = 0x1,
            TCP = 0x6,
            UDP = 0x11
        }
        public enum PacketType
        {
            IP = 0x800,
            ARP = 0x806,
        }

        //TODO: Support for ipv6
        public static void FillPacketWithChecksums(IPeripheral source, byte[] packet, params TransportLayerProtocol[] interpretedProtocols)
        {
            if (packet.Length < MACLength) {
                source.Log(LogLevel.Error, String.Format("Expected packet of at least {0} bytes, got {1}.", MACLength, packet.Length));
                return;
            }
            var packet_type = (PacketType) ((packet[12] << 8) | packet[13]);
            if (packet_type == PacketType.ARP) {
                // ARP
                return;
            } else if (packet_type != PacketType.IP) {
                source.Log(LogLevel.Error, String.Format("Unknown packet type: 0x{0:X}. Supported are: 0x800 (IP) and 0x806 (ARP).", (ushort)packet_type));
                return;
            }
            if (packet.Length < (MACLength+12)) {
                source.Log(LogLevel.Error, "IP Packet is too short!");
                return;
            }
            // IPvX
            if ((packet[MACLength] >> 4) != 0x04) {
                source.Log(LogLevel.Error, String.Format("Only IPv4 packets are supported. Got IPv{0}", (packet[MACLength] >> 4)));
                return;
            }
            // IPv4
            var ipLength = (packet[MACLength] & 0x0F) * 4;
            if(ipLength != 0)
            {
                var ipChecksum = ComputeHeaderIpChecksum(packet, MACLength, ipLength);
                packet[MACLength + 10] = (byte)(ipChecksum >> 8);
                packet[MACLength + 11] = (byte)(ipChecksum & 0xFF);
            } else {
                source.Log(LogLevel.Error, "Something is wrong - IP packet of len 0");
            }
            if(interpretedProtocols != null && interpretedProtocols.Contains((TransportLayerProtocol)packet[MACLength + 9]))
            {
                var payloadStart = MACLength + ipLength;
                var protocol = (TransportLayerProtocol)packet[MACLength + 9];
                var checksum = GetPacketChecksum(packet, MACLength, payloadStart, protocol != TransportLayerProtocol.ICMP);
                switch(protocol)
                {
                case TransportLayerProtocol.ICMP:
                    packet[payloadStart + 2] = (byte)((checksum >> 8) & 0xFF);
                    packet[payloadStart + 3] = (byte)((checksum ) & 0xFF);
                    break;
                case TransportLayerProtocol.TCP:
                    packet[payloadStart + 16] = (byte)((checksum >> 8) & 0xFF);
                    packet[payloadStart + 17] = (byte)((checksum ) & 0xFF);
                    break;
                case TransportLayerProtocol.UDP:
                    packet[payloadStart + 6] = (byte)((checksum >> 8) & 0xFF);
                    packet[payloadStart + 7] = (byte)((checksum ) & 0xFF);
                    break;
                default:
                    throw new NotImplementedException();
                }
            }
        }

        public static string DumpPacket(EthernetFrame packet, bool isSend, Machine machine)
        {           
            var builder = new StringBuilder();
            string machName;
            if(!EmulationManager.Instance.CurrentEmulation.TryGetMachineName(machine, out machName))
            {
                //probably the emulation is closing now, just return.
                return string.Empty;
            }
            if(isSend)
            {
                builder.AppendLine(String.Format("Sending packet from {0}, length: {1}", machName, packet.Bytes.Length));
            }
            else
            {
                builder.AppendLine(String.Format("Receiving packet on {0}, length: {1}", machName, packet.Bytes.Length));
            }
            builder.Append(packet.ToString());
            return builder.ToString();
        }

        public static void Swap(ref int a, ref int b)
        {
            var temporary = a;
            a = b;
            b = temporary;
        }

        public static bool CalculateUnitSuffix(double value, out double newValue, out string unit)
        {
            var units = new [] { "B", "KB", "MB", "GB", "TB" };

            var v = value;
            var i = 0;
            while(i < units.Length - 1 && Math.Round(v / 1024) >= 1)
            {
                v /= 1024;
                i++;
            }

            newValue = v;
            unit = units[i];

            return true;
        }

        public static string ToOrdinal(this int num)
        {
            if(num <= 0)
            {
                return num.ToString();
            }
            switch(num % 100)
            {
            case 11:
            case 12:
            case 13:
                return num + "th";
            }

            switch(num % 10)
            {
            case 1:
                return num + "st";
            case 2:
                return num + "nd";
            case 3:
                return num + "rd";
            default:
                return num + "th";
            }
        }

        private const int MACLength = 14;

        private static string LastBitmapName = "";
        private static Bitmap LastBitmap;

        private const BindingFlags DefaultBindingFlags = BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.DeclaredOnly;

        private static readonly string[] SIPrefixes = {
                "p",
                "n",
                "µ",
                "m",
                "",
                "k",
                "M",
                "G",
                "T"
            };
        private static readonly string[] BytePrefixes = {
                "",
                "Ki",
                "Mi",
                "Gi",
                "Ti"
            };
        private const int ZeroPrefixPosition = 4;
        private static readonly int[] MultiplyDeBruijnBitPosition2 =
         {
           0, 1, 28, 2, 29, 14, 24, 3, 30, 22, 20, 15, 25, 17, 4, 8,
           31, 27, 13, 23, 21, 19, 16, 7, 26, 12, 18, 6, 11, 5, 10, 9
         };

        /// <summary>
        /// Checks if the current user is a root.
        /// </summary>
        /// <value><c>true</c> if is root; otherwise, <c>false</c>.</value>
        public static bool IsRoot
        {
            get { return Environment.UserName == "root"; }
        }

        public static bool IsOnOsX
        {
            get
            {
                if(Environment.OSVersion.Platform == PlatformID.MacOSX)
                {
                    return true;
                }
                return Directory.Exists("/Library") && Directory.Exists("/Applications");
            }
        }

        public static bool IsCommandAvaialble(string command)
        {
            var verifyProc = new Process();
            verifyProc.StartInfo.UseShellExecute = false;
            verifyProc.StartInfo.RedirectStandardError = true;
            verifyProc.StartInfo.RedirectStandardInput = true;
            verifyProc.StartInfo.RedirectStandardOutput = true;
            verifyProc.EnableRaisingEvents = false;
            verifyProc.StartInfo.FileName = "which";
            verifyProc.StartInfo.Arguments = command;

            verifyProc.Start();

            verifyProc.WaitForExit();
            return verifyProc.ExitCode == 0;
        }

        public static string PrettyPrintFlagsEnum(dynamic enumeration)
        {
            var values = new List<string>();
            foreach(dynamic value in Enum.GetValues(enumeration.GetType()))
            {
                if((enumeration & value) != 0)
                {
                    values.Add(value.ToString());
                }
            }
            return values.Count == 0 ? "-" : values.Aggregate((x, y) => x + ", " + y);
        }

        public static bool TryGetMatchingSignature(IEnumerable<Type> signatures, MethodInfo mi, out Type matchingSignature)
        {
            matchingSignature = signatures.FirstOrDefault(x => HasMatchingSignature(x, mi));
            return matchingSignature != null;
        }

        public static bool HasMatchingSignature(Type delegateType, MethodInfo mi)
        {
            var delegateMethodInfo = delegateType.GetMethod("Invoke");

            return mi.ReturnType == delegateMethodInfo.ReturnType &&
                mi.GetParameters().Select(x => x.ParameterType).SequenceEqual(delegateMethodInfo.GetParameters().Select(x => x.ParameterType));
        }

        public static int Clamp(this int a, int b, int c)
        {
            if(a < b)
            {
                return b;
            }
            if(a > c)
            {
                return c;
            }
            return a;
        }

        public static string[] Split(this string value, int size)
        {
            var ind = 0;
            return value.GroupBy(x => ind++ / size).Select(x => string.Join("", x)).ToArray();
        }

        public static void SetBit(this IValueRegisterField field, byte index, bool value)
        {
            var val =  field.Value;
            BitHelper.SetBit(ref val, index, value);
            field.Value = val;
        }

        public static ulong InMicroseconds(this TimeSpan ts)
        {
            return (ulong)(ts.Ticks / 10);
        }

        public static void WaitWhile(this object @this, Func<bool> condition, string reason)
        {
            @this.Trace($"Waiting for '{reason}'...");
            while(condition())
            {
                Monitor.Wait(@this);
            }
            @this.Trace($"Waiting for '{reason}' finished.");
        }
    }
}

