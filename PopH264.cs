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
		public enum NaluType
		{
			Avcc1,  //	size is 1 byte
			Avcc2,  //	size is 2 bytes
			Avcc4,  //	size is 4 bytes
			Annexb3,    //	0x0 0x0 0x1
			Annexb4,    //	0x0 0x0 0x0 0x1
		};

		public struct AvccHeader
		{
			public int AvccProfile;
			public int NaluLengthMinusOne;
			public List<byte[]> SPSs;
			public List<byte[]> PPSs;
		};

		public static NaluType GetNaluType(byte[] Data)
		{
			if (Data[0] == 0 && Data[1] == 0 && Data[2] == 1)
				return NaluType.Annexb3;
			if (Data[0] == 0 && Data[1] == 0 && Data[2] == 0 && Data[3] == 1)
				return NaluType.Annexb4;

			var Size1 = Data[0];
			var Size2 = (Data[0]<<0) | (Data[1] << 8);
			var Size4 = (Data[0]<< 0) | (Data[1] << 8) | (Data[2] << 16) | (Data[3] << 24);

			//	very rough guess
			if (Size4 <= Data.Length)
				return NaluType.Avcc4;
			if (Size2 <= Data.Length)
				return NaluType.Avcc2;
			if (Size1 <= Data.Length)
				return NaluType.Avcc1;

			throw new System.Exception("Cannot determine h264 nalu type");
		}

		//	split avcc packets
		public static AvccHeader ParseAvccHeader(byte[] Data)
		{
			//	https://stackoverflow.com/a/24890903/355753
			var Version = Data[0];
			var Profile = Data[1];
			var Compatibility = Data[2];
			var ReservedAndNaluLengthMinusOne = Data[3];
			var ReservedAndSpsCount = Data[4];
			var NaluLengthMinusOne = ReservedAndNaluLengthMinusOne & ((1 << 2) - 1);
			var SpsCount = ReservedAndNaluLengthMinusOne & ((1 << 5) - 1);

			var Offset = 5;
			for (int i = 0; i < SpsCount;	i++ )
			{
				var SpsSize = (Data[Offset + 0]<<0) | (Data[Offset + 1]<<8);
				Offset += 2;
				var Sps = Data.SubArray(Offset, SpsSize);
				Offset += SpsSize;
			}

		}


	}
}
