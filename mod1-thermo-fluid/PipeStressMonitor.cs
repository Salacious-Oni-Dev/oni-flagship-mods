using System.Collections.Generic;
using OniFramework;
using UnityEngine;

namespace Mod1ThermoFluid
{
    /// <summary>
    /// Live payload behind one pipe-stress status item. Mutated in place each scan rather than
    /// re-added, because <c>StatusItemGroup.Entry</c> keeps the reference it was handed and
    /// re-resolves the name/tooltip every time the UI draws -- so updating the numbers costs
    /// nothing and never makes the notification list flicker an entry out and back in.
    /// </summary>
    internal sealed class PipeStressData
    {
        internal string Summary = string.Empty;
        internal string Detail = string.Empty;
    }

    /// <summary>
    /// Stationeers' pipe-stress warnings, delivered through ONI's own notification system.
    ///
    /// WHY THIS EXISTS AT ALL, and why it is not a port: Stationeers signals a stressed pipe with
    /// POSITIONAL AUDIO -- <c>Pipe.Stressed</c>'s setter calls
    /// <c>AtmosphericAudioHandler.AddStressedPipe(this)</c> and the player walks around a 3D
    /// world following the noise to the offending segment. ONI is 2D, has no positional audio
    /// worth navigating by, and already has a far better idiom for "something in your colony
    /// needs attention right now": a status item that also raises a notification, which the
    /// player clicks to snap the camera onto the exact tile (<c>Notifier.AutoClickFocus</c> sets
    /// <c>clickFocus</c> to the object's own transform). That is the same mechanism vanilla uses
    /// for a suffocating duplicant and an entombed building, so it needs no explanation to an
    /// ONI player.
    ///
    /// TIERS, matching Stationeers' own two thresholds rather
    /// than inventing new ones:
    ///   - <see cref="PipeStressLevel.Stressed"/> (past 80% of the rating, Stationeers'
    ///     <c>Thing.StressedRatio</c>) -> <c>NotificationType.BadMinor</c>, the tier vanilla uses
    ///     for an entombed or broken building. A nag, not an alarm.
    ///   - <see cref="PipeStressLevel.Critical"/> (past the rating itself, where Stationeers'
    ///     <c>ApplyPressureDamageToWeakest</c> starts doing real damage) ->
    ///     <c>NotificationType.Bad</c>, which also makes <c>StatusItem.AddNotification</c> pick
    ///     the "Warning" sound rather than the ordinary notification chime.
    /// <c>DuplicantThreatening</c> is deliberately NOT used: that tier is vanilla's reserve for a
    /// duplicant actually dying, and spending it on infrastructure would devalue it everywhere
    /// else in the game.
    ///
    /// ONE TILE PER NETWORK: Stationeers itself picks a single
    /// <c>_weakestMember</c> per network to damage, and a 200-tile run flagging 200 status items
    /// would bury the notification list. <see cref="PipeNetworkFacade.TryEvaluateStress"/> does
    /// the picking; this class only places the marker.
    ///
    /// SCOPE, stated plainly: only pipes Mod 1 physics has actually touched are monitored
    /// (<c>GasMixtureFacade.ManagedConduitCells</c> / <c>ManagedLiquidConduitCells</c> -- the
    /// sets populated by this mod's own network equalization). A pipe that no Mod 1 tank or pump
    /// has ever exchanged with is not scanned. That keeps the per-second cost proportional to
    /// what the mod is actually simulating instead of to map size; extending it to every conduit
    /// on the map is a real, separate piece of work (it needs an enumeration of
    /// <c>ConduitFlow</c>'s own conduit list, which vanilla does not expose) and is deliberately
    /// not attempted here.
    /// </summary>
    public sealed class PipeStressMonitor : MonoBehaviour
    {
        // Once per second. The underlying physics moves on its own ~1 s tank cadence
        // (GasMixtureTankComponent.PullIntervalSeconds), so scanning faster could not produce a
        // different answer -- it would only burn frames re-deriving one.
        private const float ScanIntervalSeconds = 1f;
        private float scanTimer;

