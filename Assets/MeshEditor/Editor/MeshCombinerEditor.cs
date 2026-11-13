
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

public class MeshCombinerEditor : EditorWindow
{
    private GameObject targetObject;

    [MenuItem("Tools/Edit Mesh/Mesh Combiner")]
    public static void OpenWindow()
    {
        GetWindow<MeshCombinerEditor>("Mesh Combiner");
    }

    private void OnGUI()
    {
        GUILayout.Label("Mesh Combiner Tool",
            EditorStyles.boldLabel);

        targetObject = (GameObject)EditorGUILayout.ObjectField(
            "Target Object",
            targetObject,
            typeof(GameObject),
            true
        );

        EditorGUILayout.Space();

        GUI.enabled = targetObject != null;

        if (GUILayout.Button("Combine Meshes", GUILayout.Height(35)))
        {
            Combine();
        }

        GUI.enabled = true;
    }

    private void Combine()
    {
        MeshFilter[] filters =
            targetObject.GetComponentsInChildren<MeshFilter>();

        List<CombineInstance> combines = new();
        List<Material> materials = new();

        foreach (MeshFilter filter in filters)
        {
            if (filter.gameObject == targetObject)
                continue;

            Mesh mesh = filter.sharedMesh;

            if (mesh == null)
                continue;

            MeshRenderer renderer =
                filter.GetComponent<MeshRenderer>();

            if (renderer == null)
                continue;

            Material[] mats = renderer.sharedMaterials;

            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                if (i >= mats.Length)
                    continue;

                CombineInstance instance = new CombineInstance
                {
                    mesh = mesh,
                    subMeshIndex = i,
                    transform =
                        targetObject.transform.worldToLocalMatrix *
                        filter.transform.localToWorldMatrix
                };

                combines.Add(instance);
                materials.Add(mats[i]);
            }
        }

        if (combines.Count == 0)
        {
            Debug.LogWarning("No meshes found!");
            return;
        }

        Mesh combinedMesh = new Mesh
        {
            name = targetObject.name + "_Combined"
        };

        combinedMesh.indexFormat =
            UnityEngine.Rendering.IndexFormat.UInt32;

        combinedMesh.CombineMeshes(
            combines.ToArray(),
            false,
            true
        );

        combinedMesh.RecalculateBounds();

        string path = EditorUtility.SaveFilePanelInProject(
            "Save Combined Mesh",
            combinedMesh.name,
            "asset",
            "Choose where to save the combined mesh."
        );

        if (string.IsNullOrEmpty(path))
        {
            DestroyImmediate(combinedMesh);
            return;
        }

        AssetDatabase.CreateAsset(combinedMesh, path);
        AssetDatabase.SaveAssets();

        Undo.RegisterFullObjectHierarchyUndo(
            targetObject,
            "Combine Meshes"
        );

        MeshFilter targetFilter =
            targetObject.GetComponent<MeshFilter>();

        if (targetFilter == null)
            targetFilter = Undo.AddComponent<MeshFilter>(targetObject);

        MeshRenderer targetRenderer =
            targetObject.GetComponent<MeshRenderer>();

        if (targetRenderer == null)
            targetRenderer = Undo.AddComponent<MeshRenderer>(targetObject);

        Undo.RecordObject(targetFilter, "Assign Combined Mesh");
        Undo.RecordObject(targetRenderer, "Assign Combined Materials");

        targetFilter.sharedMesh = combinedMesh;
        targetRenderer.sharedMaterials = materials.ToArray();

        foreach (MeshFilter filter in filters)
        {
            if (filter.gameObject == targetObject)
                continue;

            Undo.DestroyObjectImmediate(filter.gameObject);
        }

        EditorUtility.SetDirty(targetObject);

        Debug.Log("Mesh Combination Completed!");
    }
}
