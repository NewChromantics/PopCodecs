using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/*
	todo: rename this. 
	It's not limited to mp4. It's a collection of atoms, which are only known as atoms in some specs(mp4 mostly).
	Everything else calls them "boxes"
	This should be something more like PopIsoBaseMediaFormat
	PopAtom has a better ring to it (we don't really want to call it PopBoxFormat)
	Maybe PopMpeg is better, or even PopQuicktime, but that could be misleading (for jpeg2000 support etc)
	https://en.wikipedia.org/wiki/ISO_base_media_file_format


	For now it's a generic "atom" (fourcc+size) parser
*/

//	nice reference; http://fabiensanglard.net/mobile_progressive_playback/index.php
namespace PopX
{
	//	use of long = file position
	public struct TAtom
	{
		public const long HeaderSize = 8;
		public string Fourcc;
		public long FileOffset;
		public long DataSize;

		public void Set(byte[] Data8)
		{
			//Size = BitConverter.ToUInt32(new byte[] { Data8[0], Data8[1], Data8[2], Data8[3] }.Reverse().ToArray(), 0);
			int sz = Data8[3] << 0;
			sz += Data8[2] << 8;
			sz += Data8[1] << 16;
			sz += Data8[0] << 24;
			DataSize = (uint)sz;

			Fourcc = Encoding.ASCII.GetString(new byte[] { Data8[4], Data8[5], Data8[6], Data8[7] });
		}
	}


	public static class Atom
	{
		public static int Get24(byte a, byte b, byte c)
		{
			int sz = c << 0;
			sz += b << 8;
			sz += a << 16;
			return sz;
		}

		public static int Get32(byte a, byte b, byte c, byte d)
		{
			int sz = d << 0;
			sz += c << 8;
			sz += b << 16;
			sz += a << 24;
			return sz;
		}

		public static int Get64(byte a, byte b, byte c, byte d, byte e, byte f, byte g, byte h)
		{
			int sz = h << 0;
			sz += g << 8;
			sz += f << 16;
			sz += e << 24;
			sz += d << 32;
			sz += c << 40;
			sz += b << 48;
			sz += a << 56;
			return sz;
		}



		static uint GetFourcc(string FourccString)
		{
			var Bytes = Encoding.ASCII.GetBytes(FourccString);
			return GetFourcc(Bytes[0], Bytes[1], Bytes[2], Bytes[3]);
		}

		static uint GetFourcc(byte a, byte b, byte c, byte d)
		{
			return (uint)Get32(a, b, c, d);
		}




		static void DecodeAtomRecursive(System.Action<TAtom> EnumAtom, TAtom Moov, byte[] FileData)
		{
			var AtomStart = Moov.FileOffset + TAtom.HeaderSize;
			while (true)
			{
				var NextAtom = GetNextAtom(FileData, (int)AtomStart);
				if (NextAtom == null)
					break;

				var Atom = NextAtom.Value;

				//	moov atom: The metadatas, containing codec description used in the mdata atom.
				//	It also contains sub-atoms "stco" and "co64" which are absolute pointers to keyframes in the mdata atom.
				EnumAtom(Atom);

				DecodeAtomRecursive(EnumAtom, Atom, FileData);

				AtomStart = Atom.FileOffset + Atom.DataSize;
			}
		}

		static public void DecodeAtomChildren(System.Action<TAtom> EnumAtom, TAtom Moov, byte[] FileData)
		{
			//	decode moov children (mvhd, trak, udta)
			var MoovEnd = Moov.FileOffset + Moov.DataSize;
			for (var AtomStart = Moov.FileOffset + TAtom.HeaderSize; AtomStart < MoovEnd; AtomStart += 0)
			{
				var NextAtom = GetNextAtom(FileData, AtomStart);
				if (NextAtom == null)
					break;
				var Atom = NextAtom.Value;
				Debug.Log("Found " + Atom.Fourcc);
				try
				{
					EnumAtom(Atom);
				}
				catch (System.Exception e)
				{
					Debug.LogException(e);
				}
				AtomStart = Atom.FileOffset + Atom.DataSize;
			}
		}

		struct SampleMeta
		{
			//	each is 32 bit (4 bytes)
			public int FirstChunk;
			public int SamplesPerChunk;
			public int SampleDescriptionId;

			public SampleMeta(byte[] Data, int Offset)
			{
				FirstChunk = Get32(Data[Offset + 0], Data[Offset + 1], Data[Offset + 2], Data[Offset + 3]);
				SamplesPerChunk = Get32(Data[Offset + 4], Data[Offset + 5], Data[Offset + 6], Data[Offset + 7]);
				SampleDescriptionId = Get32(Data[Offset + 8], Data[Offset + 9], Data[Offset + 10], Data[Offset + 11]);
			}
		};

		static List<SampleMeta> GetSampleMetas(TAtom Atom, byte[] FileData)
		{
			var Metas = new List<SampleMeta>();
			var AtomData = new byte[Atom.DataSize];
			Array.Copy(FileData, Atom.FileOffset, AtomData, 0, AtomData.Length);

			//	https://developer.apple.com/library/content/documentation/QuickTime/QTFF/QTFFChap2/qtff2.html
			var Version = AtomData[8];
			var Flags = Get24(AtomData[9], AtomData[10], AtomData[11]);
			var EntryCount = Get32(AtomData[12], AtomData[13], AtomData[14], AtomData[15]);

			var MetaSize = 3 * 4;
			for (int i = 16; i < AtomData.Length; i += MetaSize)
			{
				var Meta = new SampleMeta(AtomData, i);
				Metas.Add(Meta);
			}
			if (Metas.Count() != EntryCount)
				Debug.LogWarning("Expected " + EntryCount + " sample metas, got " + Metas.Count());

			return Metas;
		}