        /// <summary>
        /// <c>GameClock</c> reading at the end of the previous scan, so the phase-change pass can
        /// be handed SIMULATED elapsed time rather than the real seconds <see cref="Update"/>
        /// gates on. The distinction matters: <c>Time.unscaledDeltaTime</c> keeps running while
        /// the game is paused and does not change when the player changes speed, and phase change
        /// billed against it would convert mass in a paused colony and convert it at the wrong
        /// rate in a fast one. Negative until the first scan establishes a baseline.
        /// </summary>
        private float lastPhaseChangeClockSeconds = -1f;

        private static StatusItem stressedItem;
        private static StatusItem criticalItem;

        /// <summary>
        /// The two status items, for a test harness to ask <c>KSelectable.HasStatusItem</c>
        /// about. Exposed deliberately: verifying this system by reading its own bookkeeping
        /// dictionary would only prove the dictionary agrees with itself, whereas asking the
        /// real KSelectable proves ONI actually accepted and is showing the marker.
        /// Null until the first scan builds them.
        /// </summary>
        internal static StatusItem StressedItem => stressedItem;

        internal static StatusItem CriticalItem => criticalItem;

        // Cell -> what is currently attached there. Keyed by the WORST cell rather than by
        // network, so a network that changes shape (a pipe deconstructed mid-run) self-corrects
        // on the next scan instead of leaking a marker on a tile that is no longer the worst.
        private readonly Dictionary<int, Attachment> active = new Dictionary<int, Attachment>();

        // Scratch, reused every scan so a per-second sweep allocates nothing steady-state.
        private readonly HashSet<int> visited = new HashSet<int>();
        private readonly Dictionary<int, Desired> desired = new Dictionary<int, Desired>();
        private readonly List<int> toRemove = new List<int>();

        private sealed class Attachment
        {
            internal KSelectable Selectable;
            internal System.Guid Handle;
            internal PipeStressLevel Level;
            internal PipeStressData Data;
        }

        private struct Desired
        {
            internal PipeStressLevel Level;
            internal string Summary;
            internal string Detail;
        }

        private void Update()
        {
            if (Game.Instance == null || !Grid.IsInitialized())
            {
                return;
            }

            scanTimer += Time.unscaledDeltaTime;
            if (scanTimer < ScanIntervalSeconds)
            {
                return;
            }
            scanTimer = 0f;

            try
            {
                EnsureStatusItems();
                Scan();
            }
            catch (System.Exception e)
            {
                // Same policy as HoverPatch: a warning system must never be the thing that
                // breaks a running colony. Logged once per failure rather than swallowed
                // silently, because unlike a tooltip this one has no visible symptom when it
                // stops working.
                Debug.LogWarning($"[Mod1ThermoFluid] pipe stress scan failed: {e}");
            }
        }

        /// <summary>
        /// Builds the two status items on first use. Deferred out of construction because
        /// <c>Assets.GetTintedSprite</c> (reached through the StatusItem constructor) needs the
        /// asset database, which is not up when this component is created during Game.OnSpawn.
        ///
        /// Uses the explicit name/tooltip constructor rather than vanilla's own
        /// <c>CreateStatusItem</c> helper: that helper composes a "STRINGS.BUILDING.STATUSITEMS.*"
        /// prefix and looks every string up in Klei's table, which a mod's own status item has no
        /// entries in. Passing the text directly is the supported alternative -- and it is why
        /// AddNotification is called with explicit strings too, since its no-argument form
        /// asserts that a prefix exists.
        /// </summary>
        private static void EnsureStatusItems()
        {
            if (stressedItem != null)
            {
                return;
            }

            stressedItem = new StatusItem(
                "Mod1PipeNetworkStressed",
                "Pipe Network Stressed",
                "This pipe network is approaching a physical limit.",
                "status_item_exclamation",
                StatusItem.IconType.Custom,
                NotificationType.BadMinor,
                allow_multiples: false,
                render_overlay: OverlayModes.None.ID);
            stressedItem.AddNotification(
                sound_path: null,
                notification_text: "Pipe network stressed",
                notification_tooltip: "A pipe network is approaching a pressure or phase-change limit.");

            criticalItem = new StatusItem(
                "Mod1PipeNetworkCritical",
                "Pipe Network Failing",
                "This pipe network is past a physical limit and would be taking damage.",
                "status_item_exclamation",
                StatusItem.IconType.Custom,
                NotificationType.Bad,
                allow_multiples: false,
                render_overlay: OverlayModes.None.ID);
            criticalItem.AddNotification(
                sound_path: null,
                notification_text: "Pipe network failing",
                notification_tooltip: "A pipe network is past its pressure rating or its contents are changing phase inside it.");

            // The live numbers ride on the per-attachment data object, not on the shared item.
            // Name and tooltip get different callbacks so the status list stays a one-liner while
            // the hover tooltip carries the whole readout.
            stressedItem.resolveStringCallback = ResolveSummary;
            stressedItem.resolveTooltipCallback = ResolveDetail;
            criticalItem.resolveStringCallback = ResolveSummary;
            criticalItem.resolveTooltipCallback = ResolveDetail;
        }

