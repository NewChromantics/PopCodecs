using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace PopX
{
	public static class H264
	{
		//	how many bytes is the avcc header
		public enum NaluFormat
		{
			Avcc1,  //	size is 1 byte
			Avcc2,  //	size is 2 bytes
			Avcc4,  //	size is 4 bytes
			Annexb3,    //	0x0 0x0 0x1
			Annexb4,    //	0x0 0x0 0x0 0x1
		};

		public enum NaluType
		{
			SPS = 7,
			PPS = 8,
			IFrame = 5,
			PFrame = 1,
			AccessUnitDelimiter = 9,
			SupplementalEnhancementInformation = 6,	//	https://github.com/schiermike/h264-sei-parser
		};

		public struct AvccHeader
		{
			public int AvccProfile;
			public int NaluLengthMinusOne;
			public List<byte[]> SPSs;
			public List<byte[]> PPSs;

			public int NaluLength
			{
				get
				{
					return NaluLengthMinusOne + 1;
				}
			}

			public AvccHeader(int AvccProfile,int NaluLengthMinusOne)
			{
				this.AvccProfile = AvccProfile;
				this.NaluLengthMinusOne = NaluLengthMinusOne;
				SPSs = new List<byte[]>();
				PPSs = new List<byte[]>();
			}

		};

		public static NaluFormat GetNaluFormat(byte[] Data)
		{
			if (Data[0] == 0 && Data[1] == 0 && Data[2] == 1)
				return NaluFormat.Annexb3;
			if (Data[0] == 0 && Data[1] == 0 && Data[2] == 0 && Data[3] == 1)
				return NaluFormat.Annexb4;

			var Size1 = Data[0];
			var Size2 = (Data[0]<<0) | (Data[1] << 8);
			var Size4 = (Data[0]<< 0) | (Data[1] << 8) | (Data[2] << 16) | (Data[3] << 24);

			//	very rough guess
			if (Size4 <= Data.Length)
				return NaluFormat.Avcc4;
			if (Size2 <= Data.Length)
				return NaluFormat.Avcc2;
			if (Size1 <= Data.Length)
				return NaluFormat.Avcc1;

			throw new System.Exception("Cannot determine h264 nalu type");
		}

		public static int Get8(byte[] Data, ref int StartIndex) { return PopX.Mpeg4.Get8(Data, ref StartIndex); }
		public static int Get16(byte[] Data, ref int StartIndex) { return PopX.Mpeg4.Get16(Data, ref StartIndex); }
		public static int Get32(byte[] Data, ref int StartIndex) { return PopX.Mpeg4.Get32(Data, ref StartIndex); }
		public static int Get32_BigEndian(byte[] Data, ref int StartIndex) { return PopX.Mpeg4.Get32_BigEndian(Data, ref StartIndex); }
		public static byte[] GetN(byte[] Data, int Length, ref int StartIndex) { return PopX.Mpeg4.GetN(Data, Length, ref StartIndex); }

		//	split avcc packets
		public static AvccHeader ParseAvccHeader(byte[] Data)
		{
			//	https://stackoverflow.com/a/24890903/355753
			int Offset = 0;
			var Version = Get8(Data,ref Offset);
			var Profile = Get8(Data, ref Offset);
			var Compatibility = Get8(Data, ref Offset);
			var Level = Get8(Data, ref Offset);
			var ReservedAndNaluLengthMinusOne = Get8(Data, ref Offset);
			var ReservedAndSpsCount = Get8(Data, ref Offset);
			var NaluLengthMinusOne = ReservedAndNaluLengthMinusOne & ((1 << 2) - 1);
			var SpsCount = ReservedAndSpsCount & ((1 << 5) - 1);

			var Header = new AvccHeader(Profile, NaluLengthMinusOne);

			for (int i = 0; i < SpsCount;	i++ )
			{
				var SpsSize = Get16(Data,ref Offset);
				var Sps = GetN(Data, SpsSize, ref Offset);
				Header.SPSs.Add( Sps );
			}

			var PpsCount = Get8(Data,ref Offset);

			for (int i = 0; i < PpsCount; i++)
			{
				var PpsSize = Get16(Data, ref Offset);
				var Pps = GetN(Data, PpsSize, ref Offset);
				Header.PPSs.Add(Pps);
			}

			return Header;
		}

		static int GetLength(byte[] Data,int LengthSize,ref int Offset)
		{
			if (LengthSize == 1)
			{
				return Get8(Data, ref Offset);
			}
			else if (LengthSize == 2)
			{
				return Get16(Data, ref Offset);
			}
			else if (LengthSize == 4)
			{
				return Get32(Data, ref Offset);
			}
			else
			{
				throw new System.Exception("Unhandled nalu length " + LengthSize);
			}
		}

		static void SplitPacket_Avcc(AvccHeader Header, byte[] Packet,System.Action<byte[]> EnumPacket)
		{
			int Offset = 0;
			while ( Offset < Packet.Length )
			{
				//	read length
				var Length = GetLength(Packet, Header.NaluLength, ref Offset);
				var Data = GetN(Packet, Length, ref Offset);
				EnumPacket(Data);
			}

		}


		public static List<byte[]> AvccToAnnexb4(AvccHeader Header,byte[] Packet)
		{
			var AnnexPacketDatas = new List<byte[]>();

			System.Action<byte[]> EnumPacket = (RawPacket) =>
			{
				//	ignore non iframes
				var nt = GetNaluType(RawPacket[0]);
				if (nt != NaluType.IFrame && nt != NaluType.PFrame)
				{
					return;
				}
				Debug.Log("Found " + nt);
				
				var AnnexPacketData = new List<byte>();
				//	https://github.com/SoylentGraham/SoyLib/blob/master/src/SoyH264.cpp#L328
				AnnexPacketData.Add(0);
				AnnexPacketData.Add(0);
				AnnexPacketData.Add(0);
				AnnexPacketData.Add(1);
				//AnnexPacketData.Add(GetNaluTypeByte(NaluType.AccessUnitDelimiter));
				//AnnexPacketData.Add(0xf0);
				AnnexPacketData.AddRange(RawPacket);
				AnnexPacketDatas.Add(AnnexPacketData.ToArray());
			};
			SplitPacket_Avcc(Header, Packet, EnumPacket);

			return AnnexPacketDatas;
		}

		static NaluType GetNaluType(byte Byte)
		{
			var nt = Byte & 0x1f;
			return (NaluType)nt;
		}

		public static byte GetNaluTypeByte(NaluType Type)
		{
			var NaluTypeByte = 0;
			var nal_ref_idc = 3;
			NaluTypeByte |= nal_ref_idc << 5;
			NaluTypeByte |= (int)Type;
			return (byte)NaluTypeByte;
		}

		public static byte[] RawToAnnexb4(byte[] Packet,NaluType Type)
		{
			var AnnexPacket = new byte[Packet.Length + 5];
			Array.Copy(Packet, 0, AnnexPacket, 5, Packet.Length);
			AnnexPacket[0] = 0;
			AnnexPacket[1] = 0;
			AnnexPacket[2] = 0;
			AnnexPacket[3] = 1;
			AnnexPacket[4] = GetNaluTypeByte(Type);
			return AnnexPacket;
		}
	}
}
