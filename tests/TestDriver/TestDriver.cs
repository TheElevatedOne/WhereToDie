using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using Path = System.IO.Path;

namespace WhereToDie.TestDriver
{
    // Not shipped. Drives a singleplayer run through the WhereToDie scenarios, checks the results,
    // takes screenshots into BepInEx/wdtest/ and quits the game. Results: BepInEx/wdtest/results.txt
    [BepInDependency("com.elevated.WhereToDie")]
    [BepInPlugin("com.elevated.WhereToDie.TestDriver", "WhereToDieTestDriver", "0.0.1")]
    public class TestDriver : BaseUnityPlugin
    {
        private string outDir;
        private readonly StringBuilder results = new StringBuilder();
        private int passed, failed;
        private bool finished;
        private const float Deadline = 2400f;

        private const string FinaleFlag = "WhereToDie_FinalePath";
        private bool showcaseStageOnly;
        private static Type trackerType;
        private bool godMode = true;

        public void Awake()
        {
            outDir = Path.Combine(Paths.BepInExRootPath, "wdtest");
            Directory.CreateDirectory(outDir);
            foreach (var file in Directory.GetFiles(outDir))
            {
                File.Delete(file);
            }
            trackerType = Type.GetType("WhereToDie.ZoneTracker, WhereToDie");
            Application.logMessageReceived += OnLog;
            // tests/run_ingame_test.sh writes "showcase" here for `make screenshots`.
            var modeFile = Path.Combine(Paths.BepInExRootPath, "wdtest_mode.txt");
            string mode = File.Exists(modeFile) ? File.ReadAllText(modeFile).Trim() : "test";
            // "showcase:stage" retakes only the stage-start shots.
            showcaseStageOnly = mode == "showcase:stage";
            StartCoroutine(mode.StartsWith("showcase") ? RunShowcase() : RunAll());
        }

