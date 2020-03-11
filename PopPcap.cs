using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;


namespace PopX
{
	//	wireshark packet format
	public static class Pcap
	{
		public static T GetStruct<T>(byte[] Data)
		{
			GCHandle handle = GCHandle.Alloc(Data, GCHandleType.Pinned);
			T Obj = (T)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(T));
			handle.Free();
			return Obj;
		}

		public static T GetStruct<T>(System.Func<long, byte[]> ReadData)
		{
			var Size = Marshal.SizeOf(typeof(T));
			var Data = ReadData(Size);
			return GetStruct<T>(Data);
		}

		//	https://github.com/hokiespurs/velodyne-copter/wiki/PCAP-format
		public struct GlobalHeader  //	24 bytes
		{
			public const uint ExpectedMagicNumber = 0xA1B2C3D4;
			public const int NetworkLinkType_Ethernet = 1;
			public uint MagicNumber;
			public short VersionMajor;
			public short VersionMinor;
			public int TimeZoneCorrectionSecs;
			public int TimestampAccuracy;
			public int MaxCapturedPacketLength;
			public int NetworkLinkType; //	1=ethernet
		}

		public struct PacketHeader  //	16 bytes
		{
			public int TimestampSecs;
			public int TimestampMicroSecs;
			public int FilePacketSize;  //	less than RealPacketSize if packet data is clipped
			public int RealPacketSize;
		}

		public struct Byte6
		{
			public byte a,b,c,d,e,f;
		}
		public struct EthernetHeader // 14 bytes
		{
			//public const short EthernetType_Ipv4 = 0x0800;
			public const short EthernetType_Ipv4 = 0x0008;	//	endian!

			public Byte6 DestinationMacAddress;
			public Byte6 SourceMacAddress;
			public short EthernetType;
		}

		public static short ReverseBytes(short Value)
		{
			var a = Value & 0x00ff;
			var b = Value & 0xff00;
			return (short)((a << 8) | (b >> 8));
		}

		public struct Ipv4Header // 20 bytes
		{
			public const byte Protocol_Udp = 17;

			public int VersionNumber	{	get { return VersionNumber4_HeaderLength4 >> 4; }	}
			public int HeaderLength		{	get { return 4 * (VersionNumber4_HeaderLength4 & (0xf)); }	}	//	viewing wireshark, 5=20

			public byte VersionNumber4_HeaderLength4; //	4 bits, 4bits
			public byte TypeOfService;
			public short TotalLengthLittleEndian;
			public short TotalLength	{ get { return ReverseBytes(TotalLengthLittleEndian); } }
			public short Identification;
			public short Flags4_FragmentOffset12;
			public byte TimeToLive;
			public byte Protocol;
			public short HeaderChecksum;
			public int SoruceAddress;
			public int DestinationAddress;
		}

		public struct UdpHeader // 8 bytes
		{
			public short SourcePort;
			public short DestinationPort;
			public short Length { get { return ReverseBytes(LengthLittleEndian); } }
			public short LengthLittleEndian;
			public short Checksum;
		}

		//[NUnit.Framework.Test]
		static public void StructTest()
		{
			if (Marshal.SizeOf(typeof(GlobalHeader)) != 24)
				throw new System.Exception("Global header size incorrect");

			if (Marshal.SizeOf(typeof(PacketHeader)) != 16)
				throw new System.Exception("PacketHeader size incorrect");

			if (Marshal.SizeOf(typeof(UdpHeader)) != 8)
				throw new System.Exception("UdpHeader size incorrect");

			if (Marshal.SizeOf(typeof(Ipv4Header)) != 20)
				throw new System.Exception("Ipv4Header size incorrect");

			if (Marshal.SizeOf(typeof(EthernetHeader)) != 14)
				throw new System.Exception("EthernetHeader size incorrect");			
		}

		public static GlobalHeader ParseHeader(System.Func<long, byte[]> ReadData)
		{
			StructTest();
			var Header = GetStruct<GlobalHeader>(ReadData);
			return Header;
		}

		public static void ParseNextPacket(System.Func<long, byte[]> ReadData, GlobalHeader GlobalHeader,System.Action<byte[],int> EnumPacket)
		{
			var PacketHeader = GetStruct<PacketHeader>(ReadData);

			//	this includes headers
			var PacketData = ReadData(PacketHeader.FilePacketSize);
			long HeaderSize = 0;
			System.Func<long, byte[]> ReadPacketData = (Length) =>
			{
				var SubData = PacketData.SubArray(HeaderSize, Length);
				HeaderSize += Length;
				return SubData;
			};

			var Ipv4Size = 0;
			var EthernetSize = 0;

			if (GlobalHeader.NetworkLinkType == GlobalHeader.NetworkLinkType_Ethernet )
			{
				var EthernetHeader = GetStruct<EthernetHeader>(ReadPacketData);
				
				if (EthernetHeader.EthernetType == EthernetHeader.EthernetType_Ipv4)
				{
					var Ipv4Header = GetStruct<Ipv4Header>(ReadPacketData);
					if (Ipv4Header.VersionNumber != 4)
						throw new System.Exception("IPV4 packet header version not 4, is " + Ipv4Header.VersionNumber);

					Ipv4Size = Ipv4Header.TotalLength;

					//	length includes IPV4 header, but there may be extra data not in the struct, so pop it off
					var ExtendedHeaderLength = Ipv4Header.HeaderLength - Marshal.SizeOf(Ipv4Header);
					if (ExtendedHeaderLength > 0)
					{
						var ExtendedHeaderData = ReadPacketData(ExtendedHeaderLength);
					}

					if ( Ipv4Header.Protocol == Ipv4Header.Protocol_Udp )
					{
						var UdpHeader = GetStruct<UdpHeader>(ReadPacketData);
						EthernetSize = UdpHeader.Length;
					}
				}
			}


			var RawData = ReadPacketData(PacketHeader.FilePacketSize - HeaderSize);
			//if (RawData.Length != 1000)
			//	throw new System.Exception("Expected Rawdata=1000 ; " + RawData.Length);
			var TimeMs = PacketHeader.TimestampSecs * 1000;
			TimeMs += PacketHeader.TimestampMicroSecs / 1000;
			EnumPacket(RawData, TimeMs);
		}
	}
}
