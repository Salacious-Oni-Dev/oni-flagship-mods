using UnityEngine;

namespace Mod1ThermoFluid
{
	/// <summary>
	/// Mod 1's log levels, and the reason it does not use Unity's directly.
	///
	/// <b>ONI TREATS <c>Debug.LogError</c> AS A CRASH.</b> This is not a style preference, it is
	/// what the game's own code does with the call. From <c>KCrashReporter.HandleLog</c>:
	/// <code>
	///     if (!(errorScreen == null) || (type != LogType.Exception &amp;&amp; type != 0) || ...)
	///             return;                     // LogType.Error IS 0, so an error does NOT return
	///     ...
	///     SpeedControlScreen.Instance.Pause(playSound: true, isCrashed: true);
	///     ShowDialog(text2, text3);
	/// </code>
	/// and from <c>ShowDialog</c>, when any crashable mod is loaded, the dialog is built with a
	/// <c>null</c> continue action unless the mod manager is in dev mode -- so on a player's
	/// install there is no Continue button, only quit, and every mod found in the live stack is
	/// unchecked on the way out.
	///
	/// <b>WHY THIS FILE EXISTS SEPARATELY FROM <c>OniFramework.FrameworkLog</c>.</b> The rule is
	/// the framework's and the reasoning is identical, but the PREFIX is not interchangeable.
	/// FrameworkLog stamps every line <c>[OniFramework]</c>, and the whole point of the rule is
	/// that a message must name the component actually responsible: routing Mod 1's messages
	/// through it would put the framework's name on a flagship mod's failure in the player's
	/// log, which is the misattribution the rule exists to prevent. So the policy is shared and
	/// the identity is not.
	///
	/// <b>SEVERITY TRAVELS IN THE TEXT, NOT IN THE LOG LEVEL.</b> <see cref="Error"/> writes at
	/// <c>Debug.LogWarning</c> and puts the word ERROR in the line, where a human reading the log
	/// and a tool grepping it both still see it. <c>build.sh</c> fails the build on a
	/// <c>Debug.LogError</c> anywhere in these sources, so the rule cannot be lost to the next
	/// file somebody adds.
	///
	/// <b>WHAT TO DO WHEN A CRASH REALLY IS A CRASH.</b> Throw. An exception reaches
	/// <c>KCrashReporter</c> as <c>LogType.Exception</c>, and that path builds its stack from
	/// <c>DebugUtil.RetrieveLastExceptionLogged()</c> -- the exception's OWN stack, so the mod
	/// actually at fault is the one named and disabled. A <c>Debug.LogError</c> from inside a
	/// <c>catch</c> does the opposite: by then the stack is ours, so we take the blame for a bug
	/// we contained.
	///
	/// <b>A RIG'S VERDICT IS NOT A LOG LEVEL EITHER.</b> Where a probe has an assertion sink,
	/// record the failure there and let the rig fail; <see cref="Error"/> is for the paths that
	/// have no rig instance to record against -- a static helper, a Harmony patch, a component's
	/// own audit.
	/// </summary>
	internal static class Mod1Log
	{
		/// <summary>Every line this class writes starts with it, so one grep finds the lot.</summary>
		public const string Prefix = "[Mod1ThermoFluid] ";

		/// <summary>
		/// A condition that IS an error -- a build that returned null, an audit that says matter
		/// was not conserved, a setting the mod cannot seed -- written at warning level for the
		/// reasons in the class summary.
		///
		/// Pass the message WITHOUT a "[Mod1ThermoFluid] " prefix of its own; this adds it.
		/// </summary>
		public static void Error(string message)
		{
			Debug.LogWarning(Prefix + "ERROR " + message);
		}

		/// <summary>
		/// A condition worth a look that is not an error: a fallback was taken, a value was
		/// clamped, something was skipped. Same level as <see cref="Error"/> and deliberately so
		/// -- the level carries no information here, the text does.
		/// </summary>
		public static void Warn(string message)
		{
			Debug.LogWarning(Prefix + message);
		}

		/// <summary>Ordinary progress. Plain <c>Debug.Log</c>, prefixed.</summary>
		public static void Info(string message)
		{
			Debug.Log(Prefix + message);
		}
	}
}
