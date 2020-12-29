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

			public string ToHexString()
			{
				return System.BitConverter.ToString(new byte[]{ a,b,c,d,e,f});
			}
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

		public static ushort ReverseBytes(ushort Value)
		{
			var a = Value & 0x00ff;
			var b = Value & 0xff00;
			return (ushort)((a << 8) | (b >> 8));
		}

		public struct Ipv4Header // 20 bytes
		{
			public const byte Protocol_Udp = 17;
			public const ushort Flag_Reserved = 0x8000;
			public const ushort Flag_DontFragment = 0x4000;
			public const ushort Flag_MoreFragments = 0x2000;
			public const ushort Flag_FragmentOffsetMask = 0x1FFF;

			public int VersionNumber	{	get { return VersionNumber4_HeaderLength4 >> 4; }	}
			public int HeaderLength		{	get { return 4 * (VersionNumber4_HeaderLength4 & (0xf)); }	}	//	viewing wireshark, 5=20
			public bool HasMoreFragments { get { return (Flags4_FragmentOffset12 & Flag_MoreFragments)!=0; } }
			public int NextFragmentOffset { get { return ReverseBytes((ushort)(Flags4_FragmentOffset12 & Flag_FragmentOffsetMask)) >> 5; } }		//	gr: need to sort little endian reverse vs flags. gr: argh >>5 is surely wrong. 3 bits for flags... and we've reversed t

			public byte VersionNumber4_HeaderLength4; //	4 bits, 4bits
			public byte TypeOfService;
			public ushort TotalLengthLittleEndian;
			public ushort TotalLength	{ get { return ReverseBytes(TotalLengthLittleEndian); } }
			public ushort IdentificationLittleEndian;
			public ushort Identification { get { return ReverseBytes(IdentificationLittleEndian); } }
			public ushort Flags4_FragmentOffset12LittleEndian;
			public ushort Flags4_FragmentOffset12 { get { return ReverseBytes(Flags4_FragmentOffset12LittleEndian); } }
			public byte TimeToLive;
			public byte Protocol;
			public ushort HeaderChecksum;
			public uint SourceAddress;		//	4x byte
			public uint DestinationAddress; //	4x byte
		}

		public struct UdpHeader // 8 bytes
		{
			public short SourcePortLittleEndian;
			public short DestinationPortLittleEndian;
			public short Length { get { return ReverseBytes(LengthLittleEndian); } }
			public short LengthLittleEndian;
			public short Checksum;

			public short SourcePort { get { return ReverseBytes(SourcePortLittleEndian); } }
			public short DestinationPort { get { return ReverseBytes(DestinationPortLittleEndian); } }
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

		public static string GetIpAddress(uint Address8888)
		{
			var a = (Address8888 >> 24) & 0xff;
			var b = (Address8888 >> 16) & 0xff;
			var c = (Address8888 >> 8) & 0xff;
			var d = (Address8888 >> 0) & 0xff;
			return string.Format("%d.%d.%d.%d", a, b, c, d );
		}

		/*
		static Ipv4Header? ReadNextIPV4Packet(System.Func<long, byte[]> ReadData)
		{
			
		}
		*/

		public static void ParseNextPacket(System.Func<long, byte[]> ReadData, GlobalHeader GlobalHeader,System.Action<byte[],ulong,string> EnumPacket,bool StreamNameIsDestinationPort,System.Action<string> Debug)
		{
			List<byte> FinalPacketData = new List<byte>();
			Ipv4Header? FinalIpv4Header = null;
			PacketHeader? FinalPacketHeader = null;
			List<int> PacketSizes = new List<int>();

			System.Func<long, byte[]> PopFinalPacketData = (long Length) =>
			{
				var Popped = FinalPacketData.GetRange(0, (int)Length);
				FinalPacketData.RemoveRange(0, (int)Length);
				PacketSizes[0] -= (int)Length;  //	if this goes negative we should subtract from next
				return Popped.ToArray();
			};


			//	gr: safety loop
			for ( int i=0;	i<100;	i++ )
			{
				var IsFirstPacket = i == 0;
				var MorePackets = false;
				var PacketHeader = GetStruct<PacketHeader>(ReadData);
				if ( !FinalPacketHeader.HasValue )
					FinalPacketHeader = PacketHeader;

				//	this includes headers
				var PacketData = ReadData(PacketHeader.FilePacketSize);
				//Debug.Log("Packet size " + PacketHeader.FilePacketSize);
				long HeaderSize = 0;
				System.Func<long, byte[]> ReadPacketData = (Length) =>
				{
					var SubData = PacketData.SubArray(HeaderSize, Length);
					HeaderSize += Length;
					return SubData;
				};

				var Ipv4Size = 0;
				var ExpectedPosition = 0;	//	for following-fragments, this is where it's position should be in the combined packet, so this can check if some data is missing

				//Debug.Log("Packet size " + PacketHeader.FilePacketSize);

				if (GlobalHeader.NetworkLinkType == GlobalHeader.NetworkLinkType_Ethernet)
				{
					var EthernetHeader = GetStruct<EthernetHeader>(ReadPacketData);

					if (EthernetHeader.EthernetType == EthernetHeader.EthernetType_Ipv4)
					{
						var Ipv4Header = GetStruct<Ipv4Header>(ReadPacketData);
						if (Ipv4Header.VersionNumber != 4)
							throw new System.Exception("IPV4 packet header version not 4, is " + Ipv4Header.VersionNumber);

						if (!FinalIpv4Header.HasValue)
							FinalIpv4Header = Ipv4Header;

						MorePackets = Ipv4Header.HasMoreFragments;
						ExpectedPosition = Ipv4Header.NextFragmentOffset;

						Ipv4Size = Ipv4Header.TotalLength;

						//	length includes IPV4 header, but there may be extra data not in the struct, so pop it off
						var SizeOfIpv4Header = Marshal.SizeOf(Ipv4Header);
						var ExtendedHeaderLength = Ipv4Header.HeaderLength - SizeOfIpv4Header;
						if (ExtendedHeaderLength > 0)
						{
							var ExtendedHeaderData = ReadPacketData(ExtendedHeaderLength);
						}

						//	gr: pop ou the IPV4 protocol header from the assembled packet, not here.
						//		that way, pcap's total data sizes align
					}
					else
					{
						//	non ipv4
						//StreamName += "NonIpV4 " + EthernetHeader.DestinationMacAddress.ToHexString();
					}
				}
				else
				{
					//StreamName = "Non-Ethernet Stream <" + GlobalHeader.NetworkLinkType + ">";
				}

				//Debug.Log("Packet size " + PacketHeader.FilePacketSize);

				var RawData = ReadPacketData(PacketHeader.FilePacketSize - HeaderSize);
				//if (RawData.Length != 1000)
				//	throw new System.Exception("Expected Rawdata=1000 ; " + RawData.Length);
				if (FinalPacketData.Count != ExpectedPosition)
					Debug("New packet data expected to be at " + ExpectedPosition + " but current total is " + FinalPacketData.Count);
				FinalPacketData.AddRange(RawData);
				PacketSizes.Add(RawData.Length);

				if (!MorePackets)
					break;
			}

			if (!FinalIpv4Header.HasValue)
				throw new System.Exception("Non-IPV4 packet, ignored x" + FinalPacketData.Count);

			var StreamName = GetIpAddress(FinalIpv4Header.Value.DestinationAddress);
			var FinalIpv4Header_ = FinalIpv4Header.Value;

			//	gr: this only occurs once at the start
			if (FinalIpv4Header_.Protocol == Ipv4Header.Protocol_Udp)
			{
				var UdpHeader = GetStruct<UdpHeader>(PopFinalPacketData);
				StreamName = "Port" + UdpHeader.DestinationPort;
			}

			var TotalSize = 0;
			foreach (var ps in PacketSizes)
				TotalSize += ps;
			Debug("Packet " + StreamName + " x"+ TotalSize + " [" + string.Join(",", PacketSizes) + "]");

			ulong TimeMs = (ulong)FinalPacketHeader.Value.TimestampSecs * 1000;
			TimeMs += (ulong)FinalPacketHeader.Value.TimestampMicroSecs / 1000;
			EnumPacket(FinalPacketData.ToArray(), TimeMs, StreamName);
		}
	}
}
