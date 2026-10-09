// Koikatsu Sunshine port of FreeHStudioSceneLoader.
// Modified for Koikatsu Sunshine, 2026. Licensed under GPL-3.0.
//
// Plays back the animation data that the studio plugins Timeline, KKPE (PoseEditor) and NodesConstraints keep in
// a scene, without those plugins (they only run in CharaStudio). The data formats, the interpolation, the order
// in which values are applied and the constraint math follow their source code:
//   HSPlugins - https://github.com/IllusionMods/HSPlugins
//   Copyright (c) 2021 DeathWeasel1337, based on HSPlugins by Joan6694. MIT License, see THIRD-PARTY-NOTICES.md.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Studio;
using UnityEngine;

namespace KKS_FreeHStudioSceneLoader
{
    /// <summary>
    /// Runs the scene's animation during H. Per frame:
    /// Update: Timeline (values set before the animator/IK run), then the constraints between guide objects.
    /// LateUpdate, in Studio's order (script execution orders, the same in the game and in Studio):
    /// - pose phase: KKPE bone and blend shape overrides. KKPE applies them after its IKExecutionOrder (9996), so
    ///   before IK (9999), which then solves the arms from the edited spine.
    /// - late phase: Timeline's "after" values, then all constraints. Both plugins run after the last
    ///   Expression.LateUpdate; the characters' ExpressionBone runs at 12150, after IK, the neck look (10100), FK
    ///   (10120) and the eye look (10800), and before the dynamic bones (12200/12250). Constraints use the final
    ///   pose (one test scene links the hands to the other character's spine after its IK; earlier, the wrists twist) and
    ///   hair simulates on the final head (later, it looks stiff).
    /// Hooks start each phase from the first LateUpdate of the frame that comes after it; the step at the start of
    /// PostLateUpdate is only a fallback, and must stay before the skinned meshes are updated.
    /// </summary>
    internal class SceneAnimator : MonoBehaviour
    {
        internal static SceneAnimator Instance { get; private set; }

        private readonly List<Track> _tracks = new List<Track>();
        private readonly Dictionary<Transform, PoseData> _poses = new Dictionary<Transform, PoseData>();
        private readonly List<Constraint> _constraints = new List<Constraint>();
        private readonly HashSet<GuideObject> _scaledGuides = new HashSet<GuideObject>();
        private float _duration;
        private float _speed = 1f;
        private float _time;

        internal static float DeltaTime { get; private set; }

        /// <summary>
        /// Builds the playback from the scene's plugin data. <paramref name="objects"/> are the scene's objects in
        /// the order Studio numbers them (sorted dicKey), null where an object is not placed here.
        /// </summary>
        internal static void Create(Transform overlayRoot, List<ObjectCtrlInfo> objects, List<bool> isItem, string timelineXml, string constraintsXml, string kkpeSceneXml)
        {
            Destroy();
            var go = new GameObject("FreeHStudioSceneLoader_Animation");
            var animator = go.AddComponent<SceneAnimator>();
            try
            {
                animator.Load(overlayRoot, objects, isItem, timelineXml, constraintsXml, kkpeSceneXml);
            }
            catch (Exception ex)
            {
                FreeHStudioSceneLoader.Logger.LogWarning($"Could not set up the studio plugin animation: {ex}");
            }
            if (animator._tracks.Count == 0 && animator._poses.Count == 0 && animator._constraints.Count == 0)
            {
                UnityEngine.Object.Destroy(go);
                return;
            }
            Instance = animator;
        }

        internal static void Destroy()
        {
            if (Instance != null) UnityEngine.Object.Destroy(Instance.gameObject);
            Instance = null;
        }

        private void Load(Transform overlayRoot, List<ObjectCtrlInfo> objects, List<bool> isItem, string timelineXml, string constraintsXml, string kkpeSceneXml)
        {
            if (FreeHStudioSceneLoader.ApplyPoseEditor.Value)
                LoadPoseEditor(objects, isItem, kkpeSceneXml);
            if (FreeHStudioSceneLoader.PlayTimeline.Value)
            {
                // Constraints first: Timeline refers to them by their position in the list
                if (!string.IsNullOrEmpty(constraintsXml)) LoadConstraints(overlayRoot, objects, constraintsXml);
                if (!string.IsNullOrEmpty(timelineXml)) LoadTimeline(objects, timelineXml);
            }
            FreeHStudioSceneLoader.Logger.LogInfo($"Studio plugin animation: {_tracks.Count} Timeline tracks ({_duration}s), {_poses.Count} KKPE poses, {_constraints.Count} constraints");
        }

        #region Per frame

        private void Update()
        {
            DeltaTime = Time.deltaTime;
            if (_tracks.Count > 0 && _duration > 0f)
            {
                _time = (_time + Time.deltaTime * _speed) % _duration;
                if (_time < 0f) _time += _duration;
                Interpolate(true);
            }
            RetryConstraints();
            // NodesConstraints: constraints involving guide objects are also applied right after Timeline
            foreach (var c in _constraints)
                c.ApplyEarly();
        }

        private static bool _loopInstalled;

        private struct AfterLateUpdate { }

        /// <summary>Adds <see cref="AfterLateUpdate"/> as the first step of PostLateUpdate.</summary>
        internal static void InstallPlayerLoopStep()
        {
            if (_loopInstalled) return;
            var loop = UnityEngine.LowLevel.PlayerLoop.GetCurrentPlayerLoop();
            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                if (loop.subSystemList[i].type != typeof(UnityEngine.PlayerLoop.PostLateUpdate)) continue;
                var steps = new List<UnityEngine.LowLevel.PlayerLoopSystem>(loop.subSystemList[i].subSystemList);
                steps.Insert(0, new UnityEngine.LowLevel.PlayerLoopSystem
                {
                    type = typeof(AfterLateUpdate),
                    updateDelegate = () =>
                    {
                        RunLatePhase();
                        Probe();
                    },
                });
                loop.subSystemList[i].subSystemList = steps.ToArray();
                UnityEngine.LowLevel.PlayerLoop.SetPlayerLoop(loop);
                _loopInstalled = true;
                return;
            }
            FreeHStudioSceneLoader.Logger.LogWarning("PostLateUpdate not found in the player loop; KKPE poses and constraints are not applied");
        }

        private void Awake() => InstallPlayerLoopStep();

        private static float _nextProbe;

        /// <summary>Diagnostics: the same numbers as the Studio-side probe, after every LateUpdate.</summary>
        private static void Probe()
        {
            if (!FreeHStudioSceneLoader.LogPoses.Value || Time.realtimeSinceStartup < _nextProbe) return;
            if (!StudioHost.HasPlacedCharacters || !Singleton<Studio.Studio>.IsInstance()) return;
            _nextProbe = Time.realtimeSinceStartup + 2f;
            PoseProbe.Log(FreeHStudioSceneLoader.Logger.LogInfo, "H", Instance != null ? Instance._time : 0f,
                Singleton<Studio.Studio>.Instance.dicObjectCtrl.Values.OfType<OCIChar>());
        }

        private int _poseFrame = -1, _lateFrame = -1;

        /// <summary>KKPE overrides, once per frame, before the first IK/neck look/FK LateUpdate.</summary>
        internal static void RunPosePhase()
        {
            var self = Instance;
            if (self == null || !self.isActiveAndEnabled || self._poseFrame == Time.frameCount) return;
            self._poseFrame = Time.frameCount;
            try
            {
                foreach (var pose in self._poses.Values) pose.Apply();
            }
            catch (Exception ex)
            {
                FreeHStudioSceneLoader.Logger.LogError($"Studio plugin animation failed and was stopped: {ex}");
                self.enabled = false;
            }
        }

