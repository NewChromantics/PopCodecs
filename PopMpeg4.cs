using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/*	
	I've called this mpeg4, but it's all based on the "original" quicktime atom/basemediafile format
	https://developer.apple.com/library/content/documentation/QuickTime/QTFF/QTFFChap2/qtff2.html

	the generic atom stuff is in PopX.Atom
*/
namespace PopX
{
	public static class Mpeg4
	{
		public static int Get16(byte a, byte b) { return PopX.Atom.Get16(a, b); }
		public static int Get24(byte a, byte b, byte c) { return PopX.Atom.Get24(a, b, c); }
		public static int Get32(byte a, byte b, byte c, byte d) { return PopX.Atom.Get32(a, b, c,d); }
		public static int Get64(byte a, byte b, byte c, byte d, byte e, byte f, byte g, byte h) { return PopX.Atom.Get64(a, b, c,d,e,f,g,h); }

		public static int Get16(byte[] Data, ref int StartIndex) { var v = Get16(Data[StartIndex + 0], Data[StartIndex + 1]);	StartIndex += 2;	return v; }
		public static int Get24(byte[] Data, ref int StartIndex) { var v = Get24(Data[StartIndex + 0], Data[StartIndex + 1], Data[StartIndex + 2]);	StartIndex += 3;	return v; }
		public static int Get32(byte[] Data, ref int StartIndex) { var v = Get32(Data[StartIndex + 0], Data[StartIndex + 1], Data[StartIndex + 2], Data[StartIndex + 3]); StartIndex += 4; return v; }

		public static byte[] Get8x4(byte[] Data, ref int StartIndex)
		{
			var abcd = new byte[4] { Data[StartIndex + 0], Data[StartIndex + 1], Data[StartIndex + 2], Data[StartIndex + 3] };
			StartIndex += 4;
			return abcd;
		}



		//	known mpeg4 atoms
		/*
			types[0] = "ftyp,moov,mdat";
			types[1] = "mvhd,trak,udta";
			types[2] = "tkhd,edts,mdia,meta,covr,©nam";
			types[3] = "mdhd,hdlr,minf";
			types[4] = "smhd,vmhd,dinf,stbl";
			types[5] = "stsd,stts,stss,ctts,stsc,stsz,stco";
		*/
		public struct TSample
		{
			public long DataPosition;
			public long DataSize;
			public bool IsKeyframe;	
			public int DecodeTimeMs;
			public int PresentationTimeMs;
			public int DurationMs;
		};
		//	class to make it easier to pass around data
		public class TTrack
		{
			public List<TSample> Samples;
			public List<TTrackSampleDescription> SampleDescriptions;

			public TTrack()
			{
				this.Samples = new List<TSample>();
			}
		};

		struct ChunkMeta
		{
			//	each is 32 bit (4 bytes)
			public int FirstChunk;
			public int SamplesPerChunk;
			public int SampleDescriptionId;

			public ChunkMeta(byte[] Data, int Offset)
			{
				FirstChunk = Atom.Get32(Data[Offset + 0], Data[Offset + 1], Data[Offset + 2], Data[Offset + 3]);
				SamplesPerChunk = Atom.Get32(Data[Offset + 4], Data[Offset + 5], Data[Offset + 6], Data[Offset + 7]);
				SampleDescriptionId = Atom.Get32(Data[Offset + 8], Data[Offset + 9], Data[Offset + 10], Data[Offset + 11]);
			}
		};

		public struct TMovieHeader
		{
			public float		TimeScale;	//	to convert from internal units to seconds
			public Matrix4x4	VideoTransform;
			public TimeSpan		Duration;
			public DateTime		CreationTime;
			public DateTime		ModificationTime;
			public float		PreviewDuration;
		};


		public struct TMediaHeader
		{
			public float TimeScale; //	to convert from internal units to seconds
			public Matrix4x4 VideoTransform;
			public TimeSpan Duration;
			public DateTime CreationTime;
			public DateTime ModificationTime;
			public int LanguageId;	//	todo: convert to proper c# language id
			public float Quality;	//	originally 16 bit
		};


		static public System.DateTime GetDateTimeFromSecondsSinceMidnightJan1st1904(int Seconds)
		{
			//	todo: check this
			var Epoch = new DateTime(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc);
			Epoch.AddSeconds(Seconds);
			return Epoch;
		}