		static List<long> GetChunkOffsets(TAtom? Offset32sAtom, TAtom? Offset64sAtom, byte[] FileData)
		{
			int OffsetSize;
			TAtom Atom;
			if (Offset32sAtom.HasValue)
			{
				OffsetSize = 32 / 8;
				Atom = Offset32sAtom.Value;
			}
			else if (Offset64sAtom.HasValue)
			{
				OffsetSize = 64 / 8;
				Atom = Offset64sAtom.Value;
			}
			else
			{
				throw new System.Exception("Missing offset atom");
			}

			var Offsets = new List<long>();
			var AtomData = new byte[Atom.DataSize];
			Array.Copy(FileData, Atom.FileOffset, AtomData, 0, AtomData.Length);

			var Version = AtomData[8];
			var Flags = Get24(AtomData[9], AtomData[10], AtomData[11]);
			var EntryCount = Get32(AtomData[12], AtomData[13], AtomData[14], AtomData[15]);
			if (OffsetSize <= 0)
				throw new System.Exception("Invalid offset size: " + OffsetSize);
			for (int i = 16; i < AtomData.Length; i += OffsetSize)
			{
				if (OffsetSize * 8 == 32)
				{
					var Offset = Get32(AtomData[i + 0], AtomData[i + 1], AtomData[i + 2], AtomData[i + 3]);
					Offsets.Add(Offset);
				}
				else if (OffsetSize * 8 == 64)
				{
					var Offset = Get64(AtomData[i + 0], AtomData[i + 1], AtomData[i + 2], AtomData[i + 3], AtomData[i + 4], AtomData[i + 5], AtomData[i + 6], AtomData[i + 7]);
					Offsets.Add(Offset);
				}
			}
			if (Offsets.Count() != EntryCount)
				Debug.LogWarning("Expected " + EntryCount + " chunks, got " + Offsets.Count());

			return Offsets;
		}

		static List<long> GetSampleSizes(TAtom Atom, byte[] FileData)
		{
			var Sizes = new List<long>();
			var AtomData = new byte[Atom.DataSize];
			Array.Copy(FileData, Atom.FileOffset, AtomData, 0, AtomData.Length);

			//	https://developer.apple.com/library/content/documentation/QuickTime/QTFF/QTFFChap2/qtff2.html
			var Version = AtomData[8];
			var Flags = Get24(AtomData[9], AtomData[10], AtomData[11]);
			var SampleSize = Get32(AtomData[12], AtomData[13], AtomData[14], AtomData[15]);
			var EntryCount = Get32(AtomData[16], AtomData[17], AtomData[18], AtomData[19]);

			//	if size specified, they're all this size
			if (SampleSize != 0)
			{
				for (int i = 0; i < EntryCount; i++)
					Sizes.Add(SampleSize);
				return Sizes;
			}

			//	each entry in the table is the size of a sample (and one chunk can have many samples)
			var SampleSizeStart = 20;
			//	gr: docs don't say size, but this seems accurate...
			SampleSize = (AtomData.Length - SampleSizeStart) / EntryCount;
			for (int i = SampleSizeStart; i < AtomData.Length; i += SampleSize)
			{
				if (SampleSize == 3)
				{
					var Size = Get24(AtomData[i + 0], AtomData[i + 1], AtomData[i + 2]);
					Sizes.Add(Size);
				}
				else if (SampleSize == 4)
				{
					var Size = Get32(AtomData[i + 0], AtomData[i + 1], AtomData[i + 2], AtomData[i + 3]);
					Sizes.Add(Size);
				}
			}
			if (Sizes.Count() != EntryCount)
				Debug.LogWarning("Expected " + EntryCount + " sample sizes, got " + Sizes.Count());

			return Sizes;
		}


		static TAtom? GetNextAtom(byte[] Data, long Start)
		{
			//	no more data!
			if (Start >= Data.Length)
				return null;
			
			var AtomData = new byte[TAtom.HeaderSize];
			Array.Copy( Data, Start, AtomData, 0, AtomData.Length);

			//	let it throw(TM)
			var Atom = new TAtom();
			Atom.Set(AtomData);

			//	todo: can we verify the fourcc? lets check the spec if the characters have to be ascii or something
			//	verify size
			var EndPos = Start + Atom.DataSize;
			if (EndPos > Data.Length)
				throw new System.Exception("Atom end position " + (EndPos) + " out of range of data " + Data.Length);

			Atom.FileOffset = (uint)Start;
			return Atom;
		}

		public static void Parse(string Filename, System.Action<TAtom> EnumAtom)
		{
			var FileData = File.ReadAllBytes(Filename);
			Parse(FileData, EnumAtom);
		}

		public static void Parse(byte[] FileData, System.Action<TAtom> EnumAtom)
		{
			var Length = FileData.Length;

			//	read first atom
			long i = 0;
			while (i < FileData.Length)
			{
				var NextAtom = GetNextAtom(FileData, i);
				if (NextAtom == null)
					break;

				var Atom = NextAtom.Value;
				try
				{
					EnumAtom(Atom);
				}
				catch (System.Exception e)
				{
					Debug.LogException(e);
				}

				if (Atom.DataSize == 1)
					throw new System.Exception("Extended Atom size found, not yet handled");
				//i = (int)(Atom.Offset + Atom.Length + 1);
				var NextPosition = Atom.FileOffset + Atom.DataSize;
				if (i == NextPosition)
					throw new System.Exception("Infinite loop averted");
				i = (int)NextPosition;
			}
		}


	}
}