        /// <summary>Timeline "after" values and constraints, once per frame, before the first dynamic bone LateUpdate.</summary>
        internal static void RunLatePhase()
        {
            var self = Instance;
            if (self == null || !self.isActiveAndEnabled || self._lateFrame == Time.frameCount) return;
            RunPosePhase();
            self._lateFrame = Time.frameCount;
            self.AfterAllLateUpdates();
        }

        private void AfterAllLateUpdates()
        {
            try
            {
                if (_tracks.Count > 0 && _duration > 0f) Interpolate(false);
                foreach (var guide in _scaledGuides) StudioHost.ApplyScale(guide);
                foreach (var c in _constraints) c.ApplyLate();
            }
            catch (Exception ex)
            {
                FreeHStudioSceneLoader.Logger.LogError($"Studio plugin animation failed and was stopped: {ex}");
                enabled = false;
            }
        }

        private void Interpolate(bool before)
        {
            foreach (var track in _tracks)
            {
                if (!track.Enabled) continue;
                var apply = before ? track.Before : track.After;
                if (apply == null) continue;
                // The keyframe at or before the current time and the one after it
                Key left = null, right = null;
                foreach (var key in track.Keys)
                {
                    if (key.Time <= _time) left = key;
                    else
                    {
                        right = key;
                        break;
                    }
                }
                try
                {
                    if (left != null && right != null)
                        apply(left.Value, right.Value, left.Curve.Evaluate((_time - left.Time) / (right.Time - left.Time)));
                    else if (left != null)
                        apply(left.Value, left.Value, 0f);
                    else if (right != null)
                        apply(right.Value, right.Value, 0f);
                }
                catch (Exception ex)
                {
                    // Like Timeline, drop a track that can no longer be applied (e.g. its object was destroyed)
                    track.Enabled = false;
                    FreeHStudioSceneLoader.Logger.LogWarning($"Timeline track {track.Name} stopped: {ex.Message}");
                }
            }
        }

        #endregion

        #region Timeline

        private class Key
        {
            public float Time;
            public object Value;
            public AnimationCurve Curve;
        }

        private class Track
        {
            public string Name;
            public bool Enabled = true;
            public Func<XmlNode, object> Read;
            public Action<object, object, float> Before;
            public Action<object, object, float> After;
            public readonly List<Key> Keys = new List<Key>();
        }

        private readonly Dictionary<string, int> _unsupported = new Dictionary<string, int>();

        private void LoadTimeline(List<ObjectCtrlInfo> objects, string xml)
        {
            var doc = new XmlDocument();
            doc.LoadXml(xml);
            var root = doc.DocumentElement;
            if (root == null) return;
            ReadTracks(root, objects);
            _duration = root.Attributes["duration"] != null ? XmlConvert.ToSingle(root.Attributes["duration"].Value) : 0f;
            if (_duration <= 0f)
                _duration = _tracks.SelectMany(t => t.Keys).Select(k => k.Time).DefaultIfEmpty(0f).Max();
            if (Mathf.Approximately(_duration, 0f)) _duration = 10f;
            // Timeline sets the game speed from the scene; only the playback speed is changed here
            _speed = root.Attributes["timeScale"] != null ? XmlConvert.ToSingle(root.Attributes["timeScale"].Value) : 1f;
            if (_unsupported.Count > 0)
                FreeHStudioSceneLoader.Logger.LogInfo("Timeline tracks not reproduced: " + string.Join(", ", _unsupported.Select(kv => $"{kv.Key} x{kv.Value}").ToArray()));
        }

        private void ReadTracks(XmlNode group, List<ObjectCtrlInfo> objects)
        {
            foreach (XmlNode node in group.ChildNodes)
            {
                if (node.Name == "interpolableGroup")
                {
                    ReadTracks(node, objects);
                    continue;
                }
                if (node.Name != "interpolable") continue;
                var owner = node.Attributes["owner"]?.Value;
                var id = node.Attributes["id"]?.Value;
                Track track = null;
                try
                {
                    ObjectCtrlInfo oci = null;
                    bool hasObject = node.Attributes["objectIndex"] != null;
                    if (hasObject)
                    {
                        int index = XmlConvert.ToInt32(node.Attributes["objectIndex"].Value);
                        if (index < 0 || index >= objects.Count) continue;
                        oci = objects[index];
                        // The object is not placed in H (camera, text, character turned off, failed to load)
                        if (oci == null)
                        {
                            FreeHStudioSceneLoader.Logger.LogInfo($"Timeline track {owner}/{id} skipped: object {index} is not placed");
                            continue;
                        }
                    }
                    track = CreateTrack(owner, id, oci, node);
                }
                catch (Exception ex)
                {
                    FreeHStudioSceneLoader.Logger.LogInfo($"Timeline track {owner}/{id} skipped: {ex.Message}");
                    continue;
                }
                if (track == null)
                {
                    var key = $"{owner}/{id}";
                    _unsupported[key] = _unsupported.TryGetValue(key, out var n) ? n + 1 : 1;
                    continue;
                }
                track.Name = $"{owner}/{id}";
                track.Enabled = node.Attributes["enabled"] == null || XmlConvert.ToBoolean(node.Attributes["enabled"].Value);
                foreach (XmlNode keyNode in node.ChildNodes)
                {
                    if (keyNode.Name != "keyframe") continue;
                    var curveKeys = new List<UnityEngine.Keyframe>();
                    foreach (XmlNode c in keyNode.ChildNodes)
                    {
                        if (c.Name != "curveKeyframe") continue;
                        curveKeys.Add(new UnityEngine.Keyframe(F(c, "time"), F(c, "value"), F(c, "inTangent"), F(c, "outTangent")));
                    }
                    track.Keys.Add(new Key
                    {
                        Time = F(keyNode, "time"),
                        Value = track.Read(keyNode),
                        Curve = curveKeys.Count == 0 ? AnimationCurve.Linear(0f, 0f, 1f, 1f) : new AnimationCurve(curveKeys.ToArray()),
                    });
                }
                // Timeline keeps keyframes sorted by time (and one per time)
                track.Keys.Sort((a, b) => a.Time.CompareTo(b.Time));
                if (track.Keys.Count > 0) _tracks.Add(track);
            }
        }

        private Track CreateTrack(string owner, string id, ObjectCtrlInfo oci, XmlNode node)
        {
            switch (owner)
            {
                case "Timeline": return CreateTimelineTrack(id, oci, node);
                case "KKPE": return FreeHStudioSceneLoader.ApplyPoseEditor.Value ? CreatePoseEditorTrack(id, oci, node) : null;
                case "NodesConstraints": return CreateConstraintTrack(id, node);
                default: return null;
            }
        }

