using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;


//	from PopUnityCommon
namespace PopX
{
	public static class Data
	{
		static public byte[] SubArray(this System.Array ParentArray, long Start, long Count)
		{
			var ChildArray = new byte[Count];
			System.Array.Copy(ParentArray, Start, ChildArray, 0, Count);
			return ChildArray;
		}

		static public T[] SubArray<T>(this List<T> ParentArray, long Start, long Count)
		{
			var ChildArray = new T[Count];
			ParentArray.CopyTo((int)Start, ChildArray, (int)0, (int)Count);
			return ChildArray;
		}
	}
}

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
		public static long Get64(byte a, byte b, byte c, byte d, byte e, byte f, byte g, byte h) { return PopX.Atom.Get64(a, b, c,d,e,f,g,h); }

		public static int Get8(byte[] Data, ref int StartIndex) { var v = Data[StartIndex];	StartIndex += 1; return v; }
		public static int Get16(byte[] Data, ref int StartIndex) { var v = Get16(Data[StartIndex + 0], Data[StartIndex + 1]); StartIndex += 2; return v; }
		public static int Get24(byte[] Data, ref int StartIndex) { var v = Get24(Data[StartIndex + 0], Data[StartIndex + 1], Data[StartIndex + 2]);	StartIndex += 3;	return v; }
		public static int Get32(byte[] Data, ref int StartIndex) { var v = Get32(Data[StartIndex + 0], Data[StartIndex + 1], Data[StartIndex + 2], Data[StartIndex + 3]); StartIndex += 4; return v; }
		public static long Get64(byte[] Data, ref int StartIndex) { var v = Get64(Data[StartIndex + 0], Data[StartIndex + 1], Data[StartIndex + 2], Data[StartIndex + 3], Data[StartIndex + 4], Data[StartIndex + 5], Data[StartIndex + 6], Data[StartIndex + 7]); StartIndex += 8; return v; }
		public static int Get32_BigEndian(byte[] Data, ref int StartIndex) { var v = Get32(Data[StartIndex + 3], Data[StartIndex + 2], Data[StartIndex + 1], Data[StartIndex + 0]); StartIndex += 4; return v; }

		public static byte[] Get8x4(byte[] Data, ref int StartIndex)
		{
			var abcd = new byte[4] { Data[StartIndex + 0], Data[StartIndex + 1], Data[StartIndex + 2], Data[StartIndex + 3] };
			StartIndex += 4;
			return abcd;
		}
		public static byte[] GetN(byte[] Data,int Length,ref int StartIndex)
		{
			var SubData = Data.SubArray( StartIndex, Length );
			StartIndex += Length;
			return SubData;
		}

		public static float Fixed1616ToFloat(int Fixed32)
		{
			var Int = Fixed32 >> 16;
			var FracMax = (1<<16)-1;
			var Frac = Fixed32 & FracMax;
			var Fracf = Frac / FracMax;
			return Int + Fracf;
		}

		public static float Fixed230ToFloat(int Fixed32)
		{
			var Int = Fixed32 >> 30;
			var FracMax = (1 << 30) - 1;
			var Frac = Fixed32 & FracMax;
			var Fracf = Frac / FracMax;
			return Int + Fracf;
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
			public int? MDatIdent;			//	do we know which mdat we're in
			public long? DataFilePosition;  //	chunk offsets are in file-position. Need to make it mdat-relative
			public long? DataPosition; 		//	mdat relative position
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


		static public System.DateTime GetDateTimeFromSecondsSinceMidnightJan1st1904(long Seconds)
		{
			//	todo: check this
			var Epoch = new DateTime(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc);
			Epoch.AddSeconds(Seconds);
			return Epoch;
		}

		static List<ChunkMeta> GetChunkMetas(TAtom Atom, System.Func<long, byte[]> ReadData)
		{
			var Metas = new List<ChunkMeta>();
			var AtomData = Atom.AtomData;

			var Offset = 0;
			//	https://developer.apple.com/library/content/documentation/QuickTime/QTFF/QTFFChap2/qtff2.html
			var Version = Get8(AtomData, ref Offset);
			/*var Flags =*/ Get24(AtomData, ref Offset);
			var EntryCount = Get32(AtomData, ref Offset);

			var MetaSize = 3 * 4;
			for (int i = Offset; i < AtomData.Length; i += MetaSize)
			{
				var Meta = new ChunkMeta(AtomData, i);
				Metas.Add(Meta);
			}
			if (Metas.Count() != EntryCount)
				Debug.LogWarning("Expected " + EntryCount + " chunk metas, got " + Metas.Count());

			return Metas;
		}

		static List<long> GetChunkOffsets(TAtom? Offset32sAtom, TAtom? Offset64sAtom, System.Func<long, byte[]> ReadData)
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
			var AtomData = Atom.AtomData;

			var Offset = 0;
			var Version = Get8(AtomData, ref Offset);
			//var Version = AtomData[8];
			/*var Flags = */Get24(AtomData, ref Offset);
			var EntryCount = Get32(AtomData, ref Offset);
			if (OffsetSize <= 0)
				throw new System.Exception("Invalid offset size: " + OffsetSize);
			for (int i = Offset; i < AtomData.Length; i += OffsetSize)
			{
				if (OffsetSize * 8 == 32)
				{
					var ChunkOffset = Get32(AtomData[i + 0], AtomData[i + 1], AtomData[i + 2], AtomData[i + 3]);
					Offsets.Add(ChunkOffset);
				}
				else if (OffsetSize * 8 == 64)
				{
					var ChunkOffset = Get64(AtomData[i + 0], AtomData[i + 1], AtomData[i + 2], AtomData[i + 3], AtomData[i + 4], AtomData[i + 5], AtomData[i + 6], AtomData[i + 7]);
					Offsets.Add(ChunkOffset);
				}
			}
			if (Offsets.Count() != EntryCount)
				Debug.LogWarning("Expected " + EntryCount + " chunks, got " + Offsets.Count());

			return Offsets;
		}

		static bool[] GetSampleKeyframes(TAtom? SyncSamplesAtom, System.Func<long, byte[]> ReadData, int SampleCount)
		{
			var Keyframes = new bool[SampleCount];
			var Default = (SyncSamplesAtom == null) ? true : false;

			for (var i = 0; i < Keyframes.Length;	i++ ) 
				Keyframes[i] = Default;

			if (SyncSamplesAtom == null)
				return Keyframes;

			//	read table and set keyframed
			var Atom = SyncSamplesAtom.Value;
			var AtomData = Atom.AtomData;

			var Offset = 0;
			var Version = Get8( AtomData, ref Offset);
			/*var Flags =*/ Get24(AtomData, ref Offset);
			var EntryCount = Get32(AtomData, ref Offset);

			//	each entry in the table is the size of a sample (and one chunk can have many samples)
			var StartOffset = Offset;

			if (EntryCount > 0)
			{
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
			}
			return Keyframes;
		}


		static List<int> GetSampleDurations(TAtom Atom, System.Func<long, byte[]> ReadData, int? ExpectedSampleCount)
		{
			var Durations = new List<int>();

			var AtomData = Atom.AtomData;

			var Offset = 0;
			var Version = Get8(AtomData, ref Offset);
			/*var Flags =*/ Get24(AtomData, ref Offset);
			var EntryCount = Get32(AtomData, ref Offset);
			var StartOffset = Offset;

			//	read durations as we go
			for (int i = StartOffset; i < AtomData.Length; i += 4 + 4)
			{
				var SampleCount = Get32(AtomData[i + 0], AtomData[i + 1], AtomData[i + 2], AtomData[i + 3]);
				var SampleDuration = Get32(AtomData[i + 4], AtomData[i + 5], AtomData[i + 6], AtomData[i + 7]);

				for (int s = 0; s < SampleCount; s++)
					Durations.Add(SampleDuration);
			}

			if (Durations.Count != EntryCount)
			{
				//	gr: for some reason, EntryCount is often 1, but there are more samples 
				//	throw new System.Exception("Expected " + EntryCount  + "(EntryCount) got " + Durations.Count);
			}

			if (ExpectedSampleCount != null)
				if (ExpectedSampleCount.Value != Durations.Count)
					throw new System.Exception("Expected " + ExpectedSampleCount.Value + " got " + Durations.Count);
				
			return Durations;
		}

		static List<int> GetSampleDurations(TAtom? Atom, System.Func<long, byte[]> ReadData, int Default,int? ExpectedSampleCount)
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

			return GetSampleDurations(Atom.Value, ReadData, ExpectedSampleCount);
		}

		static List<long> GetSampleSizes(TAtom Atom, System.Func<long, byte[]> ReadData)
		{
			var Sizes = new List<long>();
			var AtomData = Atom.AtomData;

			//	https://developer.apple.com/library/content/documentation/QuickTime/QTFF/QTFFChap2/qtff2.html
			var Offset = 0;
			var Version = Get8( AtomData, ref Offset );
			var Flags = Get24(AtomData, ref Offset);
			var SampleSize = Get32(AtomData, ref Offset);
			var EntryCount = Get32(AtomData, ref Offset);

			//	if size specified, they're all this size
			if (SampleSize != 0)
			{
				for (int i = 0; i < EntryCount; i++)
					Sizes.Add(SampleSize);
				return Sizes;
			}

			//	each entry in the table is the size of a sample (and one chunk can have many samples)
			var SampleSizeStart = Offset;

			if (EntryCount > 0)
			{
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
			}
			if (Sizes.Count() != EntryCount)
				Debug.LogWarning("Expected " + EntryCount + " sample sizes, got " + Sizes.Count());

			return Sizes;
		}


		static TMediaHeader DecodeAtom_MediaHeader(TAtom Atom, System.Func<long, byte[]> ReadData)
		{
			var AtomData = Atom.AtomData;

			var Offset = 0;
			var Version = Get8(AtomData,ref Offset);
			/*var Flags =*/Get24(AtomData, ref Offset);
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

		static TMovieHeader DecodeAtom_MovieHeader(TAtom Atom, System.Func<long, byte[]> ReadData)
		{
			var AtomData = Atom.AtomData;

			//	https://developer.apple.com/library/content/documentation/QuickTime/QTFF/art/qt_l_095.gif
			var Offset = 0;
			var Version = Get8(AtomData,ref Offset);
			var Flags = Get24(AtomData,ref Offset);

			//	hololens had what looked like 64 bit timestamps...
			//	this is my working reference :)
			//	https://github.com/macmade/MP4Parse/blob/master/source/MP4.MVHD.cpp#L50
			long CreationTime, ModificationTime, Duration;
			int TimeScale;
			if (Version == 0)
			{
				CreationTime = Get32(AtomData, ref Offset);
				ModificationTime = Get32(AtomData, ref Offset);
				TimeScale = Get32(AtomData, ref Offset);
				Duration = Get32(AtomData, ref Offset);
			}
			else if(Version == 1)
			{
				CreationTime = Get64(AtomData, ref Offset);
				ModificationTime = Get64(AtomData, ref Offset);
				TimeScale = Get32(AtomData, ref Offset);
				Duration = Get64(AtomData, ref Offset);
			}
			else
			{
				throw new System.Exception("Expected Version 0 or 1 for MVHD. If neccessary can probably continue without timing info!");
			}
			/*var PreferredRate =*/ Get32(AtomData,ref Offset);
			/*var PreferredVolume =*/ Get16(AtomData,ref Offset);   //	8.8 fixed point volume
			var Reserved = GetN(AtomData, 10, ref Offset);

			var Matrix_a = Get32(AtomData, ref Offset);
			var Matrix_b = Get32(AtomData, ref Offset);
			var Matrix_u = Get32(AtomData, ref Offset);
			var Matrix_c = Get32(AtomData, ref Offset);
			var Matrix_d = Get32(AtomData, ref Offset);
			var Matrix_v = Get32(AtomData, ref Offset);
			var Matrix_x = Get32(AtomData, ref Offset);
			var Matrix_y = Get32(AtomData, ref Offset);
			var Matrix_w = Get32(AtomData, ref Offset);

			/*var PreviewTime =*/Get32(AtomData,ref Offset);
			var PreviewDuration = Get32(AtomData,ref Offset);
			/*var PosterTime =*/ Get32(AtomData,ref Offset);
			/*var SelectionTime =*/ Get32(AtomData,ref Offset);
			/*var SelectionDuration =*/ Get32(AtomData,ref Offset);
			/*var CurrentTime =*/ Get32(AtomData,ref Offset);
			/*var NextTrackId =*/ Get32(AtomData,ref Offset);

			foreach (var Zero in Reserved)
			{
				if (Zero != 0)
					Debug.LogWarning("Reserved value " + Zero + " is not zero");
			}

			//	actually a 3x3 matrix, but we make it 4x4 for unity
			//	gr: do we need to transpose this? docs don't say row or column major :/
			//	wierd element labels, right? spec uses them.

			//	gr: matrixes arent simple
			//		https://developer.apple.com/library/archive/documentation/QuickTime/QTFF/QTFFChap4/qtff4.html#//apple_ref/doc/uid/TP40000939-CH206-18737
			//	All values in the matrix are 32 - bit fixed-point numbers divided as 16.16, except for the { u, v, w}
			//	column, which contains 32 - bit fixed-point numbers divided as 2.30.Figure 5 - 1 and Figure 5 - 2 depict how QuickTime uses matrices to transform displayed objects.
			var a = Fixed1616ToFloat(Matrix_a);
			var b = Fixed1616ToFloat(Matrix_b);
			var u = Fixed230ToFloat(Matrix_u);
			var c = Fixed1616ToFloat(Matrix_c);
			var d = Fixed1616ToFloat(Matrix_d);
			var v = Fixed230ToFloat(Matrix_v);
			var x = Fixed1616ToFloat(Matrix_x);
			var y = Fixed1616ToFloat(Matrix_y);
			var w = Fixed230ToFloat(Matrix_w);
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


		public static void Parse(string Filename, System.Action<TTrack> EnumTrack)
		{
			throw new System.Exception("Todo: make lambda for chunk-by-chunk reading");
			var FileData = File.ReadAllBytes(Filename);
			long BytesRead = 0;

			//Parse(FileData, out BytesRead, EnumTrack);
		}

		//	this interface lets you parse an mp4 asynchronously.
		//	whilst ReadData lets you stream in data, we still need FileOffset because 
		//	chunk/sample positions in the mp4 in non-streaming mp4's are relative to
		//	the file position, not the mdat.
		public static void ParseNextAtom(System.Func<long, byte[]> ReadData,long FilePosition, System.Action<List<TTrack>> EnumTracks,System.Action<TAtom> EnumMdat)
		{
			var NextAtom = PopX.Atom.GetNextAtom(ReadData, FilePosition);
			//	if we get no atom and no exception is thrown, we're out of data. But the caller should know that.
			if (!NextAtom.HasValue)
				throw new System.Exception("Failed to get next atoms");
			var NewAtom = NextAtom.Value;


			//	handle the atom
			if (NewAtom.Fourcc == "moov")
			{
				List<TTrack> Tracks = null;
				TMovieHeader? Header;

				//	decode moov (tracks, sample data etc)
				var MoovAtom = NewAtom;
				DecodeAtom_Moov(out Tracks, out Header, MoovAtom, ReadData);

				EnumTracks(Tracks);
			}
			else if ( NewAtom.Fourcc == "moof")
			{
				TMovieHeader? Header;

				var MoofAtom = NewAtom;
				var MdatIdent = 999;    //	currently just indexed

				List<TTrack> MoofTracks;
				DecodeAtom_Moof(out MoofTracks, out Header, MoofAtom, ReadData, MdatIdent);

				EnumTracks(MoofTracks);
				/*
				//	todo: change accessor/give accessor
				var Mdat = MdatAtoms[MdatIdent];

				while (Tracks.Count < mfi)
				{
					Tracks.Add(new TTrack());
				}
				if (MoofTrack.SampleDescriptions != null)
					Tracks[mfi].SampleDescriptions.AddRange(MoofTrack.SampleDescriptions);
				if (MoofTrack.Samples != null)
					Tracks[mfi].Samples.AddRange(MoofTrack.Samples);
				*/

			}
			else if ( NewAtom.Fourcc == "mdat" )
			{
				//	just followed a moov or moof
				//	enum it with appropriate track/sample meta
				EnumMdat(NewAtom);
			}
		}

		static void DecodeAtom_Moov(out List<TTrack> Tracks,out TMovieHeader? MovieHeader,TAtom Moov,System.Func<long, byte[]> ReadData)
		{
			var NewTracks = new List<TTrack>();

			//	get header first
			var MovieHeaderAtom = Atom.GetChildAtom(Moov, "mvhd", ReadData);
			if (MovieHeaderAtom != null)
				MovieHeader = DecodeAtom_MovieHeader(MovieHeaderAtom.Value, ReadData);
			else
				MovieHeader = null;

			//	gotta be local to be used in lambda
			var TimeScale = MovieHeader.HasValue ? MovieHeader.Value.TimeScale : 1;
			System.Action<TAtom> EnumMoovChildAtom = (Atom) =>
			{
				if (Atom.Fourcc == "trak")
				{
					var Track = new TTrack();
					DecodeAtom_Track( ref Track, Atom, null, TimeScale, ReadData);
					NewTracks.Add(Track);
				}
			};
			Atom.DecodeAtomChildren(EnumMoovChildAtom, Moov);
			Tracks = NewTracks;
		}


		//	microsoft seems to have the best reference
		//	https://msdn.microsoft.com/en-us/library/ff469287.aspx
		static void DecodeAtom_Moof(out List<TTrack> Tracks, out TMovieHeader? MovieHeader, TAtom Moof, System.Func<long, byte[]> ReadData, int? MdatIdent)
		{
			var MoofTracks = new List<TTrack>();
			MovieHeader = null;
			/*
			//	get header first
			var MovieHeaderAtom = Atom.GetChildAtom(Moov, "mfhd", FileData);
			if (MovieHeaderAtom != null)
				MovieHeader = DecodeAtom_MovieHeader(MovieHeaderAtom.Value, FileData);
			else
				MovieHeader = null;
			*/

			//	find this scale!
			var TimeScale = 1.0f / 10000000.0f;
			System.Action<TAtom> EnumMoovChildAtom = (Atom) =>
			{
				//	if ( Atom.Fourcc == "mfhd"
				if (Atom.Fourcc == "traf")
				{
					var Track = new TTrack();
					DecodeAtom_TrackFragment(ref Track, Atom, Moof, null, MdatIdent, TimeScale, ReadData);
					MoofTracks.Add(Track);
				}
			};
			Atom.DecodeAtomChildren(EnumMoovChildAtom, Moof);
			Tracks = MoofTracks;
		}


		struct FragmentHeader
		{
			//	tfhd
			public int TrackId;
			public long? BaseDataOffset;
			public int? SampleDescriptionIndex;
			public int? DefaultSampleDuration;
			public int? DefaultSampleSize;
			public int? DefaultSampleFlags;
			public bool? DurationIsEmpty;
			public bool? DefaultBaseIsMoof;

			public long? DecodeTime;     //	from tfdt
		};

		//	tfdt
		static void DecodeAtom_TrackFragmentDelta(ref FragmentHeader Header, TAtom Tfdt)
		{
			var AtomData = Tfdt.AtomData;
			var Offset = 0;
			var Version = Get8(AtomData, ref Offset);
			var Flags = Get24(AtomData, ref Offset);

			Header.DecodeTime = (Version == 0) ? Get32(AtomData, ref Offset) : Get64(AtomData, ref Offset);
		}

		//	tfhd
		static FragmentHeader DecodeAtom_TrackFragmentHeader(TAtom Tfhd, TAtom Tfdt)
		{
			FragmentHeader Header = new FragmentHeader();
			var AtomData = Tfhd.AtomData;
			var Offset = 0;
			var Version = Get8(AtomData, ref Offset);
			var Flags = Get24(AtomData, ref Offset);
			Header.TrackId = Get32(AtomData, ref Offset);

			System.Func<int, bool> HasFlagBit = (Bit) => { return (Flags & (1 << (int)Bit)) != 0; };

			//	http://178.62.222.88/mp4parser/mp4.js
			if (HasFlagBit(0))
				Header.BaseDataOffset = Get64(AtomData, ref Offset);	//	unsigned
			if (HasFlagBit(1))
				Header.SampleDescriptionIndex = Get32(AtomData, ref Offset);
			if (HasFlagBit(3))
				Header.DefaultSampleDuration = Get32(AtomData, ref Offset);
			if (HasFlagBit(4))
				Header.DefaultSampleSize = Get32(AtomData, ref Offset);
			if (HasFlagBit(5))
				Header.DefaultSampleFlags = Get32(AtomData, ref Offset);
			if (HasFlagBit(16))
				Header.DurationIsEmpty = true;
			if (HasFlagBit(17))
				Header.DefaultBaseIsMoof = true;

			DecodeAtom_TrackFragmentDelta(ref Header, Tfdt);
			return Header;
		}

		//	mp4 parser and ms docs contradict themselves
		enum TrunFlags  //	mp4 parser
		{
			DataOffsetPresent = 0,
			FirstSampleFlagsPresent = 2,
			SampleDurationPresent = 8,
			SampleSizePresent = 9,
			SampleFlagsPresent = 10,
			SampleCompositionTimeOffsetPresent = 11
		}
		/*
		enum TrunFlags  //	ms (matching hololens stream)
		{
			DataOffsetPresent = 0,
			FirstSampleFlagsPresent = 3,
			SampleDurationPresent = 9,
			SampleSizePresent = 10,
			SampleFlagsPresent = 11,
			SampleCompositionTimeOffsetPresent = 12
		};
		*/

		//	trun
		static List<TSample> DecodeAtom_FragmentSampleTable(TAtom Atom, FragmentHeader Header,TAtom MoofAtom, float TimeScale, System.Func<long, byte[]> ReadData,int? MDatIdent)
		{
			var AtomData = Atom.AtomData;

			//	this stsd description isn't well documented on the apple docs
			//	http://xhelmboyx.tripod.com/formats/mp4-layout.txt
			//	https://stackoverflow.com/a/14549784/355753
			//var Version = AtomData[8];
			var Offset = 0;
			var Version = Get8(AtomData, ref Offset);
			var Flags = Get24(AtomData, ref Offset);
			var EntryCount = Get32(AtomData, ref Offset);


			//	gr; with a fragmented mp4 the headers were incorrect (bad sample sizes, mismatch from mp4parser's output)
			//	ffmpeg -i cat_baseline.mp4 -c copy -movflags frag_keyframe+empty_moov cat_baseline_fragment.mp4
			//	http://178.62.222.88/mp4parser/mp4.js
			//	so trying this version
			//	VERSION8
			//	FLAGS24
			//	SAMPLECOUNT32

			//	https://msdn.microsoft.com/en-us/library/ff469478.aspx
			//	the docs on which flags are which are very confusing (they list either 25 bits or 114 or I don't know what)
			//	0x0800 is composition|size|duration
			//	from a stackoverflow post, 0x0300 is size|duration
			//	0x0001 is offset from http://mp4parser.com/
			System.Func<TrunFlags, bool> IsFlagBit = (Bit) => { return (Flags & (1 << (int)Bit)) != 0; };
			var SampleSizePresent = IsFlagBit(TrunFlags.SampleSizePresent);
			var SampleDurationPresent = IsFlagBit(TrunFlags.SampleDurationPresent);
			var SampleFlagsPresent = IsFlagBit(TrunFlags.SampleFlagsPresent);
			var SampleCompositionTimeOffsetPresent = IsFlagBit(TrunFlags.SampleCompositionTimeOffsetPresent);
			var FirstSampleFlagsPresent = IsFlagBit(TrunFlags.FirstSampleFlagsPresent);
			var DataOffsetPresent = IsFlagBit(TrunFlags.DataOffsetPresent);

			//	This field MUST be set.It specifies the offset from the beginning of the MoofBox field(section 2.2.4.1).
			//	gr:... to what?
			//	If only one TrunBox is specified, then the DataOffset field MUST be the sum of the lengths of the MoofBox and all the fields in the MdatBox field(section 2.2.4.8).
			//	basically, start of mdat data (which we know anyway)
			if (!DataOffsetPresent)
				throw new System.Exception("Expected data offset to be always set");
			var DataOffsetFromMoof = DataOffsetPresent ? Get32(AtomData, ref Offset) : 0;

			System.Func<int, int> TimeToMs = (TimeUnit) =>
			{
				//	to float
				var Timef = TimeUnit * TimeScale;
				var TimeMs = Timef * 1000.0f;
				return (int)TimeMs;
			};

			//	DataOffset(4 bytes): This field MUST be set.It specifies the offset from the beginning of the MoofBox field(section 2.2.4.1).
			//	If only one TrunBox is specified, then the DataOffset field MUST be the sum of the lengths of the MoofBo
			//	gr: we want the offset into the mdat, but we would have to ASSUME the mdat follows this moof
			//		just for safety, we work out the file offset instead, as we know where the start of the moof is
			var DataFileOffset = DataOffsetFromMoof + MoofAtom.FilePosition;


			var Samples = new List<TSample>();
			var CurrentDataStartPosition = DataFileOffset;
			var CurrentTime = 0;
			var FirstSampleFlags = 0;
			if (FirstSampleFlagsPresent )
			{
				FirstSampleFlags = Get32(AtomData, ref Offset);
			}

			//	when the fragments are really split up into 1sample:1dat a different box specifies values
			var DefaultSampleDuration = Header.DefaultSampleDuration.HasValue ? Header.DefaultSampleDuration.Value : 0;
			var DefaultSampleSize = Header.DefaultSampleSize.HasValue ? Header.DefaultSampleSize.Value : 0;
			var DefaultSampleFlags = Header.DefaultSampleFlags.HasValue ? Header.DefaultSampleFlags.Value : 0;

			for (int sd = 0; sd < EntryCount; sd++)
			{
				var SampleDuration = SampleDurationPresent ? Get32(AtomData, ref Offset) : DefaultSampleDuration;
				var SampleSize = SampleSizePresent ? Get32(AtomData, ref Offset) : DefaultSampleSize;
				var TrunBoxSampleFlags = SampleFlagsPresent ? Get32(AtomData, ref Offset) : DefaultSampleFlags;
				var SampleCompositionTimeOffset = SampleCompositionTimeOffsetPresent ? Get32(AtomData, ref Offset) : 0;

				if (SampleCompositionTimeOffsetPresent)
				{
					//	correct CurrentTimeMs?
				}

				var Sample = new TSample();
				Sample.MDatIdent = MDatIdent.HasValue ? MDatIdent.Value : -1;
				Sample.DataFilePosition = CurrentDataStartPosition;
				Sample.DataSize = SampleSize;
				Sample.DurationMs = TimeToMs(SampleDuration);
				Sample.IsKeyframe = false;
				Sample.DecodeTimeMs = TimeToMs(CurrentTime);
				Sample.PresentationTimeMs = TimeToMs(CurrentTime+SampleCompositionTimeOffset);
				Samples.Add(Sample);

				CurrentTime += SampleDuration;
				CurrentDataStartPosition += SampleSize;
			}

			return Samples;
		}

		static List<TSample> DecodeAtom_SampleTable(TAtom StblAtom, TAtom? MdatAtom,float TimeScale, System.Func<long, byte[]> ReadData)
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
		
			PopX.Atom.DecodeAtomChildren(EnumStblAtom, StblAtom);

			//	work out samples from atoms!
			if (SampleSizesAtom == null)
				throw new System.Exception("Track missing sample sizes atom");
			if (ChunkOffsets32Atom == null && ChunkOffsets64Atom == null)
				throw new System.Exception("Track missing chunk offset atom");
			if (SampleToChunkAtom == null)
				throw new System.Exception("Track missing sample-to-chunk table atom");
			if (SampleDecodeDurationsAtom == null)
				throw new System.Exception("Track missing time-to-sample table atom");

			var PackedChunkMetas = GetChunkMetas(SampleToChunkAtom.Value, ReadData);
			var ChunkOffsets = GetChunkOffsets(ChunkOffsets32Atom, ChunkOffsets64Atom, ReadData);
			var SampleSizes = GetSampleSizes(SampleSizesAtom.Value, ReadData);
			var SampleKeyframes = GetSampleKeyframes(SyncSamplesAtom, ReadData, SampleSizes.Count);
			var SampleDurations = GetSampleDurations(SampleDecodeDurationsAtom.Value, ReadData, SampleSizes.Count);
			var SamplePresentationTimeOffsets = GetSampleDurations(SamplePresentationTimeOffsetsAtom, ReadData, 0, SampleSizes.Count);

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

			//	we're now expecting this to be here
			var MdatStartPosition = MdatAtom.HasValue ? MdatAtom.Value.AtomDataFilePosition : (long?)null;

			//	superfolous data
			var Chunks = new List<TSample>();
			long? MdatEnd = (MdatAtom.HasValue) ? (MdatAtom.Value.DataSize) : (long?)null;
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
				var ChunkFileOffset = ChunkOffsets[ChunkIndex];

				for (int s = 0; s < SampleMeta.SamplesPerChunk; s++)
				{
					var Sample = new TSample();

					if (MdatStartPosition.HasValue)
						Sample.DataPosition = ChunkFileOffset - MdatStartPosition.Value;
					else
						Sample.DataFilePosition = ChunkFileOffset;

					Sample.DataSize = SampleSizes[SampleIndex];
					Sample.IsKeyframe = SampleKeyframes[SampleIndex];
					Sample.DecodeTimeMs = TimeToMs( SampleDecodeTimes[SampleIndex] );
					Sample.DurationMs = TimeToMs( SampleDurations[SampleIndex] );
					Sample.PresentationTimeMs = TimeToMs( SampleDecodeTimes[SampleIndex] + SamplePresentationTimeOffsets[SampleIndex] );
					Samples.Add(Sample);

					ChunkFileOffset += Sample.DataSize;
					SampleIndex++;
				}
			}

			if (SampleIndex != SampleSizes.Count)
				Debug.LogWarning("Enumerated " + SampleIndex + " samples, expected " + SampleSizes.Count);

			return Samples;
		}

		public struct AVCDecoderConfigurationRecord
		{
			public const string Fourcc = "avc1";
			//public TAtom AvccAtom;

			public AVCDecoderConfigurationRecord(byte[] Data)
			{
				//	quicktime sample description header
			}
		}

		public struct TTrackSampleDescription
		{
			public string	Fourcc;   //	gr: this is 4 bytes, but might not actually be a fourcc?
			//public int		DataReferenceIndex;	//	stopped reading this, revisit the content of stsd which comes before the avcC atom
			public byte[]	AvccAtomData;     //	codec data taken out of the avcC(inside avc1) atom
		};

		static List<TTrackSampleDescription> GetTrackSampleDescriptions(TAtom Atom, System.Func<long, byte[]> ReadData)
		{
			var AtomData = Atom.AtomData;

			//	this stsd description isn't well documented on the apple docs
			//	http://xhelmboyx.tripod.com/formats/mp4-layout.txt
			//	https://stackoverflow.com/a/14549784/355753
			var Offset = 0;
			var Version = Get8(AtomData, ref Offset);
			/*var Flags = */Get24(AtomData, ref Offset);
			var EntryCount = Get32(AtomData, ref Offset); 

			var SampleDescriptions = new List<TTrackSampleDescription>();

			System.Func<long,byte[]> ReadAtomDataData = (long Size)=>
			{
				var Data = AtomData.SubArray(Offset, Size);
				Offset += (int)Size;
				return Data;
			};

			for (int sd = 0; sd < EntryCount;	sd++ )
			{
				//	https://stackoverflow.com/a/14549784/355753
				//	each sample is an atom/box
				var SampleDescriptionAtom = PopX.Atom.GetNextAtom(ReadAtomDataData, 0).Value;
				var SampleDescription = new TTrackSampleDescription();
				SampleDescription.Fourcc = SampleDescriptionAtom.Fourcc;
				if (SampleDescription.Fourcc == "avc1")
				{
					//	gr: these are the quicktime headers I think
					//		looking at atomic parsely, I think this data is expected, we're just jumping over it to the AVCC atom
					//		so 80-4 is probably the avcC fourcc and a length before that
					var QuicktimeHeaderSize = 78;
					var Start = 0 + QuicktimeHeaderSize + SampleDescriptionAtom.HeaderSize;
					//var Start = Atom.FileOffset + QuicktimeHeaderSize + HeaderSize;
					SampleDescription.AvccAtomData = SampleDescriptionAtom.AtomData.SubArray(Start, SampleDescriptionAtom.AtomData.Length - Start);
					//SampleDescription.AvccAtom = SampleDescriptionAtom;
				}
				/*
				var Data = AtomData.SubArray(Offset, DataSize);

				TAtom x;
				x.Init()
				var OffsetStart = Offset;
				var Size = Get32(AtomData, ref Offset);
				var Fourcc = Get8x4(AtomData, ref Offset);
				var Reserved = GetN(AtomData, 6, ref Offset);
				var DataReferenceIndex = Get16(AtomData, ref Offset);

				//	read the remaining data
				var HeaderSize = (Offset - OffsetStart);
				var DataSize = Size - HeaderSize;
				var Data = AtomData.SubArray(Offset, DataSize);
				Offset += DataSize;

				var SampleDescription = new TTrackSampleDescription();
				SampleDescription.DataReferenceIndex = DataReferenceIndex;
				//SampleDescription.Data = Data;
				if (AvccAtom.HasValue)
					SampleDescription.AvccAtomData = AvccAtom.Value.AtomData;
				SampleDescription.Fourcc = Encoding.ASCII.GetString(Format);
				/*
				//	gr: temp solution, rip off the header to get the encoder's header atom
				if ( SampleDescription.Fourcc == "avc1" )
				{
					//	gr: these are the quicktime headers I think
					var QuicktimeHeaderSize = 86;
					var Start = 0 + QuicktimeHeaderSize + HeaderSize;
					//var Start = Atom.FileOffset + QuicktimeHeaderSize + HeaderSize;
					System.Func<long, byte[]> ReadAvccData = (long ReadDataSize) =>
					{
						var SubData = AtomData.SubArray(Start, ReadDataSize);
						Start += (int)ReadDataSize;
						return SubData;
					};
					//var AvccAtom = PopX.Atom.GetNextAtom(ReadAvccData);
					SampleDescription.AvccAtom = AvccAtom;
					if ( AvccAtom.HasValue )
						SampleDescription.AvccAtomData = AvccAtom.Value.AtomData;
				}
				*/
				SampleDescriptions.Add(SampleDescription);
			}

			return SampleDescriptions;
		}


		//	traf
		static void DecodeAtom_TrackFragment(ref TTrack Track, TAtom Trak, TAtom Moof,TAtom? MdatAtom,int? MdatIdent,float MovieTimeScale, System.Func<long, byte[]> ReadData)
		{
			List<TSample> TrackSamples = null;
			List<TTrackSampleDescription> TrackSampleDescriptions = null;

			TAtom? Tfhd = null;
			TAtom? Tfdt = null;

			System.Action<TAtom> EnumTrakChild = (Atom) =>
			{
				if (Atom.Fourcc == "tfhd")
					Tfhd = Atom;
				if (Atom.Fourcc == "tfdt")
					Tfdt = Atom;
				if (Atom.Fourcc == "trun")
				{
					var Header = DecodeAtom_TrackFragmentHeader(Tfhd.Value, Tfdt.Value);
					TrackSamples = DecodeAtom_FragmentSampleTable(Atom, Header, Moof, MovieTimeScale, ReadData, MdatIdent);
				}
			};

			//	go through the track
			PopX.Atom.DecodeAtomChildren(EnumTrakChild, Trak);

			Track.SampleDescriptions = TrackSampleDescriptions;
			Track.Samples = TrackSamples;
		}

		static void DecodeAtom_Track(ref TTrack Track,TAtom Trak,TAtom? MdatAtom,float MovieTimeScale, System.Func<long, byte[]> ReadData)
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
					TrackSamples = DecodeAtom_SampleTable(Atom, MdatAtom, TrackTimeScale, ReadData);

					var SampleDescriptionAtom = PopX.Atom.GetChildAtom(Atom, "stsd", ReadData);
					if (SampleDescriptionAtom != null)
						TrackSampleDescriptions = GetTrackSampleDescriptions(SampleDescriptionAtom.Value, ReadData);
				}
			};
			System.Action<TAtom> EnumMdiaAtom = (Atom) =>
			{
				if (Atom.Fourcc == "mdhd")
					MediaHeader = DecodeAtom_MediaHeader(Atom, ReadData);

				if (Atom.Fourcc == "minf")
					PopX.Atom.DecodeAtomChildren(EnumMinfAtom, Atom, ReadData);
			};
			System.Action<TAtom> EnumTrakChild = (Atom) =>
			{
				//EnumAtom(Atom);
				if (Atom.Fourcc == "mdia")
					PopX.Atom.DecodeAtomChildren(EnumMdiaAtom, Atom, ReadData);
			};

			//	go through the track
			PopX.Atom.DecodeAtomChildren(EnumTrakChild, Trak, ReadData);

			Track.SampleDescriptions = TrackSampleDescriptions;
			Track.Samples = TrackSamples;
		}

	}
}
