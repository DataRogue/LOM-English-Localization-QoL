using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Bootstrap;

namespace LOM_UI_EN
{
	/// <summary>
	/// The chainloader calls this mod makes. Since 1.2.0 it runs on BepInEx 5.4.23.3 only, the BepInEx Lash's English Patch installs
	/// (1.1.x also built a BepInEx 6 be.692 DLL; the 1.0.x and 1.1.x branches keep that build).
	/// </summary>
	internal static class Loader
	{
		private static readonly List<Action> _pending = new List<Action>();
		private static bool _allLoaded;

		/// <summary>The chainloader's plugins by GUID (null before it exists).</summary>
		public static IDictionary<string, PluginInfo> Plugins
		{
			get
			{
				return Chainloader.PluginInfos;
			}
		}

		/// <summary>
		/// Runs the action once every plugin has loaded. BepInEx 5 has no event for that, so Plugin.Start runs it (Unity calls Start
		/// after the Awake of every plugin the chainloader added in the same pass).
		/// </summary>
		public static void WhenAllLoaded(Action action)
		{
			if (_allLoaded)
			{
				action();
				return;
			}
			_pending.Add(action);
		}

		/// <summary>Plugin.Start: the chainloader is done.</summary>
		public static void AllLoaded()
		{
			if (_allLoaded)
			{
				return;
			}
			_allLoaded = true;
			foreach (Action a in _pending.ToArray())
			{
				try
				{
					a();
				}
				catch (Exception ex)
				{
					Plugin.Log?.LogError("after-load step failed: " + ex);
				}
			}
			_pending.Clear();
		}
	}
}

namespace BepInEx.Core.Logging.Interpolation
{
	using System.Text;

	/// <summary>
	/// BepInEx 6's interpolated log handlers, which the decompiled source names outright. BepInEx 5's ManualLogSource.LogInfo
	/// (object) logs ToString(), so the same calls log the same text.
	/// </summary>
	internal class BepInExLogInterpolatedStringHandler
	{
		private readonly StringBuilder _sb;

		public BepInExLogInterpolatedStringHandler(int literalLength, int formattedCount, out bool isEnabled)
		{
			_sb = new StringBuilder(literalLength + formattedCount * 8);
			isEnabled = true;
		}

		public void AppendLiteral(string s)
		{
			_sb.Append(s);
		}

		public void AppendFormatted<T>(T t)
		{
			_sb.Append(t);
		}

		public void AppendFormatted<T>(T t, string format)
		{
			_sb.Append((t is IFormattable f) ? f.ToString(format, null) : t?.ToString());
		}

		public override string ToString()
		{
			return _sb.ToString();
		}
	}

	internal sealed class BepInExInfoLogInterpolatedStringHandler : BepInExLogInterpolatedStringHandler
	{
		public BepInExInfoLogInterpolatedStringHandler(int literalLength, int formattedCount, out bool isEnabled) : base(literalLength, formattedCount, out isEnabled)
		{
		}
	}

	internal sealed class BepInExWarningLogInterpolatedStringHandler : BepInExLogInterpolatedStringHandler
	{
		public BepInExWarningLogInterpolatedStringHandler(int literalLength, int formattedCount, out bool isEnabled) : base(literalLength, formattedCount, out isEnabled)
		{
		}
	}

	internal sealed class BepInExErrorLogInterpolatedStringHandler : BepInExLogInterpolatedStringHandler
	{
		public BepInExErrorLogInterpolatedStringHandler(int literalLength, int formattedCount, out bool isEnabled) : base(literalLength, formattedCount, out isEnabled)
		{
		}
	}

	internal sealed class BepInExDebugLogInterpolatedStringHandler : BepInExLogInterpolatedStringHandler
	{
		public BepInExDebugLogInterpolatedStringHandler(int literalLength, int formattedCount, out bool isEnabled) : base(literalLength, formattedCount, out isEnabled)
		{
		}
	}

	internal sealed class BepInExMessageLogInterpolatedStringHandler : BepInExLogInterpolatedStringHandler
	{
		public BepInExMessageLogInterpolatedStringHandler(int literalLength, int formattedCount, out bool isEnabled) : base(literalLength, formattedCount, out isEnabled)
		{
		}
	}
}
