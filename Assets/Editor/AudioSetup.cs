using System.Collections.Generic;
using System.Linq;
using CoalRun.Audio;
using UnityEditor;
using UnityEngine;

namespace CoalRun.Editor
{
    // Builds the AudioDirector's cue and bed lists from the clips in Assets/Audio, so the mix is one table in one place.
    public static class AudioSetup
    {
        private static AudioClip[] Clips(string prefix)
        {
            var list = new List<AudioClip>();
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Audio" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var file = System.IO.Path.GetFileNameWithoutExtension(path);
                if (file.StartsWith(prefix)) list.Add(AssetDatabase.LoadAssetAtPath<AudioClip>(path));
            }
            return list.OrderBy(c => c.name).ToArray();
        }

        // name, clip prefix, volume, pitch low, pitch high, amountScale, sustain, pitchFromAmount
        private static readonly (string, string, float, float, float, float, bool, bool)[] Cues =
        {
            ("step", "footstep_concrete", .5f, .88f, 1.08f, 0, false, false),
            ("shovel", "shovel", .6f, .92f, 1.08f, 0, false, false),
            ("coal", "coal_patter", .45f, .85f, 1.2f, 0, false, false),
            ("pour", "pour", .65f, .95f, 1.05f, 0, false, false),
            ("spill", "spill", .55f, .9f, 1.1f, 0, false, false),
            ("clank", "impactMetal_light", .55f, .9f, 1.1f, 0, false, false),
            ("clunk", "impactMetal_medium", .65f, .9f, 1.05f, 0, false, false),
            ("notch", "impactPlate_light", .45f, .95f, 1.1f, 0, false, false),
            ("hurt", "impactPunch_heavy", .85f, .9f, 1.05f, 0, false, false),
            ("crash", "impactMetal_heavy", .85f, .9f, 1.05f, .1f, false, false),
            ("jam", "impactPlate_heavy", .85f, .85f, 1.0f, .12f, false, false),
            ("jolt", "bin_rattle", .6f, .9f, 1.05f, .25f, false, false),
            ("ram", "impactPunch_medium", .7f, .9f, 1.05f, 0, false, false),
            ("lurch", "lurch", .5f, .95f, 1.05f, 0, false, false),
            ("dash", "dash", .45f, .95f, 1.1f, 0, false, false),
            ("hook", "impactMetal_medium", .55f, .8f, .95f, 0, false, false),
            ("hoist", "chain", .5f, .95f, 1.05f, 0, false, false),
            ("wrench", "wrench", .55f, .95f, 1.05f, 0, false, false),
            ("repair", "impactMetal_light", .6f, .8f, .95f, 0, false, false),
            ("strike", "kindle", .35f, .9f, 1.15f, 0, false, false),
            ("kindle", "kindle", .5f, .95f, 1.05f, 0, false, false),
            ("ignite", "ignite", .8f, .98f, 1.02f, 0, false, false),
            ("ring", "phone_ring", .8f, 1f, 1f, 0, false, false),
            ("phone", "impactPlate_light", .6f, .75f, .85f, 0, false, false),
            ("ding", "impactBell_heavy", .65f, 1.1f, 1.2f, 0, false, false),
            ("shift", "impactBell_heavy", .55f, .9f, .95f, 0, false, false),
            ("leaving", "impactBell_heavy", .5f, 1.15f, 1.25f, 0, false, false),
            ("complete", "impactBell_heavy", .8f, .8f, .85f, 0, false, false),
            ("frozen", "gong", .9f, 1f, 1f, 0, false, false),
            ("page", "paper", .5f, .9f, 1.1f, 0, false, false),
            ("blip", "blip", .15f, 1f, 1f, 0, false, true),
            ("crank", "crank_loop", .25f, 1f, 1f, .15f, true, false),
            ("scrape", "scrape_loop", .35f, 1f, 1f, .4f, true, false),
            ("roll", "cart_roll_loop", .4f, 1f, 1f, .5f, true, false),
            ("rattle", "storm_rattle", .45f, .9f, 1.12f, 1, false, false),
            ("knock", "storm_knock", .7f, .85f, 1.1f, 1, false, false),
        };

        // name, clip prefix, volume
        private static readonly (string, string, float)[] Beds =
        {
            ("wind", "wind_loop", .55f),
            ("howl", "howl_loop", .55f),
            ("fire", "fire_loop", .7f),
            ("lift", "lift_groan_loop", .5f),
            ("drone", "drone_loop", .5f),
            ("heartbeat", "heartbeat_loop", .8f),
            ("shiver", "shiver_loop", .55f),
        };

        public static string Build()
        {
            var old = GameObject.Find("Audio");
            if (old) Object.DestroyImmediate(old);
            var go = new GameObject("Audio");
            go.transform.SetParent(GameObject.Find("Systems").transform, false);
            var director = go.AddComponent<AudioDirector>();
            var so = new SerializedObject(director);
            var cues = so.FindProperty("cues");
            cues.arraySize = Cues.Length;
            var missing = "";
            for (var i = 0; i < Cues.Length; i++)
            {
                var (name, prefix, volume, p0, p1, scale, sustain, fromAmount) = Cues[i];
                var e = cues.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("name").stringValue = name;
                var clips = Clips(prefix);
                if (clips.Length == 0) missing += prefix + " ";
                var arr = e.FindPropertyRelative("clips");
                arr.arraySize = clips.Length;
                for (var k = 0; k < clips.Length; k++) arr.GetArrayElementAtIndex(k).objectReferenceValue = clips[k];
                e.FindPropertyRelative("volume").floatValue = volume;
                e.FindPropertyRelative("pitch").vector2Value = new Vector2(p0, p1);
                e.FindPropertyRelative("amountScale").floatValue = scale;
                e.FindPropertyRelative("sustain").boolValue = sustain;
                e.FindPropertyRelative("pitchFromAmount").boolValue = fromAmount;
            }
            var beds = so.FindProperty("beds");
            beds.arraySize = Beds.Length;
            for (var i = 0; i < Beds.Length; i++)
            {
                var (name, prefix, volume) = Beds[i];
                var e = beds.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("name").stringValue = name;
                var clips = Clips(prefix);
                if (clips.Length == 0) missing += prefix + " ";
                e.FindPropertyRelative("clip").objectReferenceValue = clips.Length > 0 ? clips[0] : null;
                e.FindPropertyRelative("volume").floatValue = volume;
            }
            so.ApplyModifiedProperties();
            var cam = Camera.main;
            if (!cam.GetComponent<AudioListener>()) cam.gameObject.AddComponent<AudioListener>();
            ArtApplier.Save();
            return $"{Cues.Length} cues, {Beds.Length} beds; missing: {missing}";
        }
    }
}
