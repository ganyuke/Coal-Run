using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoalRun.Editor
{
    // Puts the generated sprites onto the scene's objects, keeping each where it was and as big as it was.
    // Called from the editor CLI (eval), one group of objects at a time.
    public static class ArtApplier
    {
        public static Sprite Load(string name) => AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Art/Generated/{name}.png");

        // The path of a transform below the scene root, e.g. "Room/Art/Walls/Back wall".
        public static string PathOf(Transform t)
        {
            var s = t.name;
            while (t.parent) { t = t.parent; s = t.name + "/" + s; }
            return s;
        }

        // Every sprite renderer whose path ends with `tail`.
        public static List<SpriteRenderer> Find(string tail)
        {
            var found = new List<SpriteRenderer>();
            foreach (var r in Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include))
                if (PathOf(r.transform).EndsWith(tail)) found.Add(r);
            return found;
        }

        // Swaps the sprite and rescales so the renderer's world size and centre are unchanged.
        public static int Fit(string tail, string sprite, bool white = true, float alpha = -1)
        {
            var s = Load(sprite);
            if (!s) { Debug.LogError("No sprite " + sprite); return 0; }
            var n = 0;
            foreach (var r in Find(tail))
            {
                var before = r.bounds.size;
                r.sprite = s;
                var after = r.bounds.size;
                var ls = r.transform.localScale;
                r.transform.localScale = new Vector3(ls.x * before.x / Mathf.Max(.0001f, after.x), ls.y * before.y / Mathf.Max(.0001f, after.y), ls.z);
                if (white) r.color = new Color(1, 1, 1, alpha >= 0 ? alpha : r.color.a);
                EditorUtility.SetDirty(r);
                n++;
            }
            return n;
        }

        // Swaps the sprite and keeps the transform's scale as it was: for the 1 x 1 unit sprites that were stretched to size.
        public static int Swap(string tail, string sprite, bool white = true, float alpha = -1)
        {
            var s = Load(sprite);
            if (!s) { Debug.LogError("No sprite " + sprite); return 0; }
            var n = 0;
            foreach (var r in Find(tail))
            {
                r.sprite = s;
                if (white) r.color = new Color(1, 1, 1, alpha >= 0 ? alpha : r.color.a);
                EditorUtility.SetDirty(r);
                n++;
            }
            return n;
        }

        // Swaps the sprite and lets it be its own natural size (scale 1), for art drawn to scale.
        public static int Natural(string tail, string sprite, float alpha = 1)
        {
            var s = Load(sprite);
            if (!s) { Debug.LogError("No sprite " + sprite); return 0; }
            var n = 0;
            foreach (var r in Find(tail))
            {
                r.sprite = s;
                r.transform.localScale = new Vector3(1, 1, r.transform.localScale.z);
                r.color = new Color(1, 1, 1, alpha);
                EditorUtility.SetDirty(r);
                n++;
            }
            return n;
        }

        // A new decorative sprite under Room/Art/Decor, `width` x `height` units (the sprite stretched to fit).
        public static GameObject Add(string name, string sprite, float x, float y, float width, float height, int order = -19, float rotation = 0, float tint = 1)
        {
            var parent = GameObject.Find("Room/Art/Decor");
            if (!parent)
            {
                var art = GameObject.Find("Room/Art");
                parent = new GameObject("Decor");
                parent.transform.SetParent(art.transform, false);
            }
            var s = Load(sprite);
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = new Vector3(x, y, 0);
            go.transform.localRotation = Quaternion.Euler(0, 0, rotation);
            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = s;
            r.sortingOrder = order;
            r.color = new Color(tint, tint, tint, 1);
            var size = s.bounds.size;
            go.transform.localScale = new Vector3(width / size.x, height / size.y, 1);
            return go;
        }

        public static void ClearDecor()
        {
            var parent = GameObject.Find("Room/Art/Decor");
            if (parent) Object.DestroyImmediate(parent);
        }

        public static int Hide(string tail)
        {
            var n = 0;
            foreach (var r in Find(tail)) { r.gameObject.SetActive(false); n++; }
            return n;
        }

        // A lit sprite material on every sprite in the scene except the ones that glow by themselves.
        public static int ApplyLit(params string[] unlitTails)
        {
            var path = "Assets/Art/SpriteLit.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default")) { name = "SpriteLit" };
                AssetDatabase.CreateAsset(mat, path);
            }
            var n = 0;
            foreach (var r in Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include))
            {
                var p = PathOf(r.transform);
                if (p.Contains("Notebook UI") || p.Contains("Speech")) continue;
                var skip = false;
                foreach (var t in unlitTails) if (p.Contains(t)) skip = true;
                r.sharedMaterial = skip ? AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat") : mat;
                if (skip) r.sharedMaterial = Unlit();
                EditorUtility.SetDirty(r);
                n++;
            }
            return n;
        }

        private static Material Unlit()
        {
            var path = "Assets/Art/SpriteUnlit.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat) return mat;
            mat = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")) { name = "SpriteUnlit" };
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        public static void Save()
        {
            var scene = SceneManager.GetActiveScene();
            EditorSceneManager_MarkAndSave(scene);
        }

        private static void EditorSceneManager_MarkAndSave(Scene scene)
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
        }
    }
}