        private Track CreateTimelineTrack(string id, ObjectCtrlInfo oci, XmlNode node)
        {
            switch (id)
            {
                case "guideObjectPos":
                case "guideObjectRot":
                case "guideObjectScale":
                {
                    if (oci?.guideObject?.transformTarget == null) return null;
                    var path = node.Attributes["guideObjectPath"]?.Value ?? "";
                    var target = path.Length == 0 ? oci.guideObject.transformTarget : oci.guideObject.transformTarget.Find(path);
                    if (target == null || !Singleton<GuideObjectManager>.Instance.dicGuideObject.TryGetValue(target, out var guide) || guide == null)
                        throw new InvalidOperationException($"guide object {path} not found");
                    if (id == "guideObjectPos")
                    {
                        Action<object, object, float> pos = (l, r, f) => guide.changeAmount.pos = Vector3.LerpUnclamped((Vector3)l, (Vector3)r, f);
                        return new Track { Read = n => V3(n, "value"), Before = pos, After = pos };
                    }
                    if (id == "guideObjectRot")
                    {
                        Action<object, object, float> rot = (l, r, f) => guide.changeAmount.rot = Quaternion.SlerpUnclamped((Quaternion)l, (Quaternion)r, f).eulerAngles;
                        return new Track { Read = n => Q(n, "value"), Before = rot, After = rot };
                    }
                    // Studio applies the scale of a guide object in its LateUpdate, which doesn't run here
                    _scaledGuides.Add(guide);
                    Action<object, object, float> scale = (l, r, f) => guide.changeAmount.scale = Vector3.LerpUnclamped((Vector3)l, (Vector3)r, f);
                    return new Track { Read = n => V3(n, "value"), Before = scale, After = scale };
                }
                case "objectEnabled":
                    if (oci == null || oci is OCILight) return null;
                    return new Track
                    {
                        Read = n => XmlConvert.ToBoolean(n.Attributes["value"].Value),
                        Before = (l, r, f) =>
                        {
                            if (oci.treeNodeObject != null && oci.treeNodeObject.visible != (bool)l) oci.treeNodeObject.visible = (bool)l;
                        },
                    };
                case "characterEyesOpen":
                    if (!(oci is OCIChar eyes)) return null;
                    return new Track { Read = n => F(n, "value"), Before = (l, r, f) => eyes.ChangeEyesOpen(Mathf.LerpUnclamped((float)l, (float)r, f)) };
                case "characterMouthOpen":
                    if (!(oci is OCIChar mouth)) return null;
                    return new Track { Read = n => F(n, "value"), Before = (l, r, f) => mouth.ChangeMouthOpen(Mathf.LerpUnclamped((float)l, (float)r, f)) };
                case "timeScale":
                    // Changes the game speed in Studio; only the playback speed here
                    return new Track { Read = n => F(n, "value"), Before = (l, r, f) => _speed = Mathf.LerpUnclamped((float)l, (float)r, f) };
                default:
                    // Studio camera, colors, lights, clothes, ... (not reproduced yet)
                    return null;
            }
        }

        private Track CreatePoseEditorTrack(string id, ObjectCtrlInfo oci, XmlNode node)
        {
            if (oci?.guideObject?.transformTarget == null) return null;
            // KKPE adds its controller to every character, but to an item only when it was edited
            if (!(oci is OCIChar) && !_poses.ContainsKey(oci.guideObject.transformTarget))
                throw new InvalidOperationException("the item has no KKPE data");
            var pose = GetPose(oci);
            switch (id)
            {
                case "bonePos":
                case "boneRot":
                case "boneScale":
                {
                    var bone = pose.Root.Find(node.Attributes["parameter"].Value);
                    if (bone == null) throw new InvalidOperationException("bone not found");
                    if (id == "bonePos") return new Track { Read = n => V3(n, "value"), Before = (l, r, f) => pose.SetPosition(bone, Vector3.LerpUnclamped((Vector3)l, (Vector3)r, f)) };
                    if (id == "boneRot") return new Track { Read = n => Q(n, "value"), Before = (l, r, f) => pose.SetRotation(bone, Quaternion.SlerpUnclamped((Quaternion)l, (Quaternion)r, f)) };
                    return new Track { Read = n => V3(n, "value"), Before = (l, r, f) => pose.SetScale(bone, Vector3.LerpUnclamped((Vector3)l, (Vector3)r, f)) };
                }
                case "lastBlendShape":
                {
                    var renderer = pose.Blend.Get(node.Attributes["parameter1"].Value);
                    int index = XmlConvert.ToInt32(node.Attributes["parameter2"].Value);
                    if (renderer == null || index < 0) throw new InvalidOperationException("blend shape renderer not found");
                    return new Track
                    {
                        Read = n => F(n, "value"),
                        Before = (l, r, f) =>
                        {
                            float weight = Mathf.LerpUnclamped((float)l, (float)r, f);
                            renderer.SetWeight(index, weight);
                            renderer.ApplyLink(weight, index);
                        },
                    };
                }
                case "groupBlendShape":
                case "groupBlendShapeLink":
                {
                    var renderer = pose.Blend.Get(node.Attributes["parameter"].Value);
                    if (renderer == null) throw new InvalidOperationException("blend shape renderer not found");
                    return new Track
                    {
                        Read = renderer.ReadGroupValue,
                        Before = (l, r, f) =>
                        {
                            var left = (float[])l;
                            var right = (float[])r;
                            for (int i = 0; i < left.Length && i < right.Length; i++)
                            {
                                float weight = Mathf.LerpUnclamped(left[i], right[i], f);
                                renderer.SetWeight(i, weight);
                                // "Link eyes components" is on by default in KKPE; the Link variant always links
                                renderer.ApplyLink(weight, i);
                            }
                        },
                    };
                }
                default:
                    // Dynamic bone gravity/force, boobs/butt physics, IK weights (not reproduced yet)
                    return null;
            }
        }

        private Track CreateConstraintTrack(string id, XmlNode node)
        {
            if (id != "constraintEnabled") return null;
            int loadId = XmlConvert.ToInt32(node.Attributes["parameter"].Value);
            var constraint = _constraints.FirstOrDefault(c => c.UniqueLoadId == loadId);
            if (constraint == null) throw new InvalidOperationException("constraint not found");
            constraint.UniqueLoadId = null;
            return new Track
            {
                Read = n => XmlConvert.ToBoolean(n.Attributes["value"].Value),
                Before = (l, r, f) =>
                {
                    if (constraint.enabled != (bool)l) constraint.SetEnabled((bool)l);
                },
            };
        }

        #endregion

        #region KKPE

        private PoseData GetPose(ObjectCtrlInfo oci)
        {
            var root = oci.guideObject.transformTarget;
            if (!_poses.TryGetValue(root, out var pose))
                _poses[root] = pose = new PoseData(oci, root);
            return pose;
        }

        /// <summary>
        /// KKPE keeps a character's edits in the card's extended data and the items' edits in the scene, for the
        /// n-th item of the scene. Only the bone and blend shape edits are reproduced.
        /// </summary>
        private void LoadPoseEditor(List<ObjectCtrlInfo> objects, List<bool> isItem, string sceneXml)
        {
            foreach (var oci in objects.OfType<OCIChar>())
            {
                try
                {
                    // KKPE puts its controller on every character, also without saved edits
                    var pose = GetPose(oci);
                    XmlNode info = null;
                    var data = ExtensibleSaveFormat.ExtendedSave.GetExtendedDataById(oci.charInfo.chaFile, "kkpe");
                    if (data?.data != null && data.data.TryGetValue("characterInfo", out var xml) && xml is string text)
                    {
                        var doc = new XmlDocument();
                        doc.LoadXml(text);
                        info = doc.DocumentElement;
                    }
                    if (info != null) pose.LoadXml(info);
                    foreach (var bone in pose.Static.Keys) FreeHStudioSceneLoader.Logger.LogDebug($"KKPE saved bone edit: {pose.Name}:{bone.name}");
                    // CharaPoseController.optimizeIK (on unless saved off)
                    bool optimizeIK = info?.Attributes?["optimizeIK"] == null || XmlConvert.ToBoolean(info.Attributes["optimizeIK"].Value);
                    if (optimizeIK && pose.Enabled) pose.OptimizeIK();
                }
                catch (Exception ex)
                {
                    FreeHStudioSceneLoader.Logger.LogWarning($"Could not read the KKPE pose of {oci.charInfo?.chaFile?.parameter?.fullname}: {ex.Message}");
                }
            }

            if (string.IsNullOrEmpty(sceneXml)) return;
            try
            {
                var doc = new XmlDocument();
                doc.LoadXml(sceneXml);
                var root = doc.DocumentElement;
                if (root == null || root.Name != "root") return;
                // KKPE gives the entries to the scene's items in order, which goes wrong when an item is missing (the
                // next item gets its data). The entries also carry the item's dicKey ("index"), which is used here.
                int i = 0;
                foreach (XmlNode node in root.ChildNodes)
                {
                    if (node.Name != "itemInfo") continue;
                    OCIItem item;
                    if (node.Attributes?["index"] != null)
                    {
                        int dicKey = XmlConvert.ToInt32(node.Attributes["index"].Value);
                        item = objects.OfType<OCIItem>().FirstOrDefault(o => o.objectInfo != null && o.objectInfo.dicKey == dicKey);
                    }
                    else
                    {
                        while (i < isItem.Count && !isItem[i]) i++;
                        if (i >= isItem.Count) break;
                        item = objects[i] as OCIItem;
                        i++;
                    }
                    if (item?.guideObject?.transformTarget != null)
                        GetPose(item).LoadXml(node);
                }
            }
            catch (Exception ex)
            {
                FreeHStudioSceneLoader.Logger.LogWarning($"Could not read the KKPE data of the scene's items: {ex.Message}");
            }
        }