        private static string ResolveSummary(string fallback, object data)
        {
            return data is PipeStressData d && !string.IsNullOrEmpty(d.Summary) ? d.Summary : fallback;
        }

        private static string ResolveDetail(string fallback, object data)
        {
            return data is PipeStressData d && !string.IsNullOrEmpty(d.Detail) ? d.Detail : fallback;
        }

        private void Scan()
        {
            visited.Clear();
            desired.Clear();

            // Standing condensate/ice housekeeping (PipeMatterState), once a second on the same
            // cadence as the stress scan. It lives HERE rather than on the freeze path because
            // the freeze path only runs while a pipe IS cold -- nothing there would ever observe
            // a pipe recovering, so trapped matter would accumulate forever after a transient
            // chill. This is also where a ruptured or deconstructed conduit leaks what it was
            // holding into the world.
            PipeMatterState.TickRecovery(gasConduit: true);
            PipeMatterState.TickRecovery(gasConduit: false);

            TickPressurePhaseChange();

            CollectStressedNetworks(GasMixtureFacade.ManagedConduitCells, PipeContentType.Gas);
            CollectStressedNetworks(GasMixtureFacade.ManagedLiquidConduitCells, PipeContentType.Liquid);

            Reconcile();
        }

        /// <summary>
        /// Drives <see cref="PipeMatterFacade.TickNetworkPhaseChange"/> over every managed pipe
        /// network -- the periodic half of phase change, and the half the Condensation-Valve/
        /// Purge-Valve loop runs on.
        ///
        /// WHY IT IS PERIODIC AND NOT EVENT-DRIVEN is the same argument
        /// <see cref="ApplyPressureDamage"/> already makes for overpressure, and for the same
        /// reason: vanilla raises a conduit event only when contents cross a FLAT temperature
        /// threshold, so a line that has been pushed past its pressure-dependent dew point by a
        /// pump generates no event at all. It simply sits there condensing, and something has to
        /// go and look. This 1 Hz sweep is already resolving exactly the network state that
        /// decision needs.
        ///
        /// SCOPED TO MANAGED CELLS, which is to say pipes this mod's own tanks and pumps have
        /// touched. That bounds the cost -- an unbounded sweep would flood-fill every conduit
        /// network in the colony once a second -- and it means an ordinary vanilla pipe somewhere
        /// else on the map behaves exactly as it always did.
        ///
        /// Its own visited set, deliberately separate from the stress pass's: conversion changes
        /// the very numbers the stress pass reads, so the stress pass must resolve its networks
        /// AFTER this has run rather than share a snapshot taken before it.
        /// </summary>
        private void TickPressurePhaseChange()
        {
            float now = GameClock.Instance != null
                ? GameClock.Instance.GetTime()
                : lastPhaseChangeClockSeconds;

            float elapsedSeconds = lastPhaseChangeClockSeconds >= 0f
                ? now - lastPhaseChangeClockSeconds
                : 0f;
            lastPhaseChangeClockSeconds = now;

            if (elapsedSeconds <= 0f)
            {
                // Paused, or the first scan of the session. Either way there is no simulated time
                // to bill a conversion against.
                return;
            }

            var seen = new HashSet<int>();
            TickPressurePhaseChange(GasMixtureFacade.ManagedConduitCells, PipeContentType.Gas,
                elapsedSeconds, seen);
            TickPressurePhaseChange(GasMixtureFacade.ManagedLiquidConduitCells,
                PipeContentType.Liquid, elapsedSeconds, seen);
        }

