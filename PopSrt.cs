using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using System.Text.RegularExpressions;



namespace PopX
{
	public static class Srt
	{
		public struct Subtitle
		{
			public ulong StartTimeMs;
			public ulong EndTimeMs;
			public int SubtitleIndex;
			public string Content;
		};

		//	todo: enum all \n\n breaks
		//	todo: stream/lambda the content input
		public static void Parse(string FileContents, System.Action<Subtitle> EnumSubtitle)
		{
			//	forgive me.
			var SubtitleChunks = FileContents.Split(new string[] { "\n\n" }, System.StringSplitOptions.RemoveEmptyEntries);

			foreach (var SubtitleChunk in SubtitleChunks)
			{
				//	todo: check for continuity errors, time out of sync etc.. if that reaaaally matters
				ParseSubtitleChunk(SubtitleChunk, EnumSubtitle);
			}
		}

		public static void ParseSubtitleChunk(string SubtitleChunk, System.Action<Subtitle> EnumSubtitle)
		{
			//	expecting...
			//		index\n
			//		starttime --> endtime\n
			//		content\n
			//		\n\n	gr: already cut

			//	forgive me.
			var Lines = SubtitleChunk.Split(new string[] { "\n" }, System.StringSplitOptions.None);
			if (Lines.Length != 3)
				throw new System.Exception("Expected 3 (got " + Lines.Length + ") lines in the subtitle chunk: " + SubtitleChunk);

			var Subtitle = new Subtitle();
			Subtitle.SubtitleIndex = int.Parse(Lines[0]);
			ParseTimecodeRange(Lines[1], out Subtitle.StartTimeMs, out Subtitle.EndTimeMs);
			Subtitle.Content = Lines[2];

			EnumSubtitle(Subtitle);
		}

		//	parse the timecode line;
		//	HH:MM:SS,MMM --> HH:MM:SS,MMM
		static void ParseTimecodeRange(string Line, out ulong StartTimeMs, out ulong EndTimeMs)
		{
			var Elements = ParseTimecodeRange(Line);
			StartTimeMs = TimeToMs(Elements[0], Elements[1], Elements[2], Elements[3]);
			EndTimeMs = TimeToMs(Elements[4], Elements[5], Elements[6], Elements[7]);
		}

		static ulong TimeToMs(ulong Hours, ulong Mins, ulong Seconds, ulong Millis)
		{
			ulong Time = Hours * (60 * 60 * 1000);
			Time += Mins * (60 * 1000);
			Time += Seconds * (1000);
			Time += Millis;
			return Time;
		}

		//	parse the timecode line;
		//	HH:MM:SS,MMM --> HH:MM:SS,MMM
		//	into 8 integers
		static List<ulong> ParseTimecodeRange(string Line)
		{
			//	regex
			var TimePattern = "([0-9]{2}):([0-9]{2}):([0-9]{2}),([0-9]{3})";
			var Pattern = new Regex(TimePattern + " --> " + TimePattern);
			var Match = Pattern.Match(Line);
			if (Match == null)
				throw new System.Exception("Timecode line doesn't match regex pattern; " + Line);

			//	make sure no ints are negative and add to list
			var Elements = new List<ulong>();
			for (var i = 1; i < 9; i++)
			{
				var NumberString = Match.Groups[i].Captures[0].Value;
				var NumberInt = ulong.Parse(NumberString);
				Elements.Add(NumberInt);
			}
			return Elements;
		}
	}
}