        /// <summary>A KKPE PoseController: bone overrides and blend shape weights of one character or item.</summary>
        internal class PoseData
        {
            internal class BoneOverride
            {
                public Vector3? Position;
                public Quaternion? Rotation;
                public Vector3? Scale;
            }

            public readonly Transform Root;
            private readonly ObjectCtrlInfo _oci;
            /// <summary>KKPE's controller is on for characters and off for items unless the scene says otherwise.</summary>
            private bool _enabled;
            public bool Enabled => _enabled;
            /// <summary>Edits saved in the card/scene, and the ones Timeline animates (applied after them).</summary>
            internal readonly Dictionary<Transform, BoneOverride> Static = new Dictionary<Transform, BoneOverride>();
            internal readonly Dictionary<Transform, BoneOverride> Animated = new Dictionary<Transform, BoneOverride>();
            private BlendData _blend;

            public PoseData(ObjectCtrlInfo oci, Transform root)
            {
                _oci = oci;
                Root = root;
                _enabled = oci is OCIChar;
            }

            public string Name => _oci is OCIChar chara ? chara.charInfo?.chaFile?.parameter?.fullname?.Trim() : Root.name;

            internal BlendData Blend => _blend ?? (_blend = new BlendData(Root, _oci is OCIChar chara && chara.sex == 1));

            private static BoneOverride Get(Dictionary<Transform, BoneOverride> set, Transform bone)
            {
                if (!set.TryGetValue(bone, out var data)) set[bone] = data = new BoneOverride();
                return data;
            }

            public void SetPosition(Transform bone, Vector3 value) => Get(Animated, bone).Position = value;
            public void SetScale(Transform bone, Vector3 value) => Get(Animated, bone).Scale = value;

            public void SetRotation(Transform bone, Quaternion value)
            {
                // KKPE leaves bones that are posed with FK to FK
                if (_oci is OCIChar chara && chara.oiCharInfo.enableFK)
                {
                    var fk = chara.listBones.FirstOrDefault(b => b.guideObject != null && b.guideObject.transformTarget == bone);
                    if (fk != null && fk.active) return;
                }
                Get(Animated, bone).Rotation = value;
            }

            /// <summary>CharaPoseController.optimizeIK: a looser spine for the full body IK.</summary>
            internal void OptimizeIK()
            {
                if (!(_oci is OCIChar chara) || chara.finalIK == null) return;
                chara.finalIK.solver.spineStiffness = 0f;
                chara.finalIK.solver.pullBodyVertical = 0f;
            }

            public void LoadXml(XmlNode node)
            {
                if (node.Attributes?["enabled"] != null) _enabled = XmlConvert.ToBoolean(node.Attributes["enabled"].Value);
                var objects = node.SelectSingleNode("advancedObjects");
                if (objects != null)
                {
                    foreach (XmlNode o in objects.ChildNodes)
                    {
                        if (o.Name != "object" || o.Attributes["name"] == null) continue;
                        var bone = Root.Find(o.Attributes["name"].Value);
                        if (bone == null) continue;
                        if (o.Attributes["posX"] != null) Get(Static, bone).Position = V3(o, "pos");
                        if (o.Attributes["rotW"] != null) Get(Static, bone).Rotation = Q(o, "rot");
                        if (o.Attributes["scaleX"] != null) Get(Static, bone).Scale = V3(o, "scale");
                    }
                }
                var meshes = node.SelectSingleNode("skinnedMeshes");
                if (meshes != null) Blend.LoadXml(meshes, _oci as OCIChar);
            }

            private static void ApplySet(Dictionary<Transform, BoneOverride> set)
            {
                foreach (var kv in set)
                {
                    if (kv.Key == null) continue;
                    if (kv.Value.Scale.HasValue) kv.Key.localScale = kv.Value.Scale.Value;
                    if (kv.Value.Rotation.HasValue) kv.Key.localRotation = kv.Value.Rotation.Value;
                    if (kv.Value.Position.HasValue) kv.Key.localPosition = kv.Value.Position.Value;
                }
            }

            /// <summary>KKPE applies these before IK (characters) or in LateUpdate (items).</summary>
            public void Apply()
            {
                if (!_enabled) return;
                ApplySet(Static);
                ApplySet(Animated);
                _blend?.Apply();
            }
        }

        /// <summary>The folders and items that constraints link the given objects to (the parent end).</summary>
        internal List<ObjectCtrlInfo> LinkedParents(ICollection<ObjectCtrlInfo> children) =>
            _constraints.Where(c => c.enabled && c.childObject != null && children.Contains(c.childObject) &&
                                    c.parentObject != null && !(c.parentObject is OCIChar) && !children.Contains(c.parentObject))
                .Select(c => c.parentObject).Distinct().ToList();

        private static readonly string[] LinkSuffixKeys = { "L", "R", "CL", "OP", "HALF" };

        /// <summary>KKPE's blend shape editor: weights kept per renderer and written after the face update.</summary>
        internal class BlendData
        {
            private const string HeadKey = "ct_head";
            // Koikatsu: the first 28 face shapes are the eye components that the eye meshes follow
            private const int EyesComponentsCount = 28;
            private static readonly string[] HeadTags = { "cf_O_head", "cf_O_face" };
            private static readonly string[] LinkTags = { "cf_O_eyeline", "cf_O_eyeline_low", "cf_Ohitomi_L", "cf_Ohitomi_R", "cf_O_namida_L", "cf_O_namida_M", "cf_O_namida_S" };

            private readonly Dictionary<string, BlendRenderer> _renderers = new Dictionary<string, BlendRenderer>();

