
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using System.IO;

public class MeshEditorWindow : EditorWindow
{
    private GameObject target;

    private bool brushEnabled = true;
    private float brushRadius = 0.3f;

    private bool hasHit;
    private Vector3 hitPoint;
    private Vector3 hitNormal;

    private int hitSubmesh;
    private int hitTriangle;

    private MeshFilter meshFilter;

    private const string SaveFolder =
        "Assets/MeshEditor/Generated";

    [MenuItem("Tools/Mesh Editor")]
    public static void Open()
    {
        GetWindow<MeshEditorWindow>("Mesh Editor");
    }

    private void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
    }

    private void OnSelectionChange()
    {
        Repaint();
        SceneView.RepaintAll();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField(
            "INTERACTIVE MESH EDITOR",
            EditorStyles.boldLabel
        );

        EditorGUILayout.Space();

        target = (GameObject)EditorGUILayout.ObjectField(
            "Target",
            target,
            typeof(GameObject),
            true
        );

        if (GUILayout.Button("Use Selected Object"))
        {
            target = Selection.activeGameObject;
        }

        EditorGUILayout.Space();

        brushEnabled = EditorGUILayout.Toggle(
            "Enable Brush",
            brushEnabled
        );

        brushRadius = EditorGUILayout.Slider(
            "Brush Radius",
            brushRadius,
            0.01f,
            3f
        );

        EditorGUILayout.Space();

        if (target != null)
        {
            meshFilter = target.GetComponent<MeshFilter>();

            if (meshFilter == null)
            {
                EditorGUILayout.HelpBox(
                    "Target does not have MeshFilter.",
                    MessageType.Error
                );
            }
            else if (meshFilter.sharedMesh == null)
            {
                EditorGUILayout.HelpBox(
                    "MeshFilter has no Mesh.",
                    MessageType.Error
                );
            }
            else if (!meshFilter.sharedMesh.isReadable)
            {
                EditorGUILayout.HelpBox(
                    "Enable Read/Write in Model Import Settings.",
                    MessageType.Error
                );
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Move mouse over the model in Scene View and left-click to remove geometry.",
                    MessageType.Info
                );
            }
        }
        else
        {
            EditorGUILayout.HelpBox(
                "Assign a GameObject containing MeshFilter.",
                MessageType.Warning
            );
        }

        if (GUILayout.Button("Refresh Scene View"))
        {
            SceneView.RepaintAll();
        }
    }

    private void OnSceneGUI(SceneView sceneView)
    {
        if (!brushEnabled || target == null)
            return;

        meshFilter = target.GetComponent<MeshFilter>();

        if (meshFilter == null ||
            meshFilter.sharedMesh == null ||
            !meshFilter.sharedMesh.isReadable)
        {
            return;
        }

        Event e = Event.current;

        Ray ray = HandleUtility.GUIPointToWorldRay(
            e.mousePosition
        );

        hasHit = TryRaycastMesh(
            ray,
            meshFilter,
            out hitPoint,
            out hitNormal,
            out hitSubmesh,
            out hitTriangle
        );

        if (!hasHit)
            return;

        // Draw brush
        Handles.color = Color.red;

        Handles.DrawWireDisc(
            hitPoint,
            hitNormal,
            brushRadius
        );

        Handles.color = new Color(
            1f, 0f, 0f, 0.12f
        );

        Handles.DrawSolidDisc(
            hitPoint,
            hitNormal,
            brushRadius
        );

        // Prevent Unity selection while brushing
        if (e.type == EventType.Layout)
        {
            HandleUtility.AddDefaultControl(
                GUIUtility.GetControlID(
                    FocusType.Passive
                )
            );
        }

        // Remove geometry
        if (e.type == EventType.MouseDown &&
            e.button == 0 &&
            !e.alt)
        {
            RemoveGeometry();

            e.Use();
        }

        sceneView.Repaint();
    }

    private bool TryRaycastMesh(
        Ray ray,
        MeshFilter filter,
        out Vector3 point,
        out Vector3 normal,
        out int submeshIndex,
        out int triangleIndex)
    {
        point = Vector3.zero;
        normal = Vector3.up;

        submeshIndex = -1;
        triangleIndex = -1;

        Mesh mesh = filter.sharedMesh;

        Vector3[] vertices = mesh.vertices;

        float closestDistance = float.MaxValue;

        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            if (mesh.GetTopology(s) != MeshTopology.Triangles)
                continue;

            int[] triangles = mesh.GetTriangles(s);

            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 a = filter.transform.TransformPoint(
                    vertices[triangles[i]]
                );

                Vector3 b = filter.transform.TransformPoint(
                    vertices[triangles[i + 1]]
                );

                Vector3 c = filter.transform.TransformPoint(
                    vertices[triangles[i + 2]]
                );

                if (!RayTriangle(
                    ray, a, b, c, out float distance))
                    continue;

                if (distance >= closestDistance)
                    continue;

                closestDistance = distance;

                point = ray.GetPoint(distance);

                normal = Vector3.Cross(
                    b - a,
                    c - a
                ).normalized;

                // Account for mirrored transforms
                if (filter.transform.localToWorldMatrix
                    .determinant < 0)
                {
                    normal = -normal;
                }

                submeshIndex = s;
                triangleIndex = i / 3;
            }
        }

        return submeshIndex >= 0;
    }

    private bool RayTriangle(
        Ray ray,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        out float distance)
    {
        distance = 0f;

        Vector3 edge1 = b - a;
        Vector3 edge2 = c - a;

        Vector3 p = Vector3.Cross(
            ray.direction,
            edge2
        );

        float determinant = Vector3.Dot(
            edge1,
            p
        );

        if (Mathf.Abs(determinant) < 0.000001f)
            return false;

        float inv = 1f / determinant;

        Vector3 t = ray.origin - a;

        float u = Vector3.Dot(t, p) * inv;

        if (u < 0f || u > 1f)
            return false;

        Vector3 q = Vector3.Cross(t, edge1);

        float v = Vector3.Dot(
            ray.direction,
            q
        ) * inv;

        if (v < 0f || u + v > 1f)
            return false;

        if (u + v > 1f)
            return false;

        distance = Vector3.Dot(edge2, q) * inv;

        return distance >= 0f;
    }
    
    private void RemoveGeometry()
    {
        if (meshFilter == null || !hasHit)
            return;

        Mesh source = meshFilter.sharedMesh;

        Mesh result = MeshGeometry.RemoveBrush(
            source,
            meshFilter.transform,
            hitPoint,
            brushRadius,
            hitSubmesh,
            hitTriangle,
            hitNormal
        );

        if (result == null)
        {
            Debug.LogWarning(
                "Mesh removal failed."
            );
            return;
        }

        if (!Directory.Exists(SaveFolder))
        {
            Directory.CreateDirectory(SaveFolder);
            AssetDatabase.Refresh();
        }

        string path = AssetDatabase.GenerateUniqueAssetPath(
            SaveFolder + "/" +
            source.name + "_Edited.asset"
        );

        AssetDatabase.CreateAsset(result, path);

        AssetDatabase.SaveAssets();

        Undo.RecordObject(
            meshFilter,
            "Remove Mesh Geometry"
        );

        meshFilter.sharedMesh = result;

        EditorUtility.SetDirty(meshFilter);

        hasHit = false;

        SceneView.RepaintAll();

        Repaint();

        Debug.Log(
            "Mesh removed successfully: " + path
        );
    }
}
