using System.Collections.Generic;
using System.Text;

namespace LOM_UI_EN
{
	/// <summary>
	/// A copy of the parser Binarizer uses (Ideafixxxer.CsvParser in the game's Fungus.dll), for when a game update moves or
	/// changes that one: same state machine, so the table reads the same either way. Lines split at \n and \r\n, empty lines
	/// skipped, a line break inside quotes kept as \r\n.
	/// </summary>
	internal static class IdeaCsv
	{
		private const int LineStart = 0;
		private const int ValueStart = 1;
		private const int Value = 2;
		private const int Quoted = 3;
		private const int QuoteInQuoted = 4;

		public static string[][] Parse(string data)
		{
			List<string[]> lines = new List<string[]>();
			List<string> cur = new List<string>();
			StringBuilder val = new StringBuilder();
			int st = LineStart;
			int pos = 0;
			int n = data.Length;
			while (pos <= n)
			{
				// Next physical line: up to \n (a \r right before it belongs to the break), or the end.
				int end = data.IndexOf('\n', pos);
				int next;
				if (end < 0)
				{
					end = n;
					next = n + 1;
				}
				else
				{
					next = end + 1;
					if (end > pos && data[end - 1] == '\r')
					{
						end--;
					}
				}
				if (end > pos)
				{
					for (int i = pos; i < end; i++)
					{
						char c = data[i];
						if (c == ',')
						{
							if (st == Quoted)
							{
								val.Append(',');
							}
							else
							{
								cur.Add(val.ToString());
								val.Length = 0;
								st = ValueStart;
							}
						}
						else if (c == '"')
						{
							switch (st)
							{
							case LineStart:
							case ValueStart:
								st = Quoted;
								break;
							case Value:
								val.Append('"');
								break;
							case Quoted:
								st = QuoteInQuoted;
								break;
							default:
								val.Append('"');
								st = Quoted;
								break;
							}
						}
						else
						{
							val.Append(c);
							if (st == LineStart || st == ValueStart)
							{
								st = Value;
							}
							else if (st == QuoteInQuoted)
							{
								st = Quoted;
							}
						}
					}
					if (st == LineStart)
					{
						lines.Add(cur.ToArray());
						cur.Clear();
					}
					else if (st == Quoted)
					{
						val.Append("\r\n");
					}
					else
					{
						cur.Add(val.ToString());
						val.Length = 0;
						lines.Add(cur.ToArray());
						cur.Clear();
						st = LineStart;
					}
				}
				pos = next;
			}
			if (val.Length > 0)
			{
				cur.Add(val.ToString());
			}
			if (cur.Count > 0)
			{
				lines.Add(cur.ToArray());
			}
			return lines.ToArray();
		}
	}
}