            public BlendData(Transform root, bool female)
            {
                // Renderers of this object, not of other studio objects attached to it
                var stack = new List<Transform> { root };
                while (stack.Count > 0)
                {
                    var t = stack[stack.Count - 1];
                    stack.RemoveAt(stack.Count - 1);
                    if (t != root && Singleton<GuideObjectManager>.Instance.dicGuideObject.ContainsKey(t) && IsObjectRoot(t)) continue;
                    var smr = t.GetComponent<SkinnedMeshRenderer>();
                    if (smr != null && smr.sharedMesh != null && smr.sharedMesh.blendShapeCount > 0)
                    {
                        var path = FixFullPath(GetPath(t, root));
                        if (!_renderers.ContainsKey(path)) _renderers[path] = new BlendRenderer(smr);
                    }
                    foreach (Transform child in t) stack.Add(child);
                }
                // Face and eye meshes at the same level are linked
                var groups = new Dictionary<string, List<BlendRenderer>>();
                foreach (var kv in _renderers)
                {
                    var name = kv.Value.Renderer.name;
                    if (!HeadTags.Contains(name) && !LinkTags.Contains(name)) continue;
                    var level = kv.Key.Substring(0, Math.Max(0, kv.Key.LastIndexOf('/')));
                    if (!groups.TryGetValue(level, out var list)) groups[level] = list = new List<BlendRenderer>();
                    list.Add(kv.Value);
                }
                foreach (var list in groups.Values)
                    foreach (var a in list)
                        foreach (var b in list)
                            if (a != b) a.Linked.Add(b);
                foreach (var r in _renderers.Values) r.CreateLinkKeynames(EyesComponentsCount);
            }

            private static bool IsObjectRoot(Transform t) =>
                Singleton<Studio.Studio>.Instance.dicObjectCtrl.Values.Any(o => o?.guideObject != null && o.guideObject.transformTarget == t);

            private static string FixFullPath(string path)
            {
                if (!path.Contains(HeadKey)) return path;
                return path.Substring(0, path.LastIndexOf(HeadKey)) + HeadKey + path.Substring(path.LastIndexOf('/'));
            }

            public BlendRenderer Get(string path) => _renderers.TryGetValue(FixFullPath(path), out var r) ? r : null;

            public void LoadXml(XmlNode meshes, OCIChar chara)
            {
                if (chara != null)
                {
                    if (meshes.Attributes["eyesPtn"] != null) chara.charInfo.ChangeEyesPtn(XmlConvert.ToInt32(meshes.Attributes["eyesPtn"].Value), false);
                    if (meshes.Attributes["eyesOpen"] != null) chara.ChangeEyesOpen(F(meshes, "eyesOpen"));
                    if (meshes.Attributes["mouthPtn"] != null) chara.charInfo.ChangeMouthPtn(XmlConvert.ToInt32(meshes.Attributes["mouthPtn"].Value), false);
                    if (meshes.Attributes["mouthOpen"] != null) chara.ChangeMouthOpen(F(meshes, "mouthOpen"));
                    if (meshes.Attributes["eyebrowsPtn"] != null) chara.charInfo.ChangeEyebrowPtn(XmlConvert.ToInt32(meshes.Attributes["eyebrowsPtn"].Value), false);
                    if (meshes.Attributes["eyebrowsOpen"] != null) chara.charInfo.ChangeEyebrowOpenMax(F(meshes, "eyebrowsOpen"));
                }
                foreach (XmlNode node in meshes.ChildNodes)
                {
                    if (node.Attributes?["name"] == null) continue;
                    var renderer = Get(node.Attributes["name"].Value);
                    if (renderer == null) continue;
                    foreach (XmlNode shape in node.ChildNodes)
                    {
                        if (shape.Attributes?["weight"] == null) continue;
                        float weight = F(shape, "weight");
                        if (shape.Attributes["blendName"] != null) renderer.SetWeight(shape.Attributes["blendName"].Value, weight);
                        else if (shape.Attributes["index"] != null) renderer.SetWeight(XmlConvert.ToInt32(shape.Attributes["index"].Value), weight);
                    }
                }
            }

            public void Apply()
            {
                foreach (var r in _renderers.Values) r.Apply();
            }
        }

        internal class BlendRenderer
        {
            public readonly SkinnedMeshRenderer Renderer;
            public readonly List<BlendRenderer> Linked = new List<BlendRenderer>();
            private readonly List<string> _names = new List<string>();
            private readonly Dictionary<string, int> _indices = new Dictionary<string, int>();
            private readonly Dictionary<string, float> _dirty = new Dictionary<string, float>();
            private readonly Dictionary<string, int> _linkKeys = new Dictionary<string, int>();
            private readonly List<string> _linkKeyList = new List<string>();

            public BlendRenderer(SkinnedMeshRenderer renderer)
            {
                Renderer = renderer;
                var mesh = renderer.sharedMesh;
                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
                    var name = mesh.GetBlendShapeName(i);
                    _indices[name] = i;
                    _names.Add(name);
                }
            }

            /// <summary>Keys that match the eye components between the face and the eye meshes (KKPE CreateLinkKeynames).</summary>
            public void CreateLinkKeynames(int eyesComponentsCount)
            {
                _linkKeys.Clear();
                _linkKeyList.Clear();
                if (Linked.Count == 0) return;
                int index = 0;
                foreach (var blendName in _names)
                {
                    if (index >= eyesComponentsCount) break;
                    string number = null;
                    int start = -1, end = -1;
                    for (int i = 0; i < blendName.Length; i++)
                    {
                        bool digit = blendName[i] >= '0' && blendName[i] <= '9';
                        if (start == -1) { if (digit) start = i; }
                        else if (!digit) { end = i - 1; break; }
                    }
                    if (start != -1 && end != -1) number = blendName.Substring(start, end - start + 1);
                    int suffix = blendName.LastIndexOf('_');
                    string mainKey = blendName.Substring(suffix + 1).ToUpper();
                    if (LinkSuffixKeys.Contains(mainKey))
                    {
                        string prefix = blendName.Substring(0, suffix);
                        suffix = prefix.LastIndexOf('_');
                        mainKey = blendName.Substring(suffix + 1).ToUpper();
                    }
                    var key = number != null ? mainKey + number : mainKey;
                    if (!_linkKeys.ContainsKey(key)) _linkKeys.Add(key, index);
                    _linkKeyList.Add(key);
                    index++;
                }
            }

            public void SetWeight(int index, float weight)
            {
                if (index >= 0 && index < _names.Count) _dirty[_names[index]] = weight;
            }

            public void SetWeight(string name, float weight)
            {
                if (_indices.ContainsKey(name)) _dirty[name] = weight;
            }

            public void ApplyLink(float weight, int index)
            {
                if (Linked.Count == 0 || index >= _linkKeyList.Count) return;
                var key = _linkKeyList[index];
                foreach (var other in Linked)
                    if (other._linkKeys.TryGetValue(key, out int i)) other.SetWeight(i, weight);
            }

            public object ReadGroupValue(XmlNode node)
            {
                int count = XmlConvert.ToInt32(node.Attributes["valueCount"].Value);
                var value = new float[_names.Count];
                for (int i = 0; i < count; i++)
                {
                    float v = F(node, $"value{i}");
                    var name = node.Attributes[$"name{i}"]?.Value;
                    // Matched by name when the mesh changed since the scene was saved
                    int index = !string.IsNullOrEmpty(name) && count != _names.Count ? (_indices.TryGetValue(name, out var n) ? n : -1) : i;
                    if (index >= 0 && index < value.Length) value[index] = v;
                }
                return value;
            }