        // A crash inside the coroutine stops it silently; report it and quit instead of hanging.
        private readonly Dictionary<string, int> exceptions = new Dictionary<string, int>();

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Exception)
            {
                string key = condition + " @ " + stackTrace.Split('\n')[0];
                exceptions[key] = exceptions.TryGetValue(key, out var n) ? n + 1 : 1;
            }
            if (!finished && type == LogType.Exception && (stackTrace.Contains("WhereToDie") || condition.Contains("WhereToDie")))
            {
                Check("no exception", false, condition + " @ " + stackTrace.Split('\n')[0]);
                if (stackTrace.Contains("TestDriver"))
                {
                    StartCoroutine(Finish());
                }
            }
        }

        public void Update()
        {
            if (!finished && Time.realtimeSinceStartup > Deadline)
            {
                Check("finished within time limit", false);
                StartCoroutine(Finish());
            }
            // Keep the run quiet: no monsters, and the player can't die except by the ritual.
            if (NetworkServer.active && Run.instance)
            {
                foreach (var director in CombatDirector.instancesList.ToList())
                {
                    if (director.enabled)
                    {
                        director.monsterCredit = 0f;
                        director.enabled = false;
                    }
                }
                foreach (var player in PlayerCharacterMasterController.instances)
                {
                    var body = player && player.master ? player.master.GetBody() : null;
                    if (body && body.healthComponent)
                    {
                        // Only the first player casts rituals; everyone else stays invulnerable.
                        body.healthComponent.godMode = body == Body ? godMode : true;
                    }
                }
            }
        }

        #region Helpers

        private void Note(string text)
        {
            Logger.LogInfo("[WDTEST] " + text);
            results.AppendLine("      " + text);
            Flush();
        }

        private void Check(string name, bool ok, string detail = "")
        {
            if (ok) passed++; else failed++;
            string line = $"[{(ok ? "PASS" : "FAIL")}] {name}{(detail.Length > 0 ? " -- " + detail : "")}";
            Logger.LogInfo("[WDTEST] " + line);
            results.AppendLine(line);
            Flush();
        }

        private void Flush()
        {
            File.WriteAllText(Path.Combine(outDir, "results.txt"), results.ToString() + $"\n(passed {passed}, failed {failed})\n");
        }

        private IEnumerator Shot(string name)
        {
            yield return new WaitForSecondsRealtime(0.5f);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(outDir, name + ".png"));
            yield return new WaitForSecondsRealtime(0.5f);
            Note("screenshot " + name + ".png");
        }

        private IEnumerator WaitFor(Func<bool> condition, float timeout, string what, bool mustSucceed = true)
        {
            float end = Time.realtimeSinceStartup + timeout;
            while (Time.realtimeSinceStartup < end)
            {
                bool ok = false;
                try { ok = condition(); } catch { }
                if (ok) { Note("ready: " + what); yield break; }
                yield return null;
            }
            if (mustSucceed)
            {
                Check("wait for " + what, false, $"timed out after {timeout}s");
            }
            else
            {
                Note("not reached: " + what);
            }
        }

        private static LocalUser FirstUser => LocalUserManager.GetFirstLocalUser();
        private static CharacterBody SecondBody => LocalUserManager.readOnlyLocalUsersList.Count > 1 ? LocalUserManager.readOnlyLocalUsersList[1].cachedBody : null;
        private static CharacterBody Body => FirstUser?.cachedBody;
        private static Inventory Inv => Body ? Body.inventory : null;
        private static string Scene => SceneCatalog.GetSceneDefForCurrentScene()?.cachedName ?? "";

        private static int Count(ItemDef item) => Inv ? Inv.GetItemCountEffective(item) : -1;
        private static int CountPermanent(ItemDef item) => Inv ? Inv.GetItemCountPermanent(item) : -1;

        private static IList Markers => (IList)trackerType.GetField("Markers", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).GetValue(null);

        private static bool MarkerFlag(object marker, string field) =>
            (bool)marker.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(marker);

        private static Vector3 MarkerPosition(object marker) =>
            (Vector3)marker.GetType().GetField("Position", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(marker);

        private static string DescribeMarkers()
        {
            var list = Markers;
            var parts = new List<string>();
            foreach (var m in list)
            {
                parts.Add($"{MarkerPosition(m)} dies={MarkerFlag(m, "Dies")} stageWide={MarkerFlag(m, "StageWide")}");
            }
            return $"{list.Count}: " + string.Join("; ", parts);
        }

        private static bool AnyMarker(Func<object, bool> predicate)
        {
            foreach (var m in Markers)
            {
                if (predicate(m)) return true;
            }
            return false;
        }

        private static int AuroraCount() => FindObjectsOfType<Transform>().Count(t => t.name == "WhereToDie_Aurora");

        private static bool ReadyBuff => Body && Body.HasBuff(DLC4Content.Buffs.CursedBookReadyForSacrifice);

        // The mod's PageIconGradient on the Reincarnation icon: null = not tinted, else whether it shows "dies" (green-red).
        private static bool? IconTint()
        {
            var type = Type.GetType("WhereToDie.PageIconGradient, WhereToDie");
            foreach (var component in Resources.FindObjectsOfTypeAll(type))
            {
                var behaviour = (Behaviour)component;
                if (!behaviour || !behaviour.gameObject.scene.IsValid() || !behaviour.isActiveAndEnabled) continue;
                var dies = (bool?)type.GetProperty("Dies", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(component);
                if (dies != null) return dies;
            }
            return null;
        }

        private static string Tint => IconTint() == null ? "none" : IconTint().Value ? "green-red" : "green-yellow";

        private static T GetField<T>(object obj, string name)
        {
            return (T)obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(obj);
        }

        private static void SetField(object obj, string name, object value)
        {
            obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).SetValue(obj, value);
        }

        private bool ChatContains(string text) => Chat.readOnlyLog.Any(line => line.Contains(text));

        private IEnumerator GoTo(string sceneName)
        {
            if (Run.instance && Run.instance.isGameOverServer)
            {
                Check($"run still alive before {sceneName}", false);
                yield break;
            }
            var scene = SceneCatalog.FindSceneDef(sceneName);
            if (!scene)
            {
                Check($"scene {sceneName} exists", false);
                yield break;
            }
            Note($"advancing to {sceneName}");
            Run.instance.AdvanceStage(scene);
            yield return WaitFor(() => Scene == sceneName && Stage.instance && Body, 120, "arrived in " + sceneName);
            yield return new WaitForSecondsRealtime(1f);
            if (Body && Body.currentVehicle)
            {
                yield return ExitPod();
            }
            yield return new WaitForSecondsRealtime(2f);
        }

        // The run (and some stages) start in a drop pod; get out right away so tests act on a free player.
        private IEnumerator ExitPod()
        {
            float end = Time.realtimeSinceStartup + 5f;
            while (Body && !Body.currentVehicle && Time.realtimeSinceStartup < end)
            {
                yield return null;
            }
            if (Body && Body.currentVehicle)
            {
                Body.currentVehicle.EjectPassenger();
                Note("left the drop pod");
                yield return new WaitForSecondsRealtime(1.5f);
            }
        }

        // Casts the Reincarnation ritual where the player stands (what EntityStates.CursedBook.Spells.Die spawns), then
        // checks the chat message and the transfer to the Soulbound Threshold. The ritual kills the player, so it is only
        // cast where the game agrees the zone works; otherwise the run would just end in a defeat.
        private IEnumerator CastRitual(string place, string message, string shot, string destination = "champrush")
        {
            if (!ReadyBuff)
            {
                Check($"{place}: ritual skipped (not in a working zone)", false);
                yield break;
            }
            godMode = false;
            yield return null;
            GameObject prefab = EntityStates.CursedBook.Spells.Die.ritualCirclePrefab;
            var circle = Instantiate(prefab);
            if (Physics.Raycast(Body.footPosition, Vector3.down, out var hit, 15000f, LayerIndex.world.mask))
            {
                circle.transform.SetPositionAndRotation(hit.point, Quaternion.identity);
                Note($"{place}: ritual circle at {hit.point} ({hit.collider.name})");
            }
            NetworkServer.Spawn(circle);
            if (message != null)
            {
                yield return WaitFor(() => ChatContains(message), 15, $"{place} chat message");
                Check($"{place}: chat message \"{message}\"", ChatContains(message));
            }
            yield return Shot(shot);
            yield return WaitFor(() => Scene == destination && Body, 60, $"{place} -> {destination}");
            godMode = true;
            Check($"{place}: ritual leads to {destination}", Scene == destination, $"scene={Scene}");
            yield return new WaitForSecondsRealtime(3f);
            yield return Shot(shot + "_arrived");
            if (Run.instance.GetEventFlag(FinaleFlag))
            {
                Check($"{place}: finale path started", true);
                // The test keeps visiting other stages; the finale path itself is tested at the end.
                Run.instance.ResetEventFlag(FinaleFlag);
            }
            if (destination == "champrush")
                Check($"{place}: page usable on arrival", Count(DLC4Content.Items.PageDie) == 1 && CountPermanent(DLC4Content.Items.PageDieCooldown) == 0,
                $"PageDie={Count(DLC4Content.Items.PageDie)} consumed={Count(DLC4Content.Items.PageDieConsumed)} cooldown={CountPermanent(DLC4Content.Items.PageDieCooldown)}");
        }

        private IEnumerator TeleportTo(Vector3 foot, float settle = 2.5f)
        {
            TeleportHelper.TeleportBody(Body, foot, false);
            yield return new WaitForSecondsRealtime(settle);
            Note($"player at {Body.footPosition} (core {Body.corePosition})");
        }


        // A fixed camera for screenshots that need to look at something (the aurora is above eye level).
        // Must be a UnityEngine.Object: CameraModePlayerBasic casts the override camera to one.
        private class FixedCamera : MonoBehaviour, ICameraStateProvider
        {
            internal Vector3 Position;
            internal Quaternion Rotation;
            public void GetCameraState(CameraRigController cameraRigController, ref CameraState cameraState)
            {
                cameraState.position = Position;
                cameraState.rotation = Rotation;
            }
            public bool IsUserLookAllowed(CameraRigController cameraRigController) => false;
            public bool IsUserControlAllowed(CameraRigController cameraRigController) => false;
            internal bool Hud = true;
            public bool IsHudAllowed(CameraRigController cameraRigController) => Hud;
        }

        private IEnumerator ShotLookingAt(string name, Vector3 from, Vector3 target)
        {
            var rig = CameraRigController.readOnlyInstancesList.FirstOrDefault(r => r && r.localUserViewer == FirstUser);
            if (!rig)
            {
                yield return Shot(name);
                yield break;
            }
            var camera = new GameObject("WhereToDieTestCamera").AddComponent<FixedCamera>();
            camera.Position = from;
            camera.Rotation = Quaternion.LookRotation(target - from);
            rig.SetOverrideCam(camera, 0f);
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Shot(name);
            rig.SetOverrideCam(null, 0f);
            Destroy(camera.gameObject);
            yield return new WaitForSecondsRealtime(0.5f);
        }

        private static List<Vector3> AuroraPositions() =>
            FindObjectsOfType<Transform>().Where(t => t.name == "WhereToDie_Aurora").Select(t => t.position).ToList();

        // Looks at the aurora nearest to `near` from `distance` metres away horizontally, a bit below it.
        private IEnumerator ShotOfAurora(string name, Vector3 near, float distance)
        {
            var auroras = AuroraPositions();
            if (auroras.Count == 0)
            {
                Note($"no aurora to look at for {name}");
                yield break;
            }
            var aurora = auroras.OrderBy(p => (p - near).sqrMagnitude).First();
            Note($"aurora at {aurora}");
            var flat = near - aurora;
            flat.y = 0f;
            if (flat.sqrMagnitude < 1f) flat = Vector3.back;
            var from = new Vector3(aurora.x, near.y + 8f, aurora.z) + flat.normalized * distance;
            var target = new Vector3(aurora.x, Mathf.Lerp(near.y, aurora.y, 0.5f), aurora.z);
            yield return ShotLookingAt(name, from, target);
        }
        #endregion

        private IEnumerator RunAll()
        {
            Check("WhereToDie loaded", trackerType != null);
            yield return WaitFor(() => RoR2Application.loadFinished && SceneCatalog.mostRecentSceneDef && SceneCatalog.mostRecentSceneDef.baseSceneName == "title", 300, "title screen");
            yield return new WaitForSecondsRealtime(3f);

            // A second local (split-screen) player with a guest profile that never saves: a real 2-player session
            // without opening a public lobby.
            RoR2.Console.instance.SubmitCmd(null, "test_splitscreen 2");
            yield return WaitFor(() => LocalUserManager.readOnlyLocalUsersList.Count == 2, 20, "two local players");
            // What the local multiplayer menu sets: only then does the client add a player for every local user.
            RoR2Application.isInLocalMultiPlayer = true;
            // host takes its player limit from sv_maxplayers, which is 1 at the main menu.
            RoR2.Console.instance.SubmitCmd(null, "sv_maxplayers 4");
            RoR2.Console.instance.SubmitCmd(null, "transition_command \"gamemode ClassicRun; host 0;\"");
            yield return WaitFor(() => PreGameController.instance && FirstUser?.currentNetworkUser, 90, "lobby");
            yield return new WaitForSecondsRealtime(3f);
            RoR2.Console.instance.SubmitCmd(null, "pregame_start_run");
            yield return WaitFor(() => Run.instance && Stage.instance && Body && SecondBody, 120, "run + both bodies");
            Check("two players in the run", PlayerCharacterMasterController.instances.Count == 2, $"players={PlayerCharacterMasterController.instances.Count}");
            if (!SecondBody)
            {
                yield return Finish();
                yield break;
            }
            yield return ExitPod();

            var threshold = SceneCatalog.FindSceneDef("champrush");
            bool dlc4 = threshold && (!threshold.requiredExpansion || Run.instance.IsExpansionEnabled(threshold.requiredExpansion));
            Check("Hallowed Concepts enabled in the run", dlc4);
            if (!dlc4)
            {
                yield return Finish();
                yield break;
            }

            yield return TestBookAndStageWindow();
            yield return TestRefreshInThreshold();
            yield return TestReentry();
            yield return TestMoonRoof();
            yield return TestPlanetarium();
            yield return TestMeridian();
            yield return TestLucidityGating();
            yield return TestFinalePath();

            yield return Finish();
        }

        // N'tormat gives the page; the 30 s stage-wide zone shows the aurora and the deadly icon frame.
        private IEnumerator TestBookAndStageWindow()
        {
            Note($"stage 1 = {Scene}, {Stage.instance.entryTime.timeSince:0.0}s since entry");
            var book = DLC4Content.KeyItems.CursedBook;
            var pickupIndex = PickupCatalog.FindPickupIndex(book.keyItemIndex);
            var pickupDef = PickupCatalog.GetPickupDef(pickupIndex);
            var context = new PickupDef.GrantContext
            {
                body = Body,
                pickup = new UniquePickup(pickupIndex),
                position = Body.corePosition,
                rotation = Quaternion.identity,
                shouldNotify = true,
            };
            pickupDef.attemptGrant(ref context);
            yield return new WaitForSecondsRealtime(1f);
            Check("N'tormat picked up", Inv.keyItem == book);
            Check("N'tormat gives the Page of Reincarnation", Count(DLC4Content.Items.PageDie) == 1, $"PageDie={Count(DLC4Content.Items.PageDie)}");

            // Player 2 picks up their own N'tormat.
            var context2 = new PickupDef.GrantContext { body = SecondBody, pickup = new UniquePickup(pickupIndex), position = SecondBody.corePosition, rotation = Quaternion.identity, shouldNotify = true };
            pickupDef.attemptGrant(ref context2);
            yield return new WaitForSecondsRealtime(1f);
            Check("player 2: N'tormat gives the page too", SecondBody.inventory.GetItemCountEffective(DLC4Content.Items.PageDie) == 1);
            yield return TestMarkerRoundTrip();

            yield return WaitFor(() => AnyMarker(m => MarkerFlag(m, "StageWide")), 10, "stage-wide marker", false);
            Note("markers: " + DescribeMarkers());
            Check("stage-wide zone marked during the first 30 s", AnyMarker(m => MarkerFlag(m, "StageWide") && MarkerFlag(m, "Dies")));
            yield return WaitFor(() => ReadyBuff, 5, "ready-for-sacrifice buff", false);
            yield return WaitFor(() => IconTint() != null, 3, "icon tint", false);
            Check("icon tinted green-red", IconTint() == true, $"tint={Tint} buff={ReadyBuff}");
            Check("aurora spawned over the stage", AuroraCount() > 0, $"auroras={AuroraCount()}");
            Note($"auroras={AuroraCount()} at {Stage.instance.entryTime.timeSince:0.0}s");
            yield return Shot("01_stage_window");
            yield return ShotOfAurora("01b_stage_aurora", Body.corePosition, 60f);

            yield return WaitFor(() => Stage.instance.entryTime.timeSince > 30.6f, 40, "30 s window over");
            yield return new WaitForSecondsRealtime(0.6f);
            Note($"auroras fading out: {AuroraCount()} still present right after the window closed");
            yield return new WaitForSecondsRealtime(2.5f);
            Note("markers: " + DescribeMarkers());
            Check("stage-wide marker gone after 30 s", !AnyMarker(m => MarkerFlag(m, "StageWide")));
            Check("aurora gone after 30 s", AuroraCount() == 0, $"auroras={AuroraCount()}");
            Check("icon tint gone after 30 s", IconTint() == null, $"tint={Tint} buff={ReadyBuff}");
            yield return Shot("02_after_window");
        }

        // A page spent on a stage transition (consumed + cooldown) is usable on arrival in the Soulbound Threshold.
        private IEnumerator TestRefreshInThreshold()
        {
            foreach (var inventory in new[] { Inv, SecondBody.inventory })
            {
                inventory.RemoveItemPermanent(DLC4Content.Items.PageDie, 1);
                inventory.GiveItemPermanent(DLC4Content.Items.PageDieConsumed, 1);
                inventory.GiveItemPermanent(DLC4Content.Items.PageDieCooldown, 1);
            }
            Note($"before: PageDie={Count(DLC4Content.Items.PageDie)} consumed={Count(DLC4Content.Items.PageDieConsumed)} cooldown={CountPermanent(DLC4Content.Items.PageDieCooldown)}");
            yield return GoTo("champrush");
            Note($"after: PageDie={Count(DLC4Content.Items.PageDie)} consumed={Count(DLC4Content.Items.PageDieConsumed)} cooldown={CountPermanent(DLC4Content.Items.PageDieCooldown)}");
            var inv2 = SecondBody ? SecondBody.inventory : null;
            Check("player 2: page recharged in the Soulbound Threshold", inv2 && inv2.GetItemCountEffective(DLC4Content.Items.PageDie) == 1
                && inv2.GetItemCountEffective(DLC4Content.Items.PageDieConsumed) == 0 && inv2.GetItemCountPermanent(DLC4Content.Items.PageDieCooldown) == 0);
            Check("page recharged in the Soulbound Threshold",
                Count(DLC4Content.Items.PageDie) == 1 && Count(DLC4Content.Items.PageDieConsumed) == 0 && CountPermanent(DLC4Content.Items.PageDieCooldown) == 0);
            Note("markers: " + DescribeMarkers());
            yield return Shot("03_threshold");
        }

        // Anima Harvester defeated but Terminal Lucidity never entered: the next stage re-enables the way in.
        private IEnumerator TestReentry()
        {
            DLC4ProgressionController.instance.SetReaperBossDefeated();
            Check("reaper marked defeated", Run.instance.GetEventFlag("ReaperBossDefeated") && DLC4ProgressionController.instance.ReaperBossDefeated);
            yield return GoTo("blackbeach");
            var progression = DLC4ProgressionController.instance;
            Check("re-entry: reaper flag reset", !Run.instance.GetEventFlag("ReaperBossDefeated") && progression && !progression.ReaperBossDefeated,
                $"flag={Run.instance.GetEventFlag("ReaperBossDefeated")} controller={(progression ? progression.ReaperBossDefeated.ToString() : "none")}");
            Check("re-entry: soul-binding trial available again",
                progression && progression.catastropheProvider && !progression.catastropheProvider.HasSpawnedMajorCatastrophe);
        }

        // After Mithrix (escape sequence), the rocketship roof leads to the Soulbound Threshold through a real ritual.
        private IEnumerator TestMoonRoof()
        {
            yield return GoTo("moon2");
            var escape = FindObjectOfType<EscapeSequenceController>();
            Check("escape sequence controller found", escape);
            if (!escape) yield break;
            escape.BeginEscapeSequence();
            yield return WaitFor(() => GameObject.Find("WhereToDie_RocketshipRoof"), 5, "rocketship roof zone");
            var zone = GameObject.Find("WhereToDie_RocketshipRoof");
            if (!zone) yield break;
            Note($"roof zone at {zone.transform.position}");
            Check("roof zone works on the roof (zone check)", CursedBookSacrificeZone.IsInAnyValidSacrificeZone(zone.transform.position + Vector3.up));
            Note("markers: " + DescribeMarkers());

            yield return TeleportTo(zone.transform.position + Vector3.up * 1f, 3f);
            if (!ReadyBuff)
            {
                Note("not on the roof after the first teleport; retrying");
                yield return TeleportTo(zone.transform.position + Vector3.up * 1f, 3f);
            }
            Check("on the roof: ready-for-sacrifice buff", ReadyBuff);
            Check("on the roof: icon tinted green-red", IconTint() == true, $"tint={Tint}");
            Check("on the roof: aurora shown", AuroraCount() > 0, $"auroras={AuroraCount()}");
            yield return Shot("04_moon_roof");
            yield return ShotOfAurora("04b_moon_roof_aurora", zone.transform.position, 90f);

            // Away from the ship nothing should light up.
            var away = zone.transform.position + new Vector3(80f, 0f, 80f);
            if (Physics.Raycast(away + Vector3.up * 200f, Vector3.down, out var ground, 600f, LayerIndex.world.mask))
            {
                yield return TeleportTo(ground.point + Vector3.up, 3f);
                Check("off the roof: no buff, no tint", !ReadyBuff && IconTint() == null, $"buff={ReadyBuff} tint={Tint}");
                yield return TeleportTo(zone.transform.position + Vector3.up * 1f, 3f);
            }

            yield return CastRitual("moon", "Found a way to escape the Moon", "05_moon_ritual");
        }

        // After the False Son, the altar in his arena. Plays the event for real: walk into its trigger, kill each False Son
        // phase as it appears (the player can't die), let the game run his defeat and the cleared cutscene, then use the altar.
        private IEnumerator TestMeridian()
        {
            yield return GoTo("meridian");
            var meridian = FindObjectOfType<MeridianEventTriggerInteraction>();
            Check("meridian event found", meridian);
            if (!meridian) yield break;

            yield return TeleportTo(meridian.transform.position + Vector3.up, 2f);
            yield return WaitFor(() => meridian.meridianEventState >= MeridianEventState.Started, 20, "False Son event started");
            Check("no altar zone before the False Son is defeated", !GameObject.Find("WhereToDie_MeridianAltar"));

            float end = Time.realtimeSinceStartup + 360f;
            var lastState = MeridianEventState.None;
            while (Time.realtimeSinceStartup < end && meridian.meridianEventState < MeridianEventState.Cleared)
            {
                if (meridian.meridianEventState != lastState)
                {
                    lastState = meridian.meridianEventState;
                    Note($"event state {lastState}");
                }
                foreach (var body in CharacterBody.readOnlyInstancesList.ToList())
                {
                    if (body && body.isBoss && body.teamComponent && body.teamComponent.teamIndex == TeamIndex.Monster
                        && body.healthComponent && body.healthComponent.alive)
                    {
                        Note($"killing {body.GetDisplayName()} ({body.name})");
                        body.healthComponent.Suicide();
                    }
                }
                yield return new WaitForSecondsRealtime(1f);
            }
            Check("False Son defeated (event cleared)", meridian.meridianEventState == MeridianEventState.Cleared, $"state={meridian.meridianEventState}");
            Note("waiting for the cleared cutscene to finish");
            yield return new WaitForSecondsRealtime(20f);
            yield return Shot("06a_meridian_cleared");
            Check("no mod zone at Prime Meridian", !GameObject.Find("WhereToDie_MeridianAltar"));

            // The game's own zone ("Relic of the False Son") sits at the Shrine of Rebirth.
            var altar = FindObjectOfType<ShrineRebirthController>();
            var altarPosition = altar ? altar.transform.position : new Vector3(132.05f, 144.9f, -168.68f);
            yield return TeleportTo(altarPosition + Vector3.up, 3f);
            if (!ReadyBuff)
            {
                Note("not at the altar after the first teleport; retrying");
                yield return TeleportTo(altarPosition + Vector3.up, 3f);
            }
            Note($"distance to altar: {(Body.footPosition - altarPosition).magnitude:0.0} m");
            RoR2.Console.instance.SubmitCmd(null, "wheretodie_status");
            Check("at the altar: ready-for-sacrifice buff", ReadyBuff);
            Check("at the altar: icon tinted green-red", IconTint() == true, $"tint={Tint}");
            Check("at the altar: exactly one aurora", AuroraCount() == 1, $"auroras={AuroraCount()}");
            yield return Shot("06_meridian_altar");
            yield return ShotOfAurora("06b_meridian_aurora", altarPosition, 60f);
            yield return CastRitual("meridian (vanilla)", null, "06c_meridian_ritual", "ultima");
        }

        // After Voidling (outro portal), the Planetarium's final arena.
        private IEnumerator TestPlanetarium()
        {
            yield return GoTo("voidraid");
            var gauntlet = VoidRaidGauntletController.instance;
            Check("planetarium controller found", gauntlet);
            if (!gauntlet) yield break;
            var initial = GetField<VoidRaidGauntletController.DonutInfo>(gauntlet, "initialDonut");
            for (int i = 0; i < 6 && gauntlet.TryOpenGauntlet(initial.crabPosition.position, NetworkInstanceId.Invalid); i++) { }
            yield return new WaitForSecondsRealtime(2f);
            gauntlet.SpawnOutroPortal();
            yield return WaitFor(() => GameObject.Find("WhereToDie_PlanetariumFinale"), 5, "planetarium zone");
            var zone = GameObject.Find("WhereToDie_PlanetariumFinale");
            if (!zone) yield break;
            var donut = GetField<VoidRaidGauntletController.DonutInfo>(gauntlet, "currentDonut");
            Note($"planetarium zone at {zone.transform.position}; donut root {donut.root.transform.position}, "
                + $"crab {(donut.crabPosition ? donut.crabPosition.position.ToString() : "-")}, return {(donut.returnPoint ? donut.returnPoint.position.ToString() : "-")}");
            var target = donut.returnPoint ? donut.returnPoint.position : zone.transform.position;
            yield return TeleportTo(target + Vector3.up, 4f);
            Check("in the final arena: ready-for-sacrifice buff", ReadyBuff);
            Check("in the final arena: icon tinted green-red", IconTint() == true, $"tint={Tint}");
            Check("in the final arena: aurora shown", AuroraCount() > 0, $"auroras={AuroraCount()}");
            yield return Shot("07_planetarium");
            yield return ShotOfAurora("07b_planetarium_aurora", Body.corePosition, 150f);
            yield return CastRitual("planetarium", "Found peace in the Void", "07c_planetarium_ritual");
        }

        // After Terminal Lucidity: no new places and no re-entry.
        private IEnumerator TestLucidityGating()
        {
            if (Scene != "ultima")
            {
                yield return GoTo("ultima");
            }
            Check("Terminal Lucidity recorded", Run.instance.GetEventFlag("WhereToDie_UltimaEntered"));
            DLC4ProgressionController.instance.SetReaperBossDefeated();
            yield return GoTo("blackbeach");
            Check("after Terminal Lucidity: no re-entry", Run.instance.GetEventFlag("ReaperBossDefeated"));
            yield return GoTo("moon2");
            var escape = FindObjectOfType<EscapeSequenceController>();
            if (escape) escape.BeginEscapeSequence();
            yield return new WaitForSecondsRealtime(3f);
            Check("after Terminal Lucidity: no rocketship roof zone", !GameObject.Find("WhereToDie_RocketshipRoof"));
        }

        private static GameObject FindPortalMS() =>
            FindObjectsOfType<Transform>().Select(t => t.gameObject).FirstOrDefault(g => g.name.StartsWith("PortalMS"));

        private IEnumerator AdvanceAndWait(string target, string expected)
        {
            Note($"advancing to {target}, expecting {expected}");
            Run.instance.AdvanceStage(SceneCatalog.FindSceneDef(target));
            yield return WaitFor(() => Scene == expected && Stage.instance && Body, 90, $"arrived in {expected}", false);
            yield return new WaitForSecondsRealtime(3f);
        }

        // After Mithrix/Voidling (finale path): Obliteration portals instead of the ways back, and no way out of Obliteration.
        private IEnumerator TestFinalePath()
        {
            Run.instance.SetEventFlag(FinaleFlag);
            yield return GoTo("champrush");
            var mission = ChampRushMissionController.instance;
            Check("threshold mission found", mission);
            if (mission)
            {
                mission.CompleteReaperFight();
                yield return WaitFor(() => FindPortalMS(), 10, "Obliteration portal in the Soulbound Threshold", false);
                var portal = FindPortalMS();
                Check("threshold: Obliteration portal instead of the way back", portal, portal ? portal.transform.position.ToString() : "none");
                if (portal)
                {
                    yield return ShotLookingAt("08_threshold_obliteration_portal", portal.transform.position + new Vector3(12f, 6f, 12f), portal.transform.position + Vector3.up * 3f);
                }
            }

            yield return AdvanceAndWait("ultima", "ultima");
            yield return FightProvidence();
            {
                var portal = FindPortalMS();
                Check("lucidity: Obliteration portal instead of the final teleporter", portal, portal ? portal.transform.position.ToString() : "none");
                if (portal)
                {
                    yield return ShotLookingAt("09_lucidity_obliteration_portal", portal.transform.position + new Vector3(12f, 6f, 12f), portal.transform.position + Vector3.up * 3f);
                }
            }

            yield return AdvanceAndWait("blackbeach", "mysteryspace");
            Check("finale: leaving Terminal Lucidity for a normal stage lands in Obliteration", Scene == "mysteryspace", $"scene={Scene}");
            yield return Shot("10_obliteration");
            Run.instance.AdvanceStage(SceneCatalog.FindSceneDef("blackbeach"));
            yield return new WaitForSecondsRealtime(10f);
            Check("finale: no way out of Obliteration", Scene == "mysteryspace", $"scene={Scene}");
            yield return TestBeadsOfFealty();
        }

        // The host -> modded client message, written and read back with the mod's own code (a remote client can't be
        // run on one machine).
        private IEnumerator TestMarkerRoundTrip()
        {
            var network = Type.GetType("WhereToDie.ZoneNetwork, WhereToDie");
            var markers = Markers;
            var writer = (NetworkWriter)network.GetMethod("WriteMarkers", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { markers });
            var reader = new NetworkReader(writer.ToArray());
            reader.ReadUInt16(); // size
            ushort type = reader.ReadUInt16();
            var read = (IList)network.GetMethod("ReadMarkers", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { reader });
            bool same = read != null && read.Count == markers.Count;
            for (int i = 0; same && i < read.Count; i++)
            {
                same = read[i].Equals(markers[i]);
            }
            Check("marker message round trip (host -> modded client)", type == 0x5744 && same, $"type=0x{type:X} sent={markers.Count} read={read?.Count}");
            yield break;
        }

        // Walks into Terminal Lucidity's fight trigger and kills Providence's phases until the encounter is over.
        private IEnumerator FightProvidence()
        {
            var providence = ProvidenceEncounterController.instance;
            Check("Providence encounter found", providence);
            if (!providence) yield break;
            var trigger = providence.childLocator ? providence.childLocator.FindChild("FightTrigger") : null;
            if (trigger)
            {
                yield return TeleportTo(trigger.position, 2f);
            }
            else
            {
                Note("no FightTrigger; starting the encounter directly");
                providence.BeginEncounter();
            }
            EntityStateMachine.TryFindByCustomName(providence.gameObject, "Encounter", out var machine);
            float end = Time.realtimeSinceStartup + 420f;
            string last = "";
            while (Time.realtimeSinceStartup < end && !FindPortalMS())
            {
                string state = machine && machine.state != null ? machine.state.GetType().Name : "?";
                if (state != last)
                {
                    Note($"encounter state {state}");
                    last = state;
                }
                foreach (var body in CharacterBody.readOnlyInstancesList.ToList())
                {
                    if (body && body.isBoss && body.teamComponent && body.teamComponent.teamIndex == TeamIndex.Monster
                        && body.healthComponent && body.healthComponent.alive)
                    {
                        Note($"killing {body.GetDisplayName()} ({body.name})");
                        body.healthComponent.Suicide();
                    }
                }
                yield return new WaitForSecondsRealtime(1f);
            }
        }

        // Obliterating with Beads of Fealty on the finale path ends the run instead of opening A Moment, Whole.
        private IEnumerator TestBeadsOfFealty()
        {
            Inv.GiveItemPermanent(RoR2Content.Items.LunarTrinket, 1);
            var obelisk = FindObjectsOfType<EntityStateMachine>().FirstOrDefault(m => m && m.gameObject.name.Contains("Obelisk"));
            Check("Obliteration obelisk found", obelisk);
            if (!obelisk) yield break;
            obelisk.SetNextState(EntityStateCatalog.InstantiateState(typeof(EntityStates.Interactables.MSObelisk.EndingGame)));
            yield return WaitFor(() => Run.instance == null || Run.instance.isGameOverServer || Scene == "limbo", 90, "obliteration result");
            Check("beads: obliterating ends the run (no A Moment, Whole)", Scene != "limbo" && (Run.instance == null || Run.instance.isGameOverServer), $"scene={Scene}");
            yield return Shot("11_obliterated");
        }

        #region Showcase (make screenshots)

        // Looks from the player's eyes (model hidden, no HUD) at a point.
        private IEnumerator FirstPerson(string name, Vector3 target, float settle = 2f)
        {
            var body = Body;
            var rig = CameraRigController.readOnlyInstancesList.FirstOrDefault(r => r && r.localUserViewer == FirstUser);
            if (!body || !rig)
            {
                Note($"no body/camera for {name}");
                yield break;
            }
            var model = body.modelLocator ? body.modelLocator.modelTransform : null;
            if (model) model.gameObject.SetActive(false);
            var eye = body.corePosition + Vector3.up * 0.9f;
            var camera = new GameObject("WhereToDieTestCamera").AddComponent<FixedCamera>();
            camera.Position = eye;
            camera.Rotation = Quaternion.LookRotation(target - eye);
            camera.Hud = false;
            rig.SetOverrideCam(camera, 0f);
            yield return new WaitForSecondsRealtime(settle);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(outDir, name + ".png"));
            yield return new WaitForSecondsRealtime(0.3f);
            Note("screenshot " + name + ".png");
            rig.SetOverrideCam(null, 0f);
            Destroy(camera.gameObject);
            if (model) model.gameObject.SetActive(true);
            yield return new WaitForSecondsRealtime(0.5f);
        }

        // A ground node roughly `distance` metres (horizontally) from `center`, to stand on and look back from.
        private static Vector3? GroundNear(Vector3 center, float distance)
        {
            var nodes = SceneInfo.instance ? SceneInfo.instance.groundNodes : null;
            if (!nodes) return null;
            var found = nodes.FindNodesInRange(center, distance * 0.6f, distance * 1.4f, HullMask.Human);
            Vector3? best = null;
            float bestScore = float.MaxValue;
            foreach (var index in found)
            {
                if (!nodes.GetNodePosition(index, out var position)) continue;
                var flat = position - center;
                flat.y = 0f;
                float score = Mathf.Abs(flat.magnitude - distance) + Mathf.Abs(position.y - center.y) * 0.5f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = position;
                }
            }
            return best;
        }

        private static Vector3? NearestAurora(Vector3 to)
        {
            var auroras = AuroraPositions();
            return auroras.Count == 0 ? (Vector3?)null : auroras.OrderBy(p => (p - to).sqrMagnitude).First();
        }

        // Stand `distance` m from a place and look at its aurora (a little above the ground, so the curtain fills the frame).
        private IEnumerator LookAtAuroraFrom(string name, Vector3 place, float distance, float heightFraction = 0.35f)
        {
            var stand = GroundNear(place, distance);
            if (stand.HasValue)
            {
                yield return TeleportTo(stand.Value + Vector3.up, 2f);
            }
            var aurora = NearestAurora(place);
            var target = aurora.HasValue ? Vector3.Lerp(place, aurora.Value, heightFraction) : place + Vector3.up * 15f;
            yield return FirstPerson(name, target);
        }

        // Selects the Reincarnation page and marks the book as just used, so the HUD icons are fully opaque.
        private void ShowPageIcons()
        {
            var book = Body ? Body.GetComponent<RoR2.Items.CursedBookBodyBehavior>() : null;
            if (!book) return;
            for (int i = 0; i < book.GetSpellSlotCount(); i++)
            {
                if (book.GetSpellSlotFromIndex(i)?.spellIndex == DLC4Content.CursedBookSpells.Die.spellIndex)
                {
                    book.SelectSpell(i);
                }
            }
            book.UpdateTimeOfLastUse();
        }

        private IEnumerator RunShowcase()
        {
            Note("showcase mode");
            yield return WaitFor(() => RoR2Application.loadFinished && SceneCatalog.mostRecentSceneDef && SceneCatalog.mostRecentSceneDef.baseSceneName == "title", 300, "title screen");
            yield return new WaitForSecondsRealtime(3f);
            RoR2.Console.instance.SubmitCmd(null, "transition_command \"gamemode ClassicRun; host 0;\"");
            yield return WaitFor(() => PreGameController.instance && FirstUser?.currentNetworkUser, 90, "lobby");
            yield return new WaitForSecondsRealtime(3f);
            RoR2.Console.instance.SubmitCmd(null, "pregame_start_run");
            yield return WaitFor(() => Run.instance && Stage.instance && Body, 120, "run + body");
            yield return ExitPod();

            var book = DLC4Content.KeyItems.CursedBook;
            var pickupIndex = PickupCatalog.FindPickupIndex(book.keyItemIndex);
            var context = new PickupDef.GrantContext { body = Body, pickup = new UniquePickup(pickupIndex), position = Body.corePosition, rotation = Quaternion.identity, shouldNotify = true };
            PickupCatalog.GetPickupDef(pickupIndex).attemptGrant(ref context);
            yield return new WaitForSecondsRealtime(1.5f);
            Check("showcase: page given", Count(DLC4Content.Items.PageDie) == 1);

            // 1. The first 30 seconds of a stage: red aurora across the map, red icon.
            yield return WaitFor(() => AuroraCount() > 0 && IconTint() == true, 10, "stage aurora + icon");
            ShowPageIcons();
            yield return Shot("hud_icon_red");
            // Find a ground spot with open sky and a clear line of sight to one of the auroras, viewpoint there, look at it.
            Vector3? viewpoint = null, view = null;
            var nodes = SceneInfo.instance ? SceneInfo.instance.groundNodes : null;
            foreach (var aurora in AuroraPositions())
            {
                if (!nodes || viewpoint.HasValue) break;
                var groundUnder = Physics.Raycast(aurora, Vector3.down, out var down, 2000f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore)
                    ? down.point : new Vector3(aurora.x, Body.corePosition.y, aurora.z);
                var target = Vector3.Lerp(groundUnder, aurora, 0.4f);
                var candidates = nodes.FindNodesInRange(groundUnder, 50f, 140f, HullMask.Human);
                foreach (var index in candidates.OrderBy(_ => UnityEngine.Random.value).Take(60))
                {
                    if (!nodes.GetNodePosition(index, out var node)) continue;
                    var eyePoint = node + Vector3.up * 1.8f;
                    if (Physics.Raycast(eyePoint, Vector3.up, 60f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore)) continue;
                    if (Physics.Linecast(eyePoint, target, LayerIndex.world.mask, QueryTriggerInteraction.Ignore)) continue;
                    viewpoint = node;
                    view = target;
                    break;
                }
            }
            if (viewpoint.HasValue)
            {
                yield return TeleportTo(viewpoint.Value + Vector3.up, 2f);
                yield return FirstPerson("01_stage_aurora", view.Value);
            }
            else
            {
                Note("no open viewpoint onto a stage aurora found");
            }
            if (showcaseStageOnly)
            {
                yield return Finish();
                yield break;
            }

            // 2. Moon: red aurora on the rocketship roof, the ritual on the roof, the chat message.
            yield return GoTo("moon2");
            FindObjectOfType<EscapeSequenceController>()?.BeginEscapeSequence();
            yield return WaitFor(() => GameObject.Find("WhereToDie_RocketshipRoof") && AuroraCount() > 0, 10, "roof zone + aurora");
            var roof = GameObject.Find("WhereToDie_RocketshipRoof");
            if (roof)
            {
                yield return LookAtAuroraFrom("02_moon_roof_aurora", roof.transform.position, 70f, 0.2f);
                yield return TeleportTo(roof.transform.position + Vector3.up, 3f);
                if (!ReadyBuff) yield return TeleportTo(roof.transform.position + Vector3.up, 3f);
                yield return FirstPerson("03_moon_on_the_roof", roof.transform.position + Body.transform.forward * 15f + Vector3.up * 25f);
                if (ReadyBuff)
                {
                    godMode = false;
                    yield return null;
                    GameObject prefab = EntityStates.CursedBook.Spells.Die.ritualCirclePrefab;
                    var circle = Instantiate(prefab);
                    if (Physics.Raycast(Body.footPosition, Vector3.down, out var hit, 15000f, LayerIndex.world.mask))
                        circle.transform.SetPositionAndRotation(hit.point, Quaternion.identity);
                    NetworkServer.Spawn(circle);
                    // The ritual kills after 2 s: shoot right away, looking at the circle in front of the feet.
                    var ahead = Body.inputBank ? Body.inputBank.aimDirection : Body.transform.forward;
                    ahead.y = 0f;
                    yield return FirstPerson("04_moon_ritual", hit.point + ahead.normalized * 3f, 0.6f);
                    yield return WaitFor(() => ChatContains("Found a way to escape the Moon"), 15, "moon chat");
                    yield return Shot("hud_chat_moon");
                    yield return WaitFor(() => Scene == "champrush" && Body, 60, "moon -> threshold");
                    godMode = true;
                    yield return new WaitForSecondsRealtime(4f);
                }
            }

            // 3. Soulbound Threshold on the finale path: yellow aurora at the Veil, Obliteration portal instead of the way back.
            if (Scene != "champrush") yield return GoTo("champrush");
            Run.instance.SetEventFlag(FinaleFlag);
            // The Veil of Memory needs the champrush page minimum; hand out the other pages for the shot.
            foreach (var spell in new[] { DLC4Content.CursedBookSpells.Clarity, DLC4Content.CursedBookSpells.WarpGate, DLC4Content.CursedBookSpells.UpgradeItem })
            {
                if (spell && spell.itemDef) Inv.GiveItemPermanent(spell.itemDef, 1);
            }
            ChampRushMissionController.instance?.CompleteReaperFight();
            yield return WaitFor(() => FindPortalMS(), 10, "threshold Obliteration portal", false);
            yield return new WaitForSecondsRealtime(2f);
            Note("markers: " + DescribeMarkers());
            object veil = null;
            foreach (var m in Markers) if (!MarkerFlag(m, "Dies")) veil = m;
            if (veil != null)
            {
                var veilPosition = MarkerPosition(veil);
                yield return LookAtAuroraFrom("05_threshold_veil_aurora", veilPosition, 35f, 0.25f);
                yield return TeleportTo(veilPosition + Vector3.up, 3f);
                yield return WaitFor(() => IconTint() == false, 5, "yellow icon", false);
                ShowPageIcons();
                yield return Shot("hud_icon_yellow_row");
            }
            var portal = FindPortalMS();
            if (portal)
            {
                var stand = GroundNear(portal.transform.position, 14f) ?? portal.transform.position + new Vector3(10f, 0f, 10f);
                yield return TeleportTo(stand + Vector3.up, 2f);
                yield return FirstPerson("06_threshold_obliteration_portal", portal.transform.position + Vector3.up * 3f);
            }
            Run.instance.ResetEventFlag(FinaleFlag);

            // 4. Planetarium: red aurora over the final arena.
            yield return GoTo("voidraid");
            var gauntlet = VoidRaidGauntletController.instance;
            if (gauntlet)
            {
                var initial = GetField<VoidRaidGauntletController.DonutInfo>(gauntlet, "initialDonut");
                for (int i = 0; i < 6 && gauntlet.TryOpenGauntlet(initial.crabPosition.position, NetworkInstanceId.Invalid); i++) { }
                yield return new WaitForSecondsRealtime(2f);
                gauntlet.SpawnOutroPortal();
                yield return WaitFor(() => GameObject.Find("WhereToDie_PlanetariumFinale") && AuroraCount() > 0, 10, "planetarium aurora");
                var donut = GetField<VoidRaidGauntletController.DonutInfo>(gauntlet, "currentDonut");
                if (donut != null && donut.returnPoint)
                {
                    yield return TeleportTo(donut.returnPoint.position + Vector3.up, 3f);
                    var aurora = NearestAurora(Body.corePosition);
                    if (aurora.HasValue) yield return FirstPerson("07_planetarium_aurora", Vector3.Lerp(GameObject.Find("WhereToDie_PlanetariumFinale").transform.position, aurora.Value, 0.3f));
                }
            }

            // 5. Prime Meridian: the game's own zone at the altar, one aurora after the False Son.
            yield return GoTo("meridian");
            var meridian = FindObjectOfType<MeridianEventTriggerInteraction>();
            if (meridian)
            {
                yield return TeleportTo(meridian.transform.position + Vector3.up, 2f);
                float end = Time.realtimeSinceStartup + 360f;
                while (Time.realtimeSinceStartup < end && meridian.meridianEventState < MeridianEventState.Cleared)
                {
                    foreach (var b in CharacterBody.readOnlyInstancesList.ToList())
                        if (b && b.isBoss && b.teamComponent && b.teamComponent.teamIndex == TeamIndex.Monster && b.healthComponent && b.healthComponent.alive)
                            b.healthComponent.Suicide();
                    yield return new WaitForSecondsRealtime(1f);
                }
                yield return new WaitForSecondsRealtime(20f);
                var altar = FindObjectOfType<ShrineRebirthController>();
                if (altar)
                {
                    // From the middle of the arena, looking at the altar.
                    yield return TeleportTo(meridian.transform.position + Vector3.up, 3f);
                    var aurora = NearestAurora(altar.transform.position);
                    yield return FirstPerson("08_meridian_altar_aurora", aurora.HasValue ? Vector3.Lerp(altar.transform.position, aurora.Value, 0.3f) : altar.transform.position + Vector3.up * 15f);
                }
            }

            // 6. Terminal Lucidity on the finale path: Obliteration portal after Providence.
            Run.instance.SetEventFlag(FinaleFlag);
            yield return GoTo("ultima");
            yield return FightProvidence();
            var lucidityPortal = FindPortalMS();
            if (lucidityPortal)
            {
                yield return new WaitForSecondsRealtime(3f);
                var stand = GroundNear(lucidityPortal.transform.position, 14f) ?? lucidityPortal.transform.position + new Vector3(10f, 0f, 10f);
                yield return TeleportTo(stand + Vector3.up, 2f);
                yield return FirstPerson("09_lucidity_obliteration_portal", lucidityPortal.transform.position + Vector3.up * 3f);
            }
            yield return Finish();
        }

        #endregion

        private IEnumerator Finish()
        {
            if (finished) yield break;
            finished = true;
            RoR2.Console.instance.SubmitCmd(null, "wheretodie_status");
            Note($"exceptions logged during the run: {exceptions.Values.Sum()} ({exceptions.Count} distinct)");
            foreach (var pair in exceptions.OrderByDescending(p => p.Value).Take(8))
            {
                Note($"  {pair.Value}x {pair.Key}");
            }
            Check("no camera InvalidCastException", !exceptions.Keys.Any(k => k.Contains("InvalidCastException") && k.Contains("Camera")));
            Note($"DONE passed={passed} failed={failed}");
            yield return new WaitForSecondsRealtime(2f);
            Application.Quit();
        }
    }
}