        private void TickPressurePhaseChange(HashSet<int> managedCells,
            PipeContentType contentType, float elapsedSeconds, HashSet<int> seen)
        {
            if (managedCells.Count == 0)
            {
                return;
            }

            foreach (int cell in new List<int>(managedCells))
            {
                if (seen.Contains(cell))
                {
                    continue;
                }

                // Mark the whole run seen before converting, so a network is evaluated once per
                // sweep however many of its tiles are in the managed set.
                if (PipeNetworkFacade.TryGetNetworkState(cell, contentType,
                    out PipeNetworkState state) && state.Cells != null)
                {
                    foreach (int c in state.Cells)
                    {
                        seen.Add(c);
                    }
                }
                else
                {
                    seen.Add(cell);
                    continue;
                }

                PipeMatterFacade.TickNetworkPhaseChange(cell, contentType, elapsedSeconds);

                // THE HAZARD, evaluated wherever the standing matter came from. Evaluated only
                // inside the OnConduitFrozen interception, a pipe whose condensate arrives through
                // the PRESSURE path above would accumulate liquid without limit and never be
                // damaged for it -- a run can condense its entire charge to a liquid volume ratio
                // thirty-odd times Stationeers' 0.02 damage threshold and stay intact.
                //
                // Evaluated here rather than moved here, and both call sites kept. Stationeers'
                // own EvaluateIncorrectMatterState is a periodic network scan, so this cadence is
                // the faithful one; but the event path deals its damage per freeze event, which
                // is far faster than 1 Hz, and the cold-pipe phases (7, 8 and 9) burst inside
                // their windows precisely because of that. Collapsing the two onto this sweep
                // alone would make a freezing pipe fifty times slower to fail, which is a
                // balance change nobody asked for. A pipe that is BOTH freezing and condensing
                // takes damage from both, which is the correct answer for a pipe in two bad
                // states at once, and a run with a hole in it takes no further damage from
                // either -- DamageWeakestMember refuses it.
                Conduit_OnConduitFrozen_Condense.EvaluateMatterStateDamage(state.Cells,
                    contentType != PipeContentType.Liquid);
            }
        }

        /// <summary>
        /// Walks a managed-conduit set, evaluating each distinct connected network exactly once.
        /// <see cref="visited"/> carries every tile of every network already looked at this pass,
        /// so a 40-tile run costs one flood fill rather than forty.
        /// </summary>
        private void CollectStressedNetworks(HashSet<int> managedCells, PipeContentType contentType)
        {
            if (managedCells.Count == 0)
            {
                return;
            }

            // Copied because evaluating a network can prune dead cells out of the live set, and
            // mutating a HashSet mid-enumeration throws.
            foreach (int cell in new List<int>(managedCells))
            {
                if (visited.Contains(cell))
                {
                    continue;
                }
                if (!PipeNetworkFacade.TryGetNetworkState(cell, contentType,
                    out PipeNetworkState state))
                {
                    visited.Add(cell);
                    continue;
                }
                foreach (int c in state.Cells)
                {
                    visited.Add(c);
                }

                if (!PipeNetworkFacade.TryEvaluateStress(state, out PipeStressReport stress))
                {
                    continue;
                }

                if (ApplyPressureDamage(state, stress, contentType))
                {
                    // The run just burst and vented. The report in hand describes the pipe as it
                    // was a moment ago, so hanging an overpressure warning on it now would put a
                    // marker on an empty pipe for one second until the next scan cleared it.
                    continue;
                }

                string title = contentType == PipeContentType.Liquid
                    ? "Liquid Pipe (Mod 1)"
                    : "Gas Pipe (Mod 1)";
                string summary = PipeReadout.DescribeStress(stress);

                // If two networks somehow nominate the same tile (they cannot share a conduit
                // layer, but a gas and a liquid pipe can occupy the same cell), keep the worse.
                if (desired.TryGetValue(stress.WorstCell, out Desired existing) &&
                    existing.Level >= stress.Level)
                {
                    continue;
                }
                desired[stress.WorstCell] = new Desired
                {
                    Level = stress.Level,
                    Summary = summary,
                    Detail = summary + "\n\n" + PipeReadout.ToPlainText(title, state),
                };
            }
        }

