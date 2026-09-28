using System;
using System.Collections.Generic;
using BepInEx;
#if BIE5
using BepInEx.Bootstrap;
#else
using BepInEx.Unity.Mono.Bootstrap;
#endif

namespace LOM_UI_EN
{
	/// <summary>
	/// The few BepInEx calls that differ between BepInEx 6 (be.692, what the English patch shipped up to 2026.02 and this mod's
	/// full package bundles) and BepInEx 5 (the loader of the patch's own plugin since its llmkit-upgrade). The same source builds
	/// for either: build.ps1 -BepInEx5 defines BIE5 and writes LOM_UI_EN.BepInEx5.dll. Each BepInEx loads only the DLL built for it
	/// (6 wants a reference to BepInEx.Core, 5 one to BepInEx), so both can sit in the plugin folder.
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
#if BIE5
				return Chainloader.PluginInfos;
#else
				return UnityChainloader.Instance?.Plugins;
#endif
			}
		}

		/// <summary>
		/// Runs the action once every plugin has loaded: on BepInEx 6 at the chainloader's Finished event; BepInEx 5 has none, so
		/// Plugin.Start runs it (Unity calls Start after the Awake of every plugin the chainloader added in the same pass).
		/// </summary>
		public static void WhenAllLoaded(Action action)
		{
#if BIE5
			if (_allLoaded)
			{
				action();
				return;
			}
			_pending.Add(action);
#else
			UnityChainloader.Instance.Finished += action;
#endif
		}

		/// <summary>Plugin.Start: the chainloader is done (BepInEx 5; on BepInEx 6 the Finished event already ran the actions).</summary>
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

#if BIE5
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
#endif
