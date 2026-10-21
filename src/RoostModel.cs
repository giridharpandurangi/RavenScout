using Jotunn.Managers;
using UnityEngine;

namespace RavenScout
{
    /// <summary>
    /// Dresses the cloned chest up as a roost using vanilla models: a wooden post beside the cargo box
    /// with a raven sitting on top. Sizes are measured from the meshes, so nothing depends on exact model dimensions.
    /// </summary>
    internal static class RoostModel
    {
        private static readonly string[] s_poleCandidates = { "wood_pole2", "wood_pole", "wood_pole_log" };
        private const float PoleHeight = 2.2f;
        private const float PoleThickness = 0.2f;
        private const float BirdScale = 1.0f;

        public static void Decorate(GameObject roost)
        {
            var chestBounds = LocalBounds(roost, roost.transform);
            var poleTop = AddPole(roost, chestBounds);
            AddBird(roost, poleTop);
        }

        private static Vector3? AddPole(GameObject roost, Bounds chestBounds)
        {
            GameObject source = null;
            foreach (var name in s_poleCandidates)
            {
                source = PrefabManager.Instance.GetPrefab(name);
                if (source)
                {
                    Plugin.Log.LogInfo($"Roost post uses prefab '{name}'");
                    break;
                }
            }
            if (!source)
            {
                Plugin.Log.LogWarning("No wooden pole prefab found; roost keeps the plain chest look.");
                return null;
            }

            // Pieces keep their intact model under WearNTear.m_new; take just that, not the piece logic.
            var wear = source.GetComponent<WearNTear>();
            var visual = wear && wear.m_new ? wear.m_new : source;

            var pole = Object.Instantiate(visual, roost.transform);
            pole.name = "RavenScout_Pole";
            pole.SetActive(true);
            foreach (var behaviour in pole.GetComponentsInChildren<MonoBehaviour>(true))
            {
                Object.DestroyImmediate(behaviour);
            }
            pole.transform.localPosition = Vector3.zero;
            pole.transform.localRotation = Quaternion.identity;
            pole.transform.localScale = Vector3.one;

            var b = LocalBounds(pole, roost.transform);
            if (b.size.y < 0.01f)
            {
                Object.DestroyImmediate(pole);
                return null;
            }

            // Force a slim post whatever the source model's proportions are.
            float thickest = Mathf.Max(b.size.x, b.size.z, 0.01f);
            float scaleXZ = PoleThickness / thickest;
            pole.transform.localScale = new Vector3(scaleXZ, PoleHeight / b.size.y, scaleXZ);
            b = LocalBounds(pole, roost.transform);

            // Stand it on the ground just right of the cargo box.
            var offset = new Vector3(
                chestBounds.max.x + b.extents.x + 0.05f - b.center.x,
                chestBounds.min.y - b.min.y,
                chestBounds.center.z - b.center.z);
            pole.transform.localPosition += offset;

            b = LocalBounds(pole, roost.transform);
            return new Vector3(b.center.x, b.max.y, b.center.z);
        }

        private static void AddBird(GameObject roost, Vector3? poleTop)
        {
            var template = RavenFlight.PerchedTemplate;
            if (!template)
            {
                return;
            }

            var bird = Object.Instantiate(template, roost.transform);
            bird.name = "RavenScout_PerchedRaven";
            bird.SetActive(true);
            bird.transform.localRotation = Quaternion.identity;
            bird.transform.localScale = template.transform.localScale * BirdScale;
            bird.transform.localPosition = Vector3.zero;

            var b = LocalBounds(bird, roost.transform);
            Vector3 seat;
            if (poleTop.HasValue)
            {
                seat = poleTop.Value;
            }
            else
            {
                // No pole: sit on the lid of the box.
                var chest = LocalBounds(roost, roost.transform);
                seat = new Vector3(chest.center.x, chest.max.y, chest.center.z);
            }
            bird.transform.localPosition = seat - new Vector3(b.center.x, b.min.y, b.center.z);
        }

        /// <summary>Mesh bounds of everything under <paramref name="part"/>, in <paramref name="space"/>'s local space. Works on inactive objects.</summary>
        public static Bounds LocalBounds(GameObject part, Transform space)
        {
            bool any = false;
            var result = new Bounds();
            var toSpace = space.worldToLocalMatrix;

            void Add(Mesh mesh, Transform t)
            {
                if (!mesh)
                {
                    return;
                }
                var m = toSpace * t.localToWorldMatrix;
                var mb = mesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(corner);
                    if (!any)
                    {
                        result = new Bounds(p, Vector3.zero);
                        any = true;
                    }
                    else
                    {
                        result.Encapsulate(p);
                    }
                }
            }

            foreach (var mf in part.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.GetComponent<MeshRenderer>() && IsShown(mf.transform, part.transform))
                {
                    Add(mf.sharedMesh, mf.transform);
                }
            }
            foreach (var smr in part.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (IsShown(smr.transform, part.transform))
                {
                    Add(smr.sharedMesh, smr.transform);
                }
            }
            return result;
        }

        /// <summary>
        /// True unless something between <paramref name="t"/> and <paramref name="root"/> is switched off
        /// (e.g. the bird's hidden flying model). Ignores whether the root itself is active.
        /// </summary>
        private static bool IsShown(Transform t, Transform root)
        {
            for (; t && t != root; t = t.parent)
            {
                if (!t.gameObject.activeSelf)
                {
                    return false;
                }
            }
            return true;
        }
    }
}