        /// <summary>
        /// Stationeers' <c>PipeBurst.Pressure</c> branch: past the rating itself,
        /// <c>ApplyPressureDamageToWeakest</c> stops warning and starts doing real damage. This
        /// is the driver for it; the curve and the threshold both live in
        /// <see cref="PipeNetworkFacade.OverpressureDamage"/>, and the weakest-member pick is the
        /// same one every other pipe hazard uses.
        ///
        /// WHY THE DRIVER IS HERE rather than on a physics path. The two matter-state hazards
        /// have a natural event to hang off -- vanilla's own frozen-conduit trigger, which fires
        /// only when there is something to react to. Overpressure has no such event: a pipe that
        /// is simply too full sits there being too full, so the check has to be periodic, and
        /// this 1 Hz scan is already reading exactly the network state the decision needs.
        /// Evaluating it a second time somewhere else would only re-derive the same numbers.
        ///
        /// STRESSED IS NOT DAMAGED, deliberately, and that is Stationeers' own line: below the
        /// rating a pipe is <c>Stressed</c>, which Stationeers signals with positional audio and
        /// this project replaces with a <c>BadMinor</c> notification. Hit points start coming off
        /// only past <see cref="PipeStressLevel.Critical"/>.
        ///
        /// THE BURST RELIEVES THE RUN. If the damage breaks the member, the network is vented
        /// through the hole immediately (<c>PipeMatterState.RuptureAt</c>) rather than on some
        /// later tick -- a burst segment cannot hold pressure, and leaving the run pressurised
        /// behind a broken tile would let the next scan destroy the next segment, and the next,
        /// which is the cascade the matter-state path already had to be taught not to do.
        /// </summary>
        private static bool ApplyPressureDamage(PipeNetworkState state, PipeStressReport stress,
            PipeContentType contentType)
        {
            if (stress.Kind != PipeStressKind.Overpressure ||
                stress.Level != PipeStressLevel.Critical)
            {
                return false;
            }

            float damage = PipeNetworkFacade.OverpressureDamage(stress.Severity);
            if (damage <= 0f)
            {
                return false;
            }

            bool gasConduit = contentType != PipeContentType.Liquid;
            int brokenCell = Conduit_OnConduitFrozen_Condense.DamageWeakestMember(
                state.Cells, gasConduit, damage,
                PressureDamageSource,
                STRINGS.UI.GAMEOBJECTEFFECTS.DAMAGE_POPS.LIQUID_PRESSURE,
                gasConduit ? SpawnFXHashes.BuildingLeakGas : SpawnFXHashes.BuildingLeakLiquid,
                gasConduit ? "gas_damage_kanim" : "water_damage_kanim");

            if (brokenCell < 0)
            {
                return false;
            }

            // Vented at the tile that actually failed, not at the worst-pressure tile: the two
            // are generally different members, and "the broken segment leaks the rest of the
            // network out" is only an accurate model if the leak comes out of the break.
            PipeMatterState.RuptureAt(gasConduit, brokenCell);
            return true;
        }

        /// <summary>
        /// The damage-source line shown on the broken-building notification. Written out rather
        /// than reusing Klei's <c>DAMAGESOURCES.LIQUID_PRESSURE</c> ("neighboring liquid
        /// pressure"), which names the opposite situation -- vanilla's string is about liquid
        /// OUTSIDE a building crushing it, and this is the pipe's own contents pushing out. The
        /// pop string above IS reused, because "Pressure Damage" is accurate for both.
        /// </summary>
        private const string PressureDamageSource = "pressure inside the pipe exceeding its rating";

