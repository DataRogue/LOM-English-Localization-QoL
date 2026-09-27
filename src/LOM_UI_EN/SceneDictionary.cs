using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace LOM_UI_EN
{
	/// <summary>
	/// Chinese source text -> English for text drawn into the game's screens (labels, buttons, captions, dice, barks), with the
	/// file format and lookup rules of XUnity AutoTranslator 5.3.1 as this game's English patch configures it
	/// (AutoTranslatorConfig.ini: no substitutions, TemplateAllNumberAway=False, HandleRichText=True, zh-TW -> en), so a line
	/// written for XUnity renders the same here. Ported from the decompiled XUnity core:
	///   file lines      TextHelper.ReadTranslationLineAndDecode        (escapes \n \r \= \\ \uXXXX %3D, '//' ends the line,
	///                                                                   a second unescaped '=' rejects it); later lines win
	///   load variants   TextTranslationCache.LoadTranslationFiles      (externally trimmed and fully trimmed copies of each key,
	///                                                                   only where that key is still free)
	///   key trimming    UntranslatedText / PerformInternalTrimming     (whitespace blocks around line breaks; Chinese keys have
	///                                                                   no whitespace between words, English values do)
	///   lookup          TextTranslationCache.TryGetTranslation         (exact, externally trimmed + whitespace put back,
	///                                                                   internally trimmed, fully trimmed + whitespace, regex)
	///   known values    TextTranslationCache.IsTranslatable            (text that is a value and not a key is left alone)
	///   rich text       RichTextParser + ...ByParserResult             (after a miss: translate the text between tags)
	/// Machine translation, number templating, splitters and scopes are not ported: this configuration never uses them.
	/// No UnityEngine types here, so the offline parity runner compiles this file on its own.
	/// </summary>
	public sealed class SceneDictionary
	{
		private readonly Dictionary<string, string> _map = new Dictionary<string, string>(65536, StringComparer.Ordinal);
		private readonly HashSet<string> _values = new HashSet<string>(StringComparer.Ordinal);
		private readonly List<KeyValuePair<Regex, string>> _regexes = new List<KeyValuePair<Regex, string>>();
		private readonly HashSet<string> _nonCjkKeys = new HashSet<string>(StringComparer.Ordinal);
		private readonly HashSet<string> _removed = new HashSet<string>(StringComparer.Ordinal);

		public int Lines { get; private set; }
		public int Entries { get; private set; }
		public int Rejected { get; private set; }
		public int Empty { get; private set; }
		public int Duplicates { get; private set; }
		public int VariantKeys { get; private set; }
		public int RegexCount => _regexes.Count;
		public int KeyCount => _map.Count;
		public int MaxKeyLength { get; private set; }
		public int MaxNonCjkKeyLength { get; private set; }
		/// <summary>The longest key with no Chinese character (U+4E00-9FAF, 3400-4DBF, F900-FAFF): punctuation-only and junk keys.</summary>
		public int MaxNonHanKeyLength { get; private set; }
		public readonly List<string> Files = new List<string>();
		public readonly List<string> Warnings = new List<string>();
		public string Label;
		/// <summary>Overlay loads: lines of the base file this mod's lines replaced, lines it removed, and this mod's lines it lacks.</summary>
		public int Substituted { get; private set; }
		public int Removed { get; private set; }
		public int Added { get; private set; }
		/// <summary>Overlay loads: this mod's own lines read (revisions, additions and removals).</summary>
		public int OverlayEntries { get; private set; }
		/// <summary>Overlay loads: the base file read (null when it was not found: only this mod's lines are in).</summary>
		public string BaseFile { get; private set; }
		/// <summary>Overlay loads: the base file's own values of the keys asked for in LoadOverlay (for OriginalMod's data check).</summary>
		public readonly Dictionary<string, string> BaseSample = new Dictionary<string, string>(StringComparer.Ordinal);

		/// <summary>Loads the files in the given order (later lines win, as in XUnity), then adds the trimmed key variants.</summary>
		public static SceneDictionary Load(IEnumerable<string> files)
		{
			SceneDictionary d = new SceneDictionary();
			foreach (string f in files)
			{
				if (File.Exists(f))
				{
					d.LoadFile(f);
				}
			}
			d.Finish();
			return d;
		}

		/// <summary>
		/// The original patch's XUnity file with this mod's lines laid over it: every base line whose key this mod has takes this
		/// mod's value in its place (a line of this mod with an empty value removes the base line), then this mod's lines the base
		/// lacks follow in file order. The result is what one file holding the base text edited in place gives, later lines winning
		/// as in XUnity, and the base English a line of this mod replaced is not left behind as a "known translation". Without the
		/// base file, only this mod's lines are in.
		/// </summary>
		public static SceneDictionary LoadOverlay(string baseFile, IEnumerable<string> overlayFiles, IEnumerable<string> sampleKeys = null)
		{
			SceneDictionary d = new SceneDictionary();
			List<KeyValuePair<string, string>> over = new List<KeyValuePair<string, string>>();
			foreach (string f in overlayFiles)
			{
				if (File.Exists(f))
				{
					d.Files.Add(f);
					d.ReadEntries(f, over);
				}
			}
			d.OverlayEntries = over.Count;
			Dictionary<string, string> sub = new Dictionary<string, string>(over.Count, StringComparer.Ordinal);
			foreach (KeyValuePair<string, string> kv in over)
			{
				sub[kv.Key] = kv.Value;
			}
			HashSet<string> sample = (sampleKeys != null) ? new HashSet<string>(sampleKeys, StringComparer.Ordinal) : null;
			HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
			if (!string.IsNullOrEmpty(baseFile) && File.Exists(baseFile))
			{
				d.BaseFile = baseFile;
				d.Files.Insert(0, baseFile);
				List<KeyValuePair<string, string>> baseEntries = new List<KeyValuePair<string, string>>(70000);
				d.ReadEntries(baseFile, baseEntries);
				foreach (KeyValuePair<string, string> kv in baseEntries)
				{
					if (sample != null && sample.Contains(kv.Key))
					{
						d.BaseSample[kv.Key] = kv.Value;
					}
					string mine;
					if (kv.Key != null && sub.TryGetValue(kv.Key, out mine))
					{
						seen.Add(kv.Key);
						if (string.IsNullOrEmpty(mine))
						{
							d.Removed++;
							d._removed.Add(kv.Key);
							continue;
						}
						d.Substituted++;
						d.Ingest(kv.Key, mine, baseFile);
					}
					else
					{
						d.Ingest(kv.Key, kv.Value, baseFile);
					}
				}
			}
			foreach (KeyValuePair<string, string> kv in over)
			{
				if (kv.Key != null && seen.Contains(kv.Key))
				{
					continue;
				}
				if (!string.IsNullOrEmpty(kv.Key) && !string.IsNullOrEmpty(kv.Value))
				{
					d.Added++;
				}
				d.Ingest(kv.Key, kv.Value, "overlay");
			}
			d.Finish();
			return d;
		}

		public void LoadFile(string path)
		{
			Files.Add(path);
			List<KeyValuePair<string, string>> entries = new List<KeyValuePair<string, string>>();
			ReadEntries(path, entries);
			foreach (KeyValuePair<string, string> kv in entries)
			{
				Ingest(kv.Key, kv.Value, path);
			}
		}

		/// <summary>The decoded key/value pairs of one file, in order (directive lines skipped, undecodable lines counted as rejected).</summary>
		private void ReadEntries(string path, List<KeyValuePair<string, string>> into)
		{
			string all = ReadShared(path);
			foreach (string line in all.Split(new char[2] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
			{
				Lines++;
				if (line.StartsWith("#set ", StringComparison.Ordinal) || line.StartsWith("#unset ", StringComparison.Ordinal) || line.StartsWith("#enable ", StringComparison.Ordinal))
				{
					continue;
				}
				string[] kv;
				try
				{
					kv = ReadTranslationLineAndDecode(line);
				}
				catch (Exception ex)
				{
					Rejected++;
					if (Warnings.Count < 20)
					{
						Warnings.Add(Path.GetFileName(path) + ": " + ex.Message);
					}
					continue;
				}
				if (kv == null)
				{
					Rejected++;
					continue;
				}
				into.Add(new KeyValuePair<string, string>(kv[0], kv[1]));
			}
		}

		/// <summary>
		/// A whole text file as UTF-8 (a BOM skipped, as XUnity's reader does), opened so that a writer holding it (XUnity appends
		/// to its translation file while the game runs) does not make the read fail; retried once after a moment if it does.
		/// </summary>
		public static string ReadShared(string path)
		{
			for (int attempt = 0; ; attempt++)
			{
				try
				{
					using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
					using (StreamReader r = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
					{
						return r.ReadToEnd();
					}
				}
				catch (IOException) when (attempt == 0)
				{
					System.Threading.Thread.Sleep(200);
				}
			}
		}

		/// <summary>Overlay loads: a base line this mod removed (junk the original patch has); nothing may bring it back.</summary>
		public bool IsRemoved(string key)
		{
			return key != null && _removed.Contains(key);
		}

		/// <summary>One decoded line, as XUnity's LoadTranslationFiles takes it.</summary>
		private void Ingest(string key, string value, string path)
		{
			if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value))
			{
				Empty++;
				return;
			}
			if (key.StartsWith("sr:", StringComparison.Ordinal))
			{
				return;
			}
			if (key.StartsWith("r:", StringComparison.Ordinal))
			{
				AddRegex(key, value, path);
				return;
			}
			if (_map.ContainsKey(key))
			{
				Duplicates++;
			}
			Add(key, value);
			Entries++;
		}

		private void AddRegex(string key, string value, string path)
		{
			// RegexTranslation: r:"pattern", anchored with ^ and $.
			string pattern = key.Substring(2);
			if (pattern.Length >= 2 && pattern[0] == '"' && pattern[pattern.Length - 1] == '"')
			{
				pattern = pattern.Substring(1, pattern.Length - 2);
			}
			if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
			{
				value = value.Substring(1, value.Length - 2);
			}
			if (!pattern.StartsWith("^", StringComparison.Ordinal))
			{
				pattern = "^" + pattern;
			}
			if (!pattern.EndsWith("$", StringComparison.Ordinal))
			{
				pattern += "$";
			}
			try
			{
				_regexes.Add(new KeyValuePair<Regex, string>(new Regex(pattern, RegexOptions.CultureInvariant), value));
			}
			catch (Exception ex)
			{
				if (Warnings.Count < 20)
				{
					Warnings.Add(Path.GetFileName(path) + ": bad regex " + pattern + ": " + ex.Message);
				}
			}
		}

		private void Add(string key, string value)
		{
			_map[key] = value;
			_values.Add(value);
		}

		/// <summary>TextTranslationCache.LoadTranslationFiles, after all files: externally and fully trimmed key variants, in insertion order, only where the variant key is still free.</summary>
		private void Finish()
		{
			List<KeyValuePair<string, string>> snapshot = new List<KeyValuePair<string, string>>(_map);
			foreach (KeyValuePair<string, string> kv in snapshot)
			{
				Key k = new Key(kv.Key, removeInternalWhitespace: true, whitespaceBetweenWords: false);
				Key v = new Key(kv.Value, removeInternalWhitespace: true, whitespaceBetweenWords: true);
				if (k.ExternallyTrimmed != kv.Key && !_map.ContainsKey(k.ExternallyTrimmed))
				{
					Add(k.ExternallyTrimmed, v.ExternallyTrimmed);
					VariantKeys++;
				}
				if (k.ExternallyTrimmed != k.FullyTrimmed && !_map.ContainsKey(k.FullyTrimmed))
				{
					Add(k.FullyTrimmed, v.FullyTrimmed);
					VariantKeys++;
				}
			}
			foreach (string key in _map.Keys)
			{
				if (key.Length > MaxKeyLength)
				{
					MaxKeyLength = key.Length;
				}
				if (key.Length > MaxNonHanKeyLength && !IsTranslatableChinese(key))
				{
					MaxNonHanKeyLength = key.Length;
				}
				if (!ContainsCjk(key))
				{
					_nonCjkKeys.Add(key);
					if (key.Length > MaxNonCjkKeyLength)
					{
						MaxNonCjkKeyLength = key.Length;
					}
				}
			}
		}

		/// <summary>Every key and value after loading (variants included), for the harness's scenedump.</summary>
		public IEnumerable<KeyValuePair<string, string>> Pairs()
		{
			return _map;
		}

		public bool ContainsKey(string key)
		{
			return key != null && _map.ContainsKey(key);
		}

		/// <summary>TextTranslationCache.IsTranslation: the text is a known translation and not itself a source line.</summary>
		public bool IsKnownValue(string text)
		{
			return !_map.ContainsKey(text) && _values.Contains(text);
		}

		/// <summary>Cheap pre-check for text without CJK: could any key (or its trimmed form) be this text?</summary>
		public bool MayMatchNonCjk(string text)
		{
			if (_nonCjkKeys.Count == 0 || text.Length > MaxNonCjkKeyLength + 16)
			{
				return false;
			}
			if (_nonCjkKeys.Contains(text))
			{
				return true;
			}
			// The trimmed forms differ from the text only when it has whitespace; most short English text has none worth a Key.
			bool whitespace = false;
			for (int i = 0; i < text.Length && !whitespace; i++)
			{
				whitespace = char.IsWhiteSpace(text[i]);
			}
			if (!whitespace)
			{
				return false;
			}
			Key k = new Key(text, removeInternalWhitespace: true, whitespaceBetweenWords: false);
			return _nonCjkKeys.Contains(k.ExternallyTrimmed) || _nonCjkKeys.Contains(k.InternallyTrimmed) || _nonCjkKeys.Contains(k.FullyTrimmed);
		}

		/// <summary>TextTranslationCache.TryGetTranslation, untemplated, global scope.</summary>
		public bool TryGet(string text, bool removeInternalWhitespace, bool allowRegex, out string value)
		{
			if (_map.TryGetValue(text, out value))
			{
				return true;
			}
			Key key = new Key(text, removeInternalWhitespace, whitespaceBetweenWords: false);
			string v;
			if ((object)text != key.ExternallyTrimmed && _map.TryGetValue(key.ExternallyTrimmed, out v))
			{
				value = key.Leading + v + key.Trailing;
				return true;
			}
			if ((object)text != key.InternallyTrimmed && _map.TryGetValue(key.InternallyTrimmed, out v))
			{
				value = v;
				return true;
			}
			if ((object)key.InternallyTrimmed != key.FullyTrimmed && _map.TryGetValue(key.FullyTrimmed, out v))
			{
				value = key.Leading + v + key.Trailing;
				return true;
			}
			if (allowRegex)
			{
				for (int i = 0; i < _regexes.Count; i++)
				{
					Match m = _regexes[i].Key.Match(text);
					if (m.Success)
					{
						value = m.Result(_regexes[i].Value);
						return true;
					}
				}
			}
			value = null;
			return false;
		}

		/// <summary>
		/// What XUnity would put into a text component for this text (AutoTranslationPlugin.TranslateOrQueueWebJobImmediate):
		/// nothing for blank text or a known translation, else the dictionary lookup, else (rich-text components only) the
		/// rich-text parser. Returns false when XUnity would have left the text alone or queued it for machine translation.
		/// </summary>
		public bool Translate(string text, bool supportsRichText, out string result)
		{
			result = null;
			if (IsNullOrWhiteSpace(text) || IsKnownValue(text))
			{
				return false;
			}
			if (TryGet(text, removeInternalWhitespace: true, allowRegex: true, out result))
			{
				return true;
			}
			if (supportsRichText && text.IndexOf('<') >= 0 && IsTranslatableChinese(text))
			{
				return TryRichText(text, out result);
			}
			result = null;
			return false;
		}

		private static readonly Regex TagRegex = new Regex("(<.*?>)", RegexOptions.CultureInvariant);
		private static readonly char[] TagNameEnders = new char[2] { '=', ' ' };
		private static readonly HashSet<string> IgnoreTags = new HashSet<string> { "ruby", "group" };
		private static readonly HashSet<string> KnownTags = new HashSet<string>
		{
			"b", "i", "size", "color", "em", "sup", "sub", "dash", "space", "u",
			"strike", "param", "format", "emoji", "speed", "sound", "line-height"
		};

		/// <summary>RichTextParser.Parse + TranslateOrQueueWebJobImmediateByParserResult: split on tags, translate every fragment; any Chinese fragment without a line fails the whole text.</summary>
		private bool TryRichText(string input, out string result)
		{
			result = null;
			List<string> template = new List<string>();      // kept tags, or null placeholders for fragments
			List<StringBuilder> fragments = new List<StringBuilder>();
			List<int> fragmentSlot = new List<int>();
			bool inFragment = false;
			int templateLength = 0;
			foreach (string part in TagRegex.Split(input))
			{
				if (part.Length == 0)
				{
					continue;
				}
				bool isTag = part[0] == '<' && part[part.Length - 1] == '>';
				if (isTag)
				{
					bool closing = part.Length > 1 && part[1] == '/';
					string inner = closing ? part.Substring(2, part.Length - 3) : part.Substring(1, part.Length - 2);
					string[] bits = inner.Split(TagNameEnders);
					string name = (bits.Length < 2) ? inner : bits[0];
					if (KnownTags.Contains(name) || (IsAllLatin(name) && !IgnoreTags.Contains(name)) || (name.Length > 0 && name[0] == '#'))
					{
						template.Add(part);
						templateLength += part.Length;
						inFragment = false;
					}
					// Unknown tags are dropped and the text around them joins one fragment, as in XUnity.
					continue;
				}
				if (inFragment)
				{
					fragments[fragments.Count - 1].Append(part);
				}
				else
				{
					fragments.Add(new StringBuilder(part));
					fragmentSlot.Add(template.Count);
					template.Add(null);
					templateLength += 5; // "[[A]]"
				}
				inFragment = true;
			}
			if (fragments.Count == 0 || templateLength <= 5)
			{
				return false;
			}
			string[] translated = new string[fragments.Count];
			for (int i = 0; i < fragments.Count; i++)
			{
				string frag = fragments[i].ToString();
				string v;
				if (IsNullOrWhiteSpace(frag) || IsKnownValue(frag))
				{
					translated[i] = frag;
				}
				else if (TryGet(frag, removeInternalWhitespace: false, allowRegex: false, out v))
				{
					translated[i] = v;
				}
				else if (!IsTranslatableChinese(frag))
				{
					translated[i] = frag;
				}
				else if (TryGet(frag, removeInternalWhitespace: true, allowRegex: true, out v))
				{
					translated[i] = v;
				}
				else
				{
					return false;
				}
			}
			StringBuilder sb = new StringBuilder(input.Length + 16);
			int f = 0;
			for (int i = 0; i < template.Count; i++)
			{
				if (template[i] == null)
				{
					sb.Append(translated[f++]);
				}
				else
				{
					sb.Append(template[i]);
				}
			}
			result = sb.ToString();
			return true;
		}

		private static bool IsAllLatin(string value)
		{
			for (int i = 0; i < value.Length; i++)
			{
				char c = value[i];
				if ((c < 'A' || c > 'Z') && (c < 'a' || c > 'z') && c != '-' && c != '_')
				{
					return false;
				}
			}
			return true;
		}

		public static bool IsNullOrWhiteSpace(string s)
		{
			if (s == null)
			{
				return true;
			}
			for (int i = 0; i < s.Length; i++)
			{
				if (!char.IsWhiteSpace(s[i]))
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>Any CJK ideograph, kana, Hangul or CJK / full-width punctuation (U+2E80 and up, below the surrogates, plus the full-width forms).</summary>
		public static bool ContainsCjk(string s)
		{
			for (int i = 0; i < s.Length; i++)
			{
				char c = s[i];
				if (c >= 0x2E80 && (c < 0xD800 || (c >= 0xF900 && c <= 0xFFEF)))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>LanguageHelper.ContainsChineseSymbols: what XUnity counts as translatable zh text (and not starting with U+180E, IgnoreTextStartingWith).</summary>
		public static bool IsTranslatableChinese(string s)
		{
			if (s.Length > 0 && s[0] == (char)0x180E)
			{
				return false;
			}
			for (int i = 0; i < s.Length; i++)
			{
				char c = s[i];
				if ((c >= 0x4E00 && c <= 0x9FAF) || (c >= 0x3400 && c <= 0x4DBF) || (c >= 0xF900 && c <= 0xFAFF))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>TextHelper.ReadTranslationLineAndDecode.</summary>
		public static string[] ReadTranslationLineAndDecode(string str)
		{
			if (string.IsNullOrEmpty(str))
			{
				return null;
			}
			string[] array = new string[2];
			int num = 0;
			bool escaped = false;
			int length = str.Length;
			StringBuilder sb = new StringBuilder((int)(length / 1.3));
			for (int i = 0; i < length; i++)
			{
				char c = str[i];
				if (escaped)
				{
					switch (c)
					{
					case '=':
					case '\\':
						sb.Append(c);
						break;
					case 'n':
						sb.Append('\n');
						break;
					case 'r':
						sb.Append('\r');
						break;
					case 'u':
						if (i + 4 < length)
						{
							sb.Append((char)int.Parse(str.Substring(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
							i += 4;
							break;
						}
						throw new FormatException("invalid \\u escape in line: " + str);
					default:
						sb.Append('\\');
						sb.Append(c);
						break;
					}
					escaped = false;
					continue;
				}
				switch (c)
				{
				case '\\':
					escaped = true;
					break;
				case '=':
					if (num > 1)
					{
						return null;
					}
					array[num++] = sb.ToString();
					sb.Length = 0;
					break;
				case '%':
					if (i + 2 < length && str[i + 1] == '3' && str[i + 2] == 'D')
					{
						sb.Append('=');
						i += 2;
					}
					else
					{
						sb.Append(c);
					}
					break;
				case '/':
					if (i + 1 < length && str[i + 1] == '/')
					{
						array[num++] = sb.ToString();
						return (num == 2) ? array : null;
					}
					sb.Append(c);
					break;
				default:
					sb.Append(c);
					break;
				}
			}
			if (num != 1)
			{
				return null;
			}
			array[1] = sb.ToString();
			return array;
		}

		/// <summary>The whitespace-normalised forms of a text (UntranslatedText, untemplated). Reference identity matters: an unchanged form is the same string instance.</summary>
		public struct Key
		{
			public string Leading;
			public string Trailing;
			public string ExternallyTrimmed;
			public string InternallyTrimmed;
			public string FullyTrimmed;

			public Key(string text, bool removeInternalWhitespace, bool whitespaceBetweenWords)
			{
				Leading = null;
				Trailing = null;
				int i = 0;
				while (i < text.Length && char.IsWhiteSpace(text[i]))
				{
					i++;
				}
				if (i != 0)
				{
					Leading = text.Substring(0, i);
				}
				if (i != text.Length)
				{
					int j = text.Length - 1;
					while (j > -1 && char.IsWhiteSpace(text[j]))
					{
						j--;
					}
					if (j != text.Length - 1)
					{
						Trailing = text.Substring(j + 1);
					}
				}
				int lead = (Leading != null) ? Leading.Length : 0;
				int trail = (Trailing != null) ? Trailing.Length : 0;
				ExternallyTrimmed = (lead > 0 || trail > 0) ? text.Substring(lead, text.Length - trail - lead) : text;
				if (removeInternalWhitespace)
				{
					FullyTrimmed = PerformInternalTrimming(ExternallyTrimmed, whitespaceBetweenWords);
					InternallyTrimmed = ((object)FullyTrimmed == ExternallyTrimmed) ? text : SurroundWithWhitespace(FullyTrimmed, Leading, Trailing);
				}
				else
				{
					FullyTrimmed = ExternallyTrimmed;
					InternallyTrimmed = text;
				}
			}

			private static string SurroundWithWhitespace(string text, string leading, string trailing)
			{
				if (leading == null && trailing == null)
				{
					return text;
				}
				return (leading ?? "") + text + (trailing ?? "");
			}
		}

		/// <summary>UntranslatedText.PerformInternalTrimming, verbatim in behaviour: returns the same instance when nothing changed.</summary>
		public static string PerformInternalTrimming(string text, bool whitespaceBetweenWords)
		{
			// Only whitespace around a line break is ever changed, so text without one comes back as is (and allocates nothing).
			if (text.IndexOf((char)10) < 0)
			{
				return text;
			}
			StringBuilder builder = new StringBuilder(64);
			bool changed = false;
			int length = text.Length;
			int pendingWs = -1;
			int i;
			for (i = 0; i < length; i++)
			{
				char c = text[i];
				if (c == '\n')
				{
					int start = i - 1;
					while (start >= 0 && char.IsWhiteSpace(text[start]))
					{
						start--;
					}
					int end;
					for (end = i + 1; end < length && char.IsWhiteSpace(text[end]); end++)
					{
					}
					start++;
					end--;
					int span = end - start;
					char last = '\0';
					if (span > 0)
					{
						int kept = 0;
						char prev = text[start];
						bool inRun = false;
						start++;
						for (int k = start; k <= end; k++)
						{
							char c4 = text[k];
							if (c4 == prev)
							{
								if (!inRun)
								{
									kept++;
									builder.Append(prev);
									inRun = true;
								}
								kept++;
								builder.Append(c4);
								last = c4;
							}
							else if (prev == '\r' && c4 == '\n')
							{
								if (k + 2 > end)
								{
									continue;
								}
								if (text[k + 1] == '\r' && text[k + 2] == '\n')
								{
									if (!inRun)
									{
										kept++;
										builder.Append('\r');
										builder.Append('\n');
										inRun = true;
									}
									kept++;
									builder.Append('\r');
									builder.Append('\n');
									last = '\n';
									k++;
								}
							}
							else
							{
								inRun = false;
								prev = c4;
							}
						}
						if (kept - 1 != span)
						{
							changed = true;
						}
					}
					else
					{
						changed = true;
					}
					if (whitespaceBetweenWords && !char.IsWhiteSpace(last) && builder.Length > 0 && builder[builder.Length - 1] != ' ')
					{
						changed = true;
						builder.Append(' ');
					}
					i = end;
					pendingWs = -1;
				}
				else if (!char.IsWhiteSpace(c))
				{
					if (pendingWs != -1)
					{
						for (int l = pendingWs; l < i; l++)
						{
							builder.Append(text[l]);
						}
						pendingWs = -1;
					}
					builder.Append(c);
				}
				else if (pendingWs == -1)
				{
					pendingWs = i;
				}
			}
			if (pendingWs != -1)
			{
				for (int m = pendingWs; m < i; m++)
				{
					builder.Append(text[m]);
				}
			}
			return changed ? builder.ToString() : text;
		}
	}
}
