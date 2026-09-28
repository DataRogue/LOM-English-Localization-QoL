using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace LOM_UI_EN
{
	/// <summary>How the OverLlm patch lays out its game data table in Mods/English.</summary>
	public enum BaseTableLayout
	{
		/// <summary>No table found.</summary>
		None,
		/// <summary>Releases up to 2026.02: one StringTable.csv in Binarizer's format (Ideafixxxer CSV, values may span lines, the
		/// first row of a key wins), read by the Binarizer plugin.</summary>
		Single,
		/// <summary>
		/// The llmkit-upgrade layout (release 2026.09.28): one CSV per game source file (Story_1.csv, System_zh-cn.csv, ...), read by
		/// the patch's own plugin FanslationStudio.LegendOfMortal.Plugin instead of Binarizer. That plugin reads every *.csv of the
		/// folder in turn (LlmKitCsv), a later row of a key replacing an earlier one. The folder also holds the patch's prefab and
		/// code text (*.yaml, read by its FanslationStudio.Plugins pack, see BaseGuards).
		/// </summary>
		PerFile
	}

	/// <summary>The OverLlm patch's game data table as installed: its layout and files, in the order they are read.</summary>
	public sealed class BaseTableSet
	{
		public const string SingleName = "StringTable.csv";

		public static readonly BaseTableSet Empty = new BaseTableSet();

		public BaseTableLayout Layout = BaseTableLayout.None;

		/// <summary>The folder the files are in (null when none was found).</summary>
		public string Dir;

		/// <summary>Single: the one StringTable.csv. PerFile: every other *.csv of the folder, in the order the patch's plugin reads them.</summary>
		public string[] Files = new string[0];

		/// <summary>PerFile only: a StringTable.csv of an older release still in the folder. This mod leaves it out (the per-file tables
		/// are newer), while the patch's plugin reads it with the rest, over part of the new text.</summary>
		public string Leftover;

		public bool Exists => Files.Length > 0;

		/// <summary>The path to name in logs and reports: the single file, or the folder.</summary>
		public string Where => (Layout == BaseTableLayout.Single) ? Files[0] : Dir;

		/// <summary>Changes when the files of the set change (for detecting a new install while the game runs).</summary>
		public string Signature
		{
			get
			{
				StringBuilder sb = new StringBuilder();
				sb.Append((int)Layout).Append('|').Append(Leftover != null ? "L" : "");
				foreach (string f in Files)
				{
					sb.Append('|').Append(Path.GetFileName(f));
				}
				return sb.ToString();
			}
		}

		public string Describe()
		{
			switch (Layout)
			{
			case BaseTableLayout.Single:
				return "one table (" + SingleName + ")";
			case BaseTableLayout.PerFile:
				return Files.Length + " tables" + ((Leftover != null) ? (" plus an old " + SingleName + " left out") : "");
			default:
				return "no table";
			}
		}

		/// <summary>A file name of the per-file layout: a game source file's name as the patch's dumper writes it (Story_12.csv,
		/// CombatEffects_zh-cn.csv; the game names the other languages' files _zh-tw and _kr).</summary>
		private static readonly Regex PerFileName = new Regex("^(Story_[0-9]+|[A-Za-z0-9]+(_[A-Za-z0-9]+)*_(zh-cn|zh-tw|kr))\\.csv$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

		/// <summary>
		/// Finds the table in englishDir (Mods/English), else the StringTable.csv of one of Binarizer's mod folders. The per-file
		/// layout needs one file named like a game source file; then every other *.csv of the folder belongs to it, as the patch's
		/// plugin reads them all. Its order is that plugin's (Directory.GetFiles; on NTFS, names case-insensitively sorted).
		/// </summary>
		public static BaseTableSet Find(string englishDir, IEnumerable<string> binarizerDirs)
		{
			string single = null;
			if (!string.IsNullOrEmpty(englishDir) && Directory.Exists(englishDir))
			{
				List<string> perFile = new List<string>();
				bool known = false;
				foreach (string f in Directory.GetFiles(englishDir, "*.csv", SearchOption.TopDirectoryOnly))
				{
					string name = Path.GetFileName(f);
					// Windows matches "*.csv" against longer extensions too (".csvx"); the plugin reads those, this mod does not.
					if (!name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}
					if (string.Equals(name, SingleName, StringComparison.OrdinalIgnoreCase))
					{
						single = f;
						continue;
					}
					perFile.Add(f);
					known |= PerFileName.IsMatch(name);
				}
				if (known)
				{
					perFile.Sort(StringComparer.OrdinalIgnoreCase);
					return new BaseTableSet
					{
						Layout = BaseTableLayout.PerFile,
						Dir = englishDir,
						Files = perFile.ToArray(),
						Leftover = single
					};
				}
			}
			if (single != null)
			{
				return new BaseTableSet
				{
					Layout = BaseTableLayout.Single,
					Dir = englishDir,
					Files = new string[1] { single }
				};
			}
			if (binarizerDirs != null)
			{
				foreach (string dir in binarizerDirs)
				{
					try
					{
						string q = Path.Combine(dir, SingleName);
						if (File.Exists(q))
						{
							return new BaseTableSet
							{
								Layout = BaseTableLayout.Single,
								Dir = dir,
								Files = new string[1] { q }
							};
						}
					}
					catch (Exception)
					{
					}
				}
			}
			return Empty;
		}
	}

	/// <summary>
	/// The CSV dialect of the llmkit-upgrade tables, read exactly as the patch's plugin reads them (StringTableInjectionPatches.
	/// LoadTranslations and CsvUtility.ParseFile of LegendOfMortalOverLlm/LegendOfMortalPlugin, release 2026.09.28): the whole file
	/// at once; a field is quoted when it holds a comma, a quote or a line break, with "" for a quote, so a quoted value may span
	/// lines (the packager writes real line breaks in thousands of values: Legend texts, character intros, item descriptions); a
	/// line break inside quotes is kept as LF; outside quotes CR, LF or CRLF ends a row; blank rows, rows with fewer than two fields
	/// and rows without a key are skipped; only the first two fields count, with the two characters \n turned into a line break.
	/// (The branch's first plugin read line by line and cut such values at their first line break; this mod read them whole
	/// then already.) No Unity or BepInEx types, so it can be tested outside the game.
	/// </summary>
	public static class LlmKitCsv
	{
		/// <summary>CsvUtility.ParseFile: every row of a file's text, as its fields.</summary>
		public static IEnumerable<string[]> ParseFile(string content)
		{
			List<string> fields = new List<string>();
			StringBuilder current = new StringBuilder();
			bool inQuotes = false;
			for (int i = 0; i < content.Length; i++)
			{
				char c = content[i];
				if (c == '"')
				{
					if (inQuotes && i + 1 < content.Length && content[i + 1] == '"')
					{
						current.Append('"');
						i++;
					}
					else
					{
						inQuotes = !inQuotes;
					}
					continue;
				}
				if (c == '\r' && i + 1 < content.Length && content[i + 1] == '\n')
				{
					continue;
				}
				if (inQuotes)
				{
					current.Append((c == '\r') ? '\n' : c);
					continue;
				}
				if (c == ',')
				{
					fields.Add(current.ToString());
					current.Length = 0;
					continue;
				}
				if (c == '\n' || c == '\r')
				{
					fields.Add(current.ToString());
					current.Length = 0;
					if (fields.Count > 1 || fields[0].Length > 0)
					{
						yield return fields.ToArray();
					}
					fields.Clear();
					continue;
				}
				current.Append(c);
			}
			fields.Add(current.ToString());
			if (fields.Count > 1 || fields[0].Length > 0)
			{
				yield return fields.ToArray();
			}
		}

		/// <summary>
		/// Every row of one table file as (key, value), in file order. The file is read like File.ReadAllText (UTF-8, a byte order
		/// mark honoured) but shared, so a file another process holds open still reads.
		/// </summary>
		public static IEnumerable<KeyValuePair<string, string>> Read(string path)
		{
			string text;
			using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
			using (StreamReader sr = new StreamReader(fs, new UTF8Encoding(false), true))
			{
				text = sr.ReadToEnd();
			}
			return Rows(text);
		}

		/// <summary>StringTableInjectionPatches.LoadTranslations' rows of one file's text (see Read).</summary>
		public static IEnumerable<KeyValuePair<string, string>> Rows(string text)
		{
			foreach (string[] f in ParseFile(text))
			{
				if (f.Length < 2 || f[0].Length == 0)
				{
					continue;
				}
				yield return new KeyValuePair<string, string>(f[0], f[1].Replace("\\n", "\n"));
			}
		}
	}
}