		static List<ChunkMeta> GetChunkMetas(TAtom Atom, byte[] FileData)
		{
			var Metas = new List<ChunkMeta>();
			var AtomData = new byte[Atom.DataSize];
			Array.Copy(FileData, Atom.FileOffset, AtomData, 0, AtomData.Length);

			//	https://developer.apple.com/library/content/documentation/QuickTime/QTFF/QTFFChap2/qtff2.html
			var Version = AtomData[8];
			var Flags = PopX.Atom.Get24(AtomData[9], AtomData[10], AtomData[11]);
			var EntryCount = PopX.Atom.Get32(AtomData[12], AtomData[13], AtomData[14], AtomData[15]);

			var MetaSize = 3 * 4;
			for (int i = 16; i < AtomData.Length; i += MetaSize)
			{
				var Meta = new ChunkMeta(AtomData, i);
				Metas.Add(Meta);
			}
			if (Metas.Count() != EntryCount)
				Debug.LogWarning("Expected " + EntryCount + " chunk metas, got " + Metas.Count());

			return Metas;
		}

		static List<long> GetChunkOffsets(TAtom? Offset32sAtom, TAtom? Offset64sAtom, byte[] FileData)
		{
			//	chunk offsets are file-relative, not realtive to mdat or anything else. see
			//	https://developer.apple.com/library/content/documentation/QuickTime/QTFF/QTFFChap2/qtff2.html
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

		static bool[] GetSampleKeyframes(TAtom? SyncSamplesAtom, byte[] FileData,int SampleCount)
		{
			var Keyframes = new bool[SampleCount];
			var Default = (SyncSamplesAtom == null) ? true : false;

			for (var i = 0; i < Keyframes.Length;	i++ ) 
				Keyframes[i] = Default;

			if (SyncSamplesAtom == null)
				return Keyframes;

			//	read table and set keyframed
			var Atom = SyncSamplesAtom.Value;
			var AtomData = new byte[Atom.DataSize];
			Array.Copy(FileData, Atom.FileOffset, AtomData, 0, AtomData.Length);

			var Version = AtomData[8];
			var Flags = Get24(AtomData[9], AtomData[10], AtomData[11]);
			var EntryCount = Get32(AtomData[12], AtomData[13], AtomData[14], AtomData[15]);

			//	each entry in the table is the size of a sample (and one chunk can have many samples)
			var StartOffset = 16;
			//	gr: docs don't say size, but this seems accurate...
			var IndexSize = (AtomData.Length - StartOffset) / EntryCount;
			for (int i = StartOffset; i < AtomData.Length; i += IndexSize)
			{
				int SampleIndex;
				if (IndexSize == 3)
				{
					SampleIndex = Get24(AtomData[i + 0], AtomData[i + 1], AtomData[i + 2]);
				}
				else if (IndexSize == 4)
				{
					SampleIndex = Get32(AtomData[i + 0], AtomData[i + 1], AtomData[i + 2], AtomData[i + 3]);
				}
				else
				{
					throw new System.Exception("Unhandled index size " + IndexSize);
				}
				//	gr: indexes start at 1 again...
				SampleIndex--;
				Keyframes[SampleIndex] = true;
			}

			return Keyframes;
		}


		static List<int> GetSampleDurations(TAtom Atom, byte[] FileData,int? ExpectedSampleCount)
		{
			var Durations = new List<int>();

			var AtomData = new byte[Atom.DataSize];
			Array.Copy(FileData, Atom.FileOffset, AtomData, 0, AtomData.Length);

			var Version = AtomData[8];
			var Flags = Get24(AtomData[9], AtomData[10], AtomData[11]);
			var EntryCount = Get32(AtomData[12], AtomData[13], AtomData[14], AtomData[15]);
			var StartOffset = 16;

			//	read durations as we go
			for (int i = StartOffset; i < AtomData.Length; i += 4 + 4)
			{
				var SampleCount = Get32(AtomData[i + 0], AtomData[i + 1], AtomData[i + 2], AtomData[i + 3]);
				var SampleDuration = Get32(AtomData[i + 4], AtomData[i + 5], AtomData[i + 6], AtomData[i + 7]);

				for (int s = 0; s < SampleCount; s++)
					Durations.Add(SampleDuration);
			}

			if (ExpectedSampleCount != null)
				if (ExpectedSampleCount.Value != Durations.Count)
					throw new System.Exception("Expected " + ExpectedSampleCount.Value + " got " + Durations.Count);
				
			return Durations;
		}

		static List<int> GetSampleDurations(TAtom? Atom, byte[] FileData,int Default,int? ExpectedSampleCount)
		{
			if ( Atom == null )
			{
				//	we need to know how many samples there are, if we're going to make a list of defaults
				if (ExpectedSampleCount == null)
					throw new System.Exception("No Duration atom, and no sample count, so cannot generate table of defaults");

				var Offsets = new List<int>();
				for (var i = 0; i < ExpectedSampleCount.Value; i++)
					Offsets.Add(Default);
				return Offsets;
			}

			return GetSampleDurations(Atom.Value, FileData, ExpectedSampleCount);
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


		static TMediaHeader DecodeAtom_MediaHeader(TAtom Atom, byte[] FileData)
		{
			var AtomData = FileData.SubArray(Atom.FileOffset, Atom.DataSize);

			var Version = AtomData[8];
			var Offset = 9;
			var Flags = Get24(AtomData, ref Offset);
			var CreationTime = Get32(AtomData, ref Offset);
			var ModificationTime = Get32(AtomData, ref Offset);
			var TimeScale = Get32(AtomData, ref Offset);
			var Duration = Get32(AtomData, ref Offset);
			var Language = Get16(AtomData, ref Offset);
			var Quality = Get16(AtomData, ref Offset);

			var Header = new TMediaHeader();
			Header.TimeScale = 1.0f / (float)TimeScale; //	timescale is time units per second
			Header.Duration = new TimeSpan(0,0, (int)(Duration * Header.TimeScale));
			Header.CreationTime = GetDateTimeFromSecondsSinceMidnightJan1st1904(CreationTime);
			Header.ModificationTime = GetDateTimeFromSecondsSinceMidnightJan1st1904(ModificationTime);
			Header.CreationTime = GetDateTimeFromSecondsSinceMidnightJan1st1904(CreationTime);
			Header.LanguageId = Language;
			Header.Quality = Quality / (float)(1 << 16);
			return Header;
		}

		static TMovieHeader DecodeAtom_MovieHeader(TAtom Atom,byte[] FileData)
		{
			var Sizes = new List<long>();
			var AtomData = FileData.SubArray(Atom.FileOffset, Atom.DataSize);

			//	https://developer.apple.com/library/content/documentation/QuickTime/QTFF/art/qt_l_095.gif
			var Version = AtomData[8];
			var Offset = 9;
			var Flags = Get24(AtomData,ref Offset);
			var CreationTime = Get32(AtomData,ref Offset);
			var ModificationTime = Get32(AtomData,ref Offset);
			var TimeScale = Get32(AtomData,ref Offset);
			var Duration = Get32(AtomData,ref Offset);
			var PreferredRate = Get32(AtomData,ref Offset);
			var PreferredVolume = Get16(AtomData,ref Offset);
			var Reserved = AtomData.SubArray(Offset, 10);	Offset += 10;
			var Matrix = AtomData.SubArray(Offset, 36); Offset += 36;
			var PreviewTime = Get32(AtomData,ref Offset);
			var PreviewDuration = Get32(AtomData,ref Offset);
			var PosterTime = Get32(AtomData,ref Offset);
			var SelectionTime = Get32(AtomData,ref Offset);
			var SelectionDuration = Get32(AtomData,ref Offset);
			var CurrentTime = Get32(AtomData,ref Offset);
			var NextTrackId = Get32(AtomData,ref Offset);

			foreach (var Zero in Reserved)
				if (Zero != 0)
					Debug.LogWarning("Reserved value " + Zero + " is not zero");

			//	actually a 3x3 matrix, but we make it 4x4 for unity
			//	gr: do we need to transpose this? docs don't say row or column major :/
			//	wierd element labels, right? spec uses them.
			var a = Matrix[0];
			var b = Matrix[1];
			var u = Matrix[2];
			var c = Matrix[3];
			var d = Matrix[4];
			var v = Matrix[5];
			var x = Matrix[6];
			var y = Matrix[7];
			var w = Matrix[8];
			var MtxRow0 = new Vector4(a, b, u, 0);
			var MtxRow1 = new Vector4(c, d, v, 0);
			var MtxRow2 = new Vector4(x, y, w, 0);
			var MtxRow3 = new Vector4(0, 0, 0, 1);

			var Header = new TMovieHeader();
			Header.TimeScale = 1.0f / (float)TimeScale; //	timescale is time units per second
			Header.VideoTransform = new Matrix4x4(MtxRow0, MtxRow1, MtxRow2, MtxRow3);
			Header.Duration = new TimeSpan(0,0,(int)(Duration * Header.TimeScale));
			Header.CreationTime = GetDateTimeFromSecondsSinceMidnightJan1st1904(CreationTime);
			Header.ModificationTime = GetDateTimeFromSecondsSinceMidnightJan1st1904(ModificationTime);
			Header.CreationTime = GetDateTimeFromSecondsSinceMidnightJan1st1904(CreationTime);
			Header.PreviewDuration = PreviewDuration * Header.TimeScale;
			return Header;
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

		public static void Parse(string Filename, System.Action<TTrack> EnumTrack)
		{
			var FileData = File.ReadAllBytes(Filename);
			Parse(FileData, EnumTrack);
		}

		public static void Parse(byte[] FileData, System.Action<TTrack> EnumTrack)
		{
			var Length = FileData.Length;

			//	decode the header atoms
			TAtom? ftypAtom = null;
			TAtom? moovAtom = null;
			TAtom? mdatAtom = null;

			System.Action<TAtom> EnumRootAtoms = (Atom) =>
			{
				if (Atom.Fourcc == "ftyp") ftypAtom = Atom;
				else if (Atom.Fourcc == "moov") moovAtom = Atom;
				else if (Atom.Fourcc == "mdat") mdatAtom = Atom;
				else
					Debug.Log("Ignored atom: " + Atom.Fourcc);
			};

			//	read the root atoms
			PopX.Atom.Parse(FileData, EnumRootAtoms);

			//	dont even need mdat!
			var Errors = new List<string>();
			if (ftypAtom == null)
				Errors.Add("Missing ftyp atom");
			if (moovAtom == null)
				Errors.Add("Missing moov atom");
			if (Errors.Count > 0)
				throw new System.Exception(String.Join(", ",Errors.ToArray()));

			//	decode moov (tracks, sample data etc)
			List<TTrack> Tracks;
			TMovieHeader? Header;
			DecodeAtom_Moov(out Tracks, out Header, moovAtom.Value, FileData);
			foreach (var t in Tracks)
				EnumTrack(t);
		}


		static void DecodeAtom_Moov(out List<TTrack> Tracks,out TMovieHeader? MovieHeader,TAtom Moov, byte[] FileData)
		{
			var NewTracks = new List<TTrack>();

			//	get header first
			var MovieHeaderAtom = Atom.GetChildAtom(Moov, "mvhd", FileData);
			if (MovieHeaderAtom != null)
				MovieHeader = DecodeAtom_MovieHeader(MovieHeaderAtom.Value, FileData);
			else
				MovieHeader = null;

			//	gotta be local to be used in lambda
			var TimeScale = MovieHeader.HasValue ? MovieHeader.Value.TimeScale : 1;
			System.Action<TAtom> EnumMoovChildAtom = (Atom) =>
			{
				if (Atom.Fourcc == "trak")
				{
					var Track = new TTrack();
					DecodeAtom_Track( ref Track, Atom, null, TimeScale, FileData );
					NewTracks.Add(Track);
				}
			};
			Atom.DecodeAtomChildren(EnumMoovChildAtom, Moov, FileData);
			Tracks = NewTracks;
		}


		static List<TSample> DecodeAtom_SampleTable(TAtom StblAtom, TAtom? MdatAtom,float TimeScale,byte[] FileData)
		{
			TAtom? ChunkOffsets32Atom = null;
			TAtom? ChunkOffsets64Atom = null;
			TAtom? SampleSizesAtom = null;
			TAtom? SampleToChunkAtom = null;
			TAtom? SyncSamplesAtom = null;
			TAtom? SampleDecodeDurationsAtom = null;
			TAtom? SamplePresentationTimeOffsetsAtom = null;

			System.Action<TAtom> EnumStblAtom = (Atom) =>
			{
				//	http://mirror.informatimago.com/next/developer.apple.com/documentation/QuickTime/REF/Streaming.35.htm
				if (Atom.Fourcc == "stco")
					ChunkOffsets32Atom = Atom;
				if (Atom.Fourcc == "co64")
					ChunkOffsets64Atom = Atom;
				if (Atom.Fourcc == "stsz")
					SampleSizesAtom = Atom;
				if (Atom.Fourcc == "stsc")
					SampleToChunkAtom = Atom;
				if (Atom.Fourcc == "stss")
					SyncSamplesAtom = Atom;
				if (Atom.Fourcc == "stts")
					SampleDecodeDurationsAtom = Atom;
				if (Atom.Fourcc=="ctts")
					SamplePresentationTimeOffsetsAtom = Atom;
			};
		
			PopX.Atom.DecodeAtomChildren(EnumStblAtom, StblAtom, FileData);

			//	work out samples from atoms!
			if (SampleSizesAtom == null)
				throw new System.Exception("Track missing sample sizes atom");
			if (ChunkOffsets32Atom == null && ChunkOffsets64Atom == null)
				throw new System.Exception("Track missing chunk offset atom");
			if (SampleToChunkAtom == null)
				throw new System.Exception("Track missing sample-to-chunk table atom");
			if (SampleDecodeDurationsAtom == null)
				throw new System.Exception("Track missing time-to-sample table atom");

			var PackedChunkMetas = GetChunkMetas(SampleToChunkAtom.Value, FileData);
			var ChunkOffsets = GetChunkOffsets(ChunkOffsets32Atom, ChunkOffsets64Atom, FileData);
			var SampleSizes = GetSampleSizes(SampleSizesAtom.Value, FileData);
			var SampleKeyframes = GetSampleKeyframes(SyncSamplesAtom, FileData, SampleSizes.Count);
			var SampleDurations = GetSampleDurations(SampleDecodeDurationsAtom.Value, FileData, SampleSizes.Count);
			var SamplePresentationTimeOffsets = GetSampleDurations(SamplePresentationTimeOffsetsAtom, FileData, 0, SampleSizes.Count);

			//	durations start at zero (proper time must come from somewhere else!) and just count up over durations
			var SampleDecodeTimes = new int[SampleSizes.Count];
			for (int i = 0; i < SampleDecodeTimes.Length;	i++)
			{
				var LastDuration = (i == 0) ? 0 : SampleDurations[i - 1];
				var LastTime = (i == 0) ? 0 : SampleDecodeTimes[i - 1];
				SampleDecodeTimes[i] = LastTime + LastDuration;
			}

			//	pad the metas to fit offset information
			//	https://sites.google.com/site/james2013notes/home/mp4-file-format
			var ChunkMetas = new List<ChunkMeta>();
			//foreach ( var ChunkMeta in PackedChunkMetas )
			for (var i = 0; i < PackedChunkMetas.Count; i++)
			{
				var ChunkMeta = PackedChunkMetas[i];
				//	first begins at 1. despite being an index...
				var FirstChunk = ChunkMeta.FirstChunk - 1;
				//	pad previous up to here
				while (ChunkMetas.Count < FirstChunk)
					ChunkMetas.Add(ChunkMetas[ChunkMetas.Count - 1]);

				ChunkMetas.Add(ChunkMeta);
			}
			//	and pad the end
			while (ChunkMetas.Count < ChunkOffsets.Count)
				ChunkMetas.Add(ChunkMetas[ChunkMetas.Count - 1]);

			//	superfolous data
			var Chunks = new List<TSample>();
			long? MdatEnd = (MdatAtom.HasValue) ? (MdatAtom.Value.FileOffset + MdatAtom.Value.DataSize) : (long?)null;
			for (int i = 0; i < ChunkOffsets.Count; i++)
			{
				var ThisChunkOffset = ChunkOffsets[i];
				//	chunks are serial, so length is up to next
				//	gr: mdatend might need to be +1
				long? NextChunkOffset = (i >= ChunkOffsets.Count - 1) ? MdatEnd : ChunkOffsets[i + 1];
				long ChunkLength = (NextChunkOffset.HasValue) ? (NextChunkOffset.Value - ThisChunkOffset) : 0;

				var Chunk = new TSample();
				Chunk.DataPosition = ThisChunkOffset;
				Chunk.DataSize = ChunkLength;
				Chunks.Add(Chunk);
			}

			var Samples = new List<TSample>();

			System.Func<int,int> TimeToMs = (TimeUnit) =>
			{
				//	to float
				var Timef = TimeUnit * TimeScale;
				var TimeMs = Timef * 1000.0f;
				return (int)TimeMs;
			};

			int SampleIndex = 0;
			for (int i = 0; i < ChunkMetas.Count(); i++)
			{
				var SampleMeta = ChunkMetas[i];
				var ChunkIndex = i;
				var ChunkOffset = ChunkOffsets[ChunkIndex];

				for (int s = 0; s < SampleMeta.SamplesPerChunk; s++)
				{
					var Sample = new TSample();
					Sample.DataPosition = ChunkOffset;
					Sample.DataSize = SampleSizes[SampleIndex];
					Sample.IsKeyframe = SampleKeyframes[SampleIndex];
					Sample.DecodeTimeMs = TimeToMs( SampleDecodeTimes[SampleIndex] );
					Sample.DurationMs = TimeToMs( SampleDurations[SampleIndex] );
					Sample.PresentationTimeMs = TimeToMs( SampleDecodeTimes[SampleIndex] + SamplePresentationTimeOffsets[SampleIndex] );
					Samples.Add(Sample);

					ChunkOffset += Sample.DataSize;
					SampleIndex++;
				}
			}

			if (SampleIndex != SampleSizes.Count)
				Debug.LogWarning("Enumerated " + SampleIndex + " samples, expected " + SampleSizes.Count);

			return Samples;
		}

		public struct TTrackSampleDescription
		{
			public string Fourcc;   //	gr: this is 4 bytes, but might not actually be a fourcc?
			public int DataReferenceIndex;
			public byte[] Data;		//	codec specific data
		};

		static List<TTrackSampleDescription> GetTrackSampleDescriptions(TAtom Atom, byte[] FileData)
		{
			var Sizes = new List<long>();
			var AtomData = FileData.SubArray(Atom.FileOffset, Atom.DataSize);

			//	https://developer.apple.com/library/content/documentation/QuickTime/QTFF/art/qt_l_095.gif
			var Version = AtomData[8];
			var Offset = 9;
			var Flags = Get24(AtomData, ref Offset);
			var EntryCount = Get32(AtomData, ref Offset);

			var SampleDescriptions = new List<TTrackSampleDescription>();

			for (int sd = 0; sd < EntryCount;	sd++ )
			{
				var OffsetStart = Offset;
				var Size = Get32(AtomData, ref Offset);
				var Format = Get8x4(AtomData, ref Offset);
				var Reserved = AtomData.SubArray(Offset, 6);
				Offset += 6;
				var DataReferenceIndex = Get16(AtomData, ref Offset);

				//	read the remaining data
				var HeaderSize = (Offset - OffsetStart);
				var DataSize = Size - HeaderSize;
				var Data = AtomData.SubArray(Offset, DataSize);
				Offset += DataSize;

				var SampleDescription = new TTrackSampleDescription();
				SampleDescription.DataReferenceIndex = DataReferenceIndex;
				SampleDescription.Data = Data;
				SampleDescription.Fourcc = Encoding.ASCII.GetString(Format);
				SampleDescriptions.Add(SampleDescription);
			}
			return SampleDescriptions;
		}

		static void DecodeAtom_Track(ref TTrack Track,TAtom Trak,TAtom? MdatAtom,float MovieTimeScale,byte[] FileData)
		{
			List<TSample> TrackSamples = null;
			List<TTrackSampleDescription> TrackSampleDescriptions = null;
			TMediaHeader? MediaHeader = null;

			System.Action<TAtom> EnumMinfAtom = (Atom) =>
			{
				//EnumAtom(Atom);
				if (Atom.Fourcc == "stbl")
				{
					var TrackTimeScale = MediaHeader.HasValue ? MediaHeader.Value.TimeScale : MovieTimeScale;
					TrackSamples = DecodeAtom_SampleTable(Atom, MdatAtom, TrackTimeScale, FileData);

					var SampleDescriptionAtom = PopX.Atom.GetChildAtom(Atom, "stsd", FileData);
					if (SampleDescriptionAtom != null)
						TrackSampleDescriptions = GetTrackSampleDescriptions(SampleDescriptionAtom.Value,FileData);
				}
			};
			System.Action<TAtom> EnumMdiaAtom = (Atom) =>
			{
				if (Atom.Fourcc == "mdhd")
					MediaHeader = DecodeAtom_MediaHeader(Atom, FileData);

				if (Atom.Fourcc == "minf")
					PopX.Atom.DecodeAtomChildren(EnumMinfAtom, Atom, FileData);
			};
			System.Action<TAtom> EnumTrakChild = (Atom) =>
			{
				//EnumAtom(Atom);
				if (Atom.Fourcc == "mdia")
					PopX.Atom.DecodeAtomChildren(EnumMdiaAtom, Atom, FileData);
			};

			//	go through the track
			PopX.Atom.DecodeAtomChildren(EnumTrakChild, Trak, FileData);

			Track.SampleDescriptions = TrackSampleDescriptions;
			Track.Samples = TrackSamples;
		}

	}
}