        /// <summary>
        /// Brings the attached status items in line with what this pass wants. Updating an
        /// existing attachment in place (rather than remove-then-add) is what keeps a
        /// continuously-stressed network from re-firing its notification sound every second.
        /// </summary>
        private void Reconcile()
        {
            toRemove.Clear();
            foreach (KeyValuePair<int, Attachment> entry in active)
            {
                Attachment attachment = entry.Value;
                bool stillWanted = desired.TryGetValue(entry.Key, out Desired want) &&
                    want.Level == attachment.Level;
                if (!stillWanted || attachment.Selectable == null)
                {
                    toRemove.Add(entry.Key);
                    continue;
                }
                attachment.Data.Summary = want.Summary;
                attachment.Data.Detail = want.Detail;
            }

            foreach (int cell in toRemove)
            {
                Attachment attachment = active[cell];
                if (attachment.Selectable != null)
                {
                    attachment.Selectable.RemoveStatusItem(attachment.Handle);
                }
                active.Remove(cell);
            }

            foreach (KeyValuePair<int, Desired> entry in desired)
            {
                if (active.ContainsKey(entry.Key))
                {
                    continue;
                }
                KSelectable selectable = SelectableConduitAt(entry.Key);
                if (selectable == null)
                {
                    continue;
                }
                StatusItem item = entry.Value.Level == PipeStressLevel.Critical
                    ? criticalItem
                    : stressedItem;
                var data = new PipeStressData
                {
                    Summary = entry.Value.Summary,
                    Detail = entry.Value.Detail,
                };
                active[entry.Key] = new Attachment
                {
                    Selectable = selectable,
                    Handle = selectable.AddStatusItem(item, data),
                    Level = entry.Value.Level,
                    Data = data,
                };
            }
        }

        /// <summary>
        /// The pipe building at a cell, as something a status item can hang off. Conduits live on
        /// their own object layers (<c>GasConduit</c>/<c>LiquidConduit</c>), NOT on
        /// <c>ObjectLayer.Building</c> where DebugInspectorServer's own building lookup goes, so
        /// this checks both conduit layers rather than reusing that path.
        /// </summary>
        private static KSelectable SelectableConduitAt(int cell)
        {
            if (!Grid.IsValidCell(cell))
            {
                return null;
            }
            GameObject go = Grid.Objects[cell, (int)ObjectLayer.GasConduit]
                ?? Grid.Objects[cell, (int)ObjectLayer.LiquidConduit];
            return go != null ? go.GetComponent<KSelectable>() : null;
        }

        /// <summary>
        /// Detaches the status items this monitor put on other objects.
        ///
        /// SKIPPED WHILE QUITTING, which is Klei's own rule for the same walk. Vanilla's
        /// <c>StatusItemGroup.Destroy</c> wraps its entire removal loop in
        /// <c>if (!Game.IsQuitting())</c>, and the reason is one line inside
        /// <c>StatusItemGroup.RemoveStatusItemInternal</c>:
        ///
        /// <code>
        /// if (arg.notification != null) {
        ///     gameObject.GetComponent&lt;Notifier&gt;().Remove(arg.notification);  // NOT guarded
        /// }
        /// </code>
        ///
        /// Both of this monitor's items call <c>AddNotification</c>, so <c>shouldNotify</c> is
        /// set and every attachment carries a notification -- which means every attachment takes
        /// that branch. During teardown the conduit's <c>Notifier</c> component is already
        /// destroyed while the entry is still in the group, <c>GetComponent</c> returns null, and
        /// the call throws. The caller cannot defend against it: <c>Selectable</c> passing a
        /// <c>!= null</c> check says nothing about a sibling component, and the throw is two
        /// frames below <c>KSelectable.RemoveStatusItem</c>.
        ///
        /// Without this guard an NRE in <c>KSelectable.RemoveStatusItem</c> fires on every quit.
        /// It is distinct from the defect <see cref="OniFramework.NotificationSafety"/> fixes -- that one is an unguarded event invoke and fires during normal play; this one
        /// is a destroyed sibling component and fires only on teardown.
        ///
        /// Skipping is safe for the same reason it is safe for Klei: this component lives on the
        /// Game object, so every conduit it attached to is being destroyed in the same teardown.
        /// Nothing it hung can outlive it.
        /// </summary>
        private void OnDestroy()
        {
            if (!Game.IsQuitting())
            {
                foreach (KeyValuePair<int, Attachment> entry in active)
                {
                    if (entry.Value.Selectable != null)
                    {
                        entry.Value.Selectable.RemoveStatusItem(entry.Value.Handle);
                    }
                }
            }
            active.Clear();
        }
    }
}