            public void Apply()
            {
                if (Renderer == null) return;
                foreach (var kv in _dirty)
                    Renderer.SetBlendShapeWeight(_indices[kv.Key], kv.Value);
            }
        }

        #endregion

        #region NodesConstraints

        // Bones added after the character was created (uncensor and penetration plugins) are looked up again
        private readonly List<XmlNode> _missingConstraints = new List<XmlNode>();
        private List<ObjectCtrlInfo> _objects;
        private Transform _overlayRoot;
        private float _retryUntil, _nextRetry;

        private void LoadConstraints(Transform overlayRoot, List<ObjectCtrlInfo> objects, string xml)
        {
            var doc = new XmlDocument();
            doc.LoadXml(xml);
            var root = doc.DocumentElement;
            if (root == null) return;
            _objects = objects;
            _overlayRoot = overlayRoot;
            foreach (XmlNode node in root.ChildNodes)
            {
                if (node.Attributes?["parentObjectIndex"] == null) continue;
                if (!TryAddConstraint(node, out var reason, out var retry) && retry)
                    _missingConstraints.Add(node);
                if (reason != null)
                    FreeHStudioSceneLoader.Logger.LogInfo($"Constraint {node.Attributes["uniqueLoadId"]?.Value} skipped{(retry ? " for now" : "")}: {reason}");
            }
            _retryUntil = Time.realtimeSinceStartup + 15f;
        }

        private bool TryAddConstraint(XmlNode node, out string reason, out bool retry)
        {
            retry = false;
            try
            {
                var c = Constraint.Load(node, _objects, _overlayRoot, out reason, out retry);
                if (c == null) return false;
                // NodesConstraints skips a second constraint between the same two transforms
                if (_constraints.Any(o => (o.parentTransform == c.parentTransform && o.childTransform == c.childTransform) ||
                                          (o.parentTransform == c.childTransform && o.childTransform == c.parentTransform)))
                    return true;
                _constraints.Add(c);
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return false;
            }
        }

        private void RetryConstraints()
        {
            if (_missingConstraints.Count == 0 || Time.realtimeSinceStartup < _nextRetry) return;
            _nextRetry = Time.realtimeSinceStartup + 0.5f;
            bool last = Time.realtimeSinceStartup > _retryUntil;
            for (int i = _missingConstraints.Count - 1; i >= 0; i--)
            {
                var node = _missingConstraints[i];
                if (TryAddConstraint(node, out var reason, out _))
                {
                    _missingConstraints.RemoveAt(i);
                    FreeHStudioSceneLoader.Logger.LogInfo($"Constraint {node.Attributes["uniqueLoadId"]?.Value} added");
                }
                else if (last)
                    FreeHStudioSceneLoader.Logger.LogInfo($"Constraint {node.Attributes["uniqueLoadId"]?.Value} not added: {reason}");
            }
            if (last) _missingConstraints.Clear();
        }

        // Ported from NodesConstraints.Constraint (HSPlugins, MIT): same fields and math. World positions saved by
        // Studio are converted to the H scene, where the studio scene is placed under the overlay root.
        private class Constraint
        {
            public class TransformLock
            {
                public bool x = true, y = true, z = true;
                public TransformLock() { }
                public TransformLock(bool x, bool y, bool z) { this.x = x; this.y = y; this.z = z; }
            }

            private struct Smooth<T>
            {
                public float connectionTime;
                public float disConnectionTime;
                public float connectionCurrentTime;
                public float disConnectionCurrentTime;
                public T smoothStartValue;
            }

            public bool enabled = true;
            public TransformLock positionLocks = new TransformLock();
            public TransformLock rotationLocks = new TransformLock();
            public TransformLock scaleLocks = new TransformLock();
            public GuideObject parent;
            public Transform parentTransform;
            public GuideObject child;
            public Transform childTransform;
            /// <summary>The scene objects the two ends belong to.</summary>
            public ObjectCtrlInfo parentObject, childObject;
            public bool position = true;
            public bool rotation = true;
            public bool lookAt;
            public bool scale = true;
            public bool mirrorPosition;
            public bool mirrorRotation;
            public bool mirrorScale;
            public float positionChangeFactor = 1;
            public float rotationChangeFactor = 1;
            public float scaleChangeFactor = 1;
            public float positionDamp;
            public float rotationDamp;
            public float scaleDamp;
            public bool resetOriginalPosition = true;
            public bool resetOriginalRotation = true;
            public bool resetOriginalScale = true;
            private Smooth<Vector3> smoothPos;
            private Smooth<Quaternion> smoothRot;
            private Smooth<Vector3> smoothScale;
            public Vector3 positionOffset = Vector3.zero;
            public Quaternion rotationOffset = Quaternion.identity;
            public Vector3 scaleOffset = Vector3.one;
            public Vector3 originalChildPosition;
            public Quaternion originalChildRotation;
            public Vector3 originalChildScale;
            public bool fixDynamicBone;
            public int? UniqueLoadId;
            public Vector3 originalParentPosition;
            public Quaternion originalParentRotation;
            public Vector3 originalParentScale;

            private static bool B(XmlNode n, string name) => XmlConvert.ToBoolean(n.Attributes[name].Value);
            private static bool Has(XmlNode n, string name) => n.Attributes[name] != null;

            public static Constraint Load(XmlNode n, List<ObjectCtrlInfo> objects, Transform overlayRoot, out string reason, out bool retry)
            {
                retry = false;
                int parentIndex = XmlConvert.ToInt32(n.Attributes["parentObjectIndex"].Value);
                // -2: the studio camera, which doesn't exist here
                reason = $"parent object {parentIndex} is not placed";
                if (parentIndex < 0 || parentIndex >= objects.Count || objects[parentIndex] == null) return null;
                var parentT = objects[parentIndex].guideObject.transformTarget.Find(n.Attributes["parentPath"].Value);
                reason = $"parent {n.Attributes["parentPath"].Value} not found";
                retry = true;
                if (parentT == null) return null;
                retry = false;
                int childIndex = XmlConvert.ToInt32(n.Attributes["childObjectIndex"].Value);
                reason = $"child object {childIndex} is not placed";
                if (childIndex < 0 || childIndex >= objects.Count || objects[childIndex] == null) return null;
                var childRoot = objects[childIndex].guideObject.transformTarget;
                var childT = Has(n, "childPath") ? childRoot.Find(n.Attributes["childPath"].Value) : FindDescendant(childRoot, n.Attributes["childName"].Value);
                reason = $"child {n.Attributes["childPath"]?.Value ?? n.Attributes["childName"]?.Value} not found";
                retry = true;
                if (childT == null) return null;
                retry = false;
                reason = null;

                var c = new Constraint
                {
                    enabled = !Has(n, "enabled") || B(n, "enabled"),
                    parentTransform = parentT,
                    childTransform = childT,
                    parentObject = objects[parentIndex],
                    childObject = objects[childIndex],
                    position = B(n, "position"),
                    positionOffset = V3(n, "positionOffset"),
                    rotation = B(n, "rotation"),
                    rotationOffset = Q(n, "rotationOffset"),
                    scale = Has(n, "scale") && B(n, "scale"),
                    scaleOffset = Has(n, "scaleOffsetX") ? V3(n, "scaleOffset") : Vector3.one,
                };
                var guides = Singleton<GuideObjectManager>.Instance.dicGuideObject;
                guides.TryGetValue(parentT, out c.parent);
                guides.TryGetValue(childT, out c.child);
                c.originalChildPosition = childT.localPosition;
                c.originalChildRotation = childT.localRotation;
                c.originalChildScale = childT.localScale;
                c.originalParentPosition = parentT.position;
                c.originalParentRotation = parentT.rotation;
                c.originalParentScale = parentT.lossyScale;

                if (Has(n, "uniqueLoadId")) c.UniqueLoadId = XmlConvert.ToInt32(n.Attributes["uniqueLoadId"].Value);
                if (Has(n, "dynamic")) c.fixDynamicBone = B(n, "dynamic");
                if (Has(n, "mirrorPosition")) c.mirrorPosition = B(n, "mirrorPosition");
                if (Has(n, "mirrorRotation")) c.mirrorRotation = B(n, "mirrorRotation");
                if (Has(n, "mirrorScale")) c.mirrorScale = B(n, "mirrorScale");
                if (Has(n, "lookAt")) c.lookAt = B(n, "lookAt");
                // Saved in Studio's world space
                if (Has(n, "originalParentPositionX")) c.originalParentPosition = overlayRoot.TransformPoint(V3(n, "originalParentPosition"));
                if (Has(n, "originalParentRotationX")) c.originalParentRotation = overlayRoot.rotation * Q(n, "originalParentRotation");
                if (Has(n, "originalParentScaleX")) c.originalParentScale = V3(n, "originalParentScale");
                if (Has(n, "positionChangeFactor"))
                {
                    c.positionChangeFactor = F(n, "positionChangeFactor");
                    c.rotationChangeFactor = F(n, "rotationChangeFactor");
                    c.scaleChangeFactor = F(n, "scaleChangeFactor");
                    c.positionDamp = F(n, "positionDamp");
                    c.rotationDamp = F(n, "rotationDamp");
                    c.scaleDamp = F(n, "scaleDamp");
                    c.positionLocks = new TransformLock(B(n, "positionLocksX"), B(n, "positionLocksY"), B(n, "positionLocksZ"));
                    c.rotationLocks = new TransformLock(B(n, "rotationLocksX"), B(n, "rotationLocksY"), B(n, "rotationLocksZ"));
                    c.scaleLocks = new TransformLock(B(n, "scaleLocksX"), B(n, "scaleLocksY"), B(n, "scaleLocksZ"));
                }
                if (Has(n, "posSmoothConnectionTime"))
                {
                    c.smoothPos.connectionTime = F(n, "posSmoothConnectionTime");
                    c.smoothPos.disConnectionTime = F(n, "posSmoothDisConnectionTime");
                    c.smoothRot.connectionTime = F(n, "rotSmoothConnectionTime");
                    c.smoothRot.disConnectionTime = F(n, "rotSmoothDisConnectionTime");
                    c.smoothScale.connectionTime = F(n, "scaleSmoothConnectionTime");
                    c.smoothScale.disConnectionTime = F(n, "scaleSmoothDisConnectionTime");
                }
                if (Has(n, "resetOriginalPosition"))
                {
                    c.resetOriginalPosition = B(n, "resetOriginalPosition");
                    c.resetOriginalRotation = B(n, "resetOriginalRotation");
                    c.resetOriginalScale = B(n, "resetOriginalScale");
                }
                return c;
            }

            private bool Alive => parentTransform != null && childTransform != null;

            private float GetInterpolationFactor(float damp) => 1 - Mathf.Exp(-damp * DeltaTime);
            private Vector3 GetPositionMovement() => parentTransform.position - originalParentPosition;
            private Quaternion GetRotationChange() => Quaternion.Inverse(originalParentRotation) * parentTransform.rotation;

            /// <summary>NodesConstraints.ApplyNodesConstraints: after Timeline, for constraints with a guide object.</summary>
            public void ApplyEarly()
            {
                if (!Alive || (child == null && parent == null)) return;
                if (enabled)
                {
                    UpdateSmoothConnectTimes();
                    if (position && (child == null || child.enablePos)) UpdatePosition();
                    if (rotation && (child == null || child.enableRot)) UpdateRotation();
                    if (scale && (child == null || child.enableScale)) UpdateScale();
                }
                else
                {
                    SmoothDisconnectUpdate(true);
                }
            }

            /// <summary>NodesConstraints.ApplyConstraints: after everything else.</summary>
            public void ApplyLate()
            {
                if (!Alive) return;
                if (!enabled)
                {
                    SmoothDisconnectUpdate(child == null && parent == null);
                    return;
                }
                if (fixDynamicBone) return;
                if (position) UpdatePosition();
                if (rotation) UpdateRotation();
                if (scale) UpdateScale();
            }

            private void UpdateSmoothConnectTimes()
            {
                Tick(ref smoothPos.connectionTime, ref smoothPos.connectionCurrentTime);
                Tick(ref smoothRot.connectionTime, ref smoothRot.connectionCurrentTime);
                Tick(ref smoothScale.connectionTime, ref smoothScale.connectionCurrentTime);
            }

            private static void Tick(ref float time, ref float current)
            {
                if (time > 0f && current > 0f)
                {
                    if (current > time) current = time;
                    current -= DeltaTime;
                    if (current < 0) current = 0;
                }
            }

            private Vector3 TargetPosition()
            {
                Vector3 targetPos = mirrorPosition ? -GetPositionMovement() * (positionChangeFactor + 1) : GetPositionMovement() * (positionChangeFactor - 1);
                targetPos = parentTransform.TransformPoint(targetPos + positionOffset);
                if (!positionLocks.x) targetPos.x = childTransform.position.x;
                if (!positionLocks.y) targetPos.y = childTransform.position.y;
                if (!positionLocks.z) targetPos.z = childTransform.position.z;
                return targetPos;
            }

            private Quaternion TargetRotation()
            {
                Quaternion targetRot;
                if (lookAt)
                {
                    var look = Quaternion.LookRotation(parentTransform.position - childTransform.position);
                    targetRot = mirrorRotation ? new Quaternion(-look.x, -look.y, look.z, look.w) : look;
                }
                else if (mirrorRotation)
                    targetRot = originalParentRotation * Quaternion.Inverse(GetRotationChange());
                else
                    targetRot = originalParentRotation * GetRotationChange();
                targetRot *= rotationOffset;
                return Quaternion.SlerpUnclamped(Quaternion.identity, targetRot, rotationChangeFactor);
            }

            private Quaternion LockRotation(Quaternion targetRot) => Quaternion.Euler(
                rotationLocks.x ? targetRot.eulerAngles.x : childTransform.rotation.eulerAngles.x,
                rotationLocks.y ? targetRot.eulerAngles.y : childTransform.rotation.eulerAngles.y,
                rotationLocks.z ? targetRot.eulerAngles.z : childTransform.rotation.eulerAngles.z);

            private Vector3 TargetScale()
            {
                float e = mirrorScale ? -scaleChangeFactor : scaleChangeFactor;
                var parentScale = childTransform.parent.lossyScale;
                var target = new Vector3(
                    originalParentScale.x * scaleOffset.x / parentScale.x * Mathf.Pow(parentTransform.lossyScale.x / originalParentScale.x, e),
                    originalParentScale.y * scaleOffset.y / parentScale.y * Mathf.Pow(parentTransform.lossyScale.y / originalParentScale.y, e),
                    originalParentScale.z * scaleOffset.z / parentScale.z * Mathf.Pow(parentTransform.lossyScale.z / originalParentScale.z, e));
                if (!scaleLocks.x) target.x = childTransform.localScale.x;
                if (!scaleLocks.y) target.y = childTransform.localScale.y;
                if (!scaleLocks.z) target.z = childTransform.localScale.z;
                return target;
            }

            private void UpdatePosition()
            {
                var targetPos = TargetPosition();
                if (smoothPos.connectionTime > 0f && smoothPos.connectionCurrentTime > 0f)
                    targetPos = Vector3.Lerp(smoothPos.smoothStartValue, targetPos, Mathf.Clamp01(1f - smoothPos.connectionCurrentTime / smoothPos.connectionTime));
                if (positionDamp > 0) targetPos = Vector3.Lerp(childTransform.position, targetPos, GetInterpolationFactor(positionDamp));
                childTransform.position = targetPos;
                if (child != null) child.changeAmount.pos = child.transformTarget.localPosition;
            }

            private void UpdateRotation()
            {
                var targetRot = TargetRotation();
                if (smoothRot.connectionTime > 0f && smoothRot.connectionCurrentTime > 0f)
                    targetRot = Quaternion.Slerp(smoothRot.smoothStartValue, targetRot, Mathf.Clamp01(1f - smoothRot.connectionCurrentTime / smoothRot.connectionTime));
                targetRot = LockRotation(targetRot);
                if (rotationDamp > 0) targetRot = Quaternion.Slerp(childTransform.rotation, targetRot, GetInterpolationFactor(rotationDamp));
                childTransform.rotation = targetRot;
                if (child != null) child.changeAmount.rot = child.transformTarget.localEulerAngles;
            }

            private void UpdateScale()
            {
                if (childTransform.parent == null) return;
                var targetScale = TargetScale();
                if (smoothScale.connectionTime > 0f && smoothScale.connectionCurrentTime > 0f)
                    targetScale = Vector3.Lerp(smoothScale.smoothStartValue, targetScale, Mathf.Clamp01(1f - smoothScale.connectionCurrentTime / smoothScale.connectionTime));
                if (scaleDamp > 0) targetScale = Vector3.Lerp(childTransform.localScale, targetScale, GetInterpolationFactor(scaleDamp));
                childTransform.localScale = targetScale;
                if (child != null) child.changeAmount.scale = child.transformTarget.localScale;
            }

            private void SmoothDisconnectUpdate(bool updateTimer)
            {
                if (position && smoothPos.disConnectionTime > 0f && smoothPos.disConnectionCurrentTime > 0f)
                {
                    var targetPos = TargetPosition();
                    float t = DisconnectFactor(ref smoothPos.disConnectionTime, ref smoothPos.disConnectionCurrentTime, updateTimer);
                    if (resetOriginalPosition)
                    {
                        targetPos = childTransform.parent != null ? childTransform.parent.TransformPoint(originalChildPosition) : originalChildPosition;
                        childTransform.position = Vector3.Lerp(smoothPos.smoothStartValue, targetPos, t);
                        if (t >= 1f) childTransform.localPosition = originalChildPosition;
                    }
                    else
                        childTransform.position = Vector3.Lerp(targetPos, childTransform.position, t);
                    if (child != null) child.changeAmount.pos = child.transformTarget.localPosition;
                }
                if (rotation && smoothRot.disConnectionTime > 0f && smoothRot.disConnectionCurrentTime > 0f)
                {
                    var targetRot = TargetRotation();
                    float t = DisconnectFactor(ref smoothRot.disConnectionTime, ref smoothRot.disConnectionCurrentTime, updateTimer);
                    if (resetOriginalRotation)
                    {
                        targetRot = childTransform.parent != null ? childTransform.parent.rotation * originalChildRotation : originalChildRotation;
                        childTransform.rotation = Quaternion.Slerp(smoothRot.smoothStartValue, targetRot, t);
                        if (t >= 1f) childTransform.localRotation = originalChildRotation;
                    }
                    else
                        childTransform.rotation = LockRotation(Quaternion.Slerp(targetRot, childTransform.rotation, t));
                    if (child != null) child.changeAmount.rot = child.transformTarget.localEulerAngles;
                }
                if (scale && smoothScale.disConnectionTime > 0f && smoothScale.disConnectionCurrentTime > 0f && childTransform.parent != null)
                {
                    var targetScale = TargetScale();
                    float t = DisconnectFactor(ref smoothScale.disConnectionTime, ref smoothScale.disConnectionCurrentTime, updateTimer);
                    if (resetOriginalScale)
                    {
                        childTransform.localScale = Vector3.Lerp(smoothScale.smoothStartValue, originalChildScale, t);
                        if (t >= 1f) childTransform.localScale = originalChildScale;
                    }
                    else
                        childTransform.localScale = Vector3.Lerp(targetScale, childTransform.localScale, t);
                    if (child != null) child.changeAmount.scale = child.transformTarget.localScale;
                }
            }

            private static float DisconnectFactor(ref float time, ref float current, bool updateTimer)
            {
                if (updateTimer)
                {
                    current -= DeltaTime;
                    if (current < 0) current = 0;
                }
                if (current > time) current = time;
                return Mathf.Clamp01(1f - current / time);
            }

            /// <summary>NodesConstraints.SetConstraintEnabled (toggled by Timeline).</summary>
            public void SetEnabled(bool newEnabled)
            {
                if (enabled && !newEnabled)
                {
                    if (position)
                    {
                        smoothPos.disConnectionCurrentTime = smoothPos.disConnectionTime;
                        smoothPos.connectionCurrentTime = 0f;
                        smoothPos.smoothStartValue = childTransform.position;
                        if (resetOriginalPosition && smoothPos.disConnectionTime <= 0f)
                        {
                            childTransform.localPosition = originalChildPosition;
                            if (child != null) child.changeAmount.pos = originalChildPosition;
                        }
                    }
                    if (rotation)
                    {
                        smoothRot.disConnectionCurrentTime = smoothRot.disConnectionTime;
                        smoothRot.connectionCurrentTime = 0f;
                        smoothRot.smoothStartValue = childTransform.rotation;
                        if (resetOriginalRotation && smoothRot.disConnectionTime <= 0f)
                        {
                            childTransform.localRotation = originalChildRotation;
                            if (child != null) child.changeAmount.rot = originalChildRotation.eulerAngles;
                        }
                    }
                    if (scale)
                    {
                        smoothScale.disConnectionCurrentTime = smoothScale.disConnectionTime;
                        smoothScale.connectionCurrentTime = 0f;
                        smoothScale.smoothStartValue = childTransform.localScale;
                        if (resetOriginalScale && smoothScale.disConnectionTime <= 0f)
                        {
                            childTransform.localScale = originalChildScale;
                            if (child != null) child.changeAmount.scale = originalChildScale;
                        }
                    }
                }
                if (!enabled && newEnabled)
                {
                    originalChildPosition = childTransform.localPosition;
                    originalChildRotation = childTransform.localRotation;
                    originalChildScale = childTransform.localScale;
                    if (position)
                    {
                        smoothPos.connectionCurrentTime = smoothPos.connectionTime;
                        smoothPos.smoothStartValue = childTransform.position;
                        smoothPos.disConnectionCurrentTime = 0f;
                    }
                    if (rotation)
                    {
                        smoothRot.connectionCurrentTime = smoothRot.connectionTime;
                        smoothRot.smoothStartValue = childTransform.rotation;
                        smoothRot.disConnectionCurrentTime = 0f;
                    }
                    if (scale)
                    {
                        smoothScale.connectionCurrentTime = smoothScale.connectionTime;
                        smoothScale.smoothStartValue = childTransform.localScale;
                        smoothScale.disConnectionCurrentTime = 0f;
                    }
                }
                enabled = newEnabled;
            }
        }

        #endregion

        #region Helpers

        private static float F(XmlNode n, string name) => XmlConvert.ToSingle(n.Attributes[name].Value);
        private static Vector3 V3(XmlNode n, string prefix) => new Vector3(F(n, prefix + "X"), F(n, prefix + "Y"), F(n, prefix + "Z"));
        private static Quaternion Q(XmlNode n, string prefix) => new Quaternion(F(n, prefix + "X"), F(n, prefix + "Y"), F(n, prefix + "Z"), F(n, prefix + "W"));

        private static string GetPath(Transform t, Transform root)
        {
            var parts = new List<string>();
            for (; t != null && t != root; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                var found = FindDescendant(child, name);
                if (found != null) return found;
            }
            return null;
        }

        #endregion
    }
}
