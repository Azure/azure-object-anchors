using UnityEngine;
using System.IO;
using Numerics = System.Numerics;

using Microsoft.Azure.ObjectAnchors;
using Microsoft.Azure.ObjectAnchors.Unity;
using System.Linq;
using System;

public class MeshLoader : MonoBehaviour
{
    public string ModelPath;

    async void Start()
    {
        Debug.Log($"Loading model from: '{ModelPath}'");
        byte[] modelBytes = File.ReadAllBytes(ModelPath);

        ObjectAnchorsSession session = new ObjectAnchorsSession(ObjectAnchorsConfig.GetConfig().AccountInformation);
        using (ObjectObserver observer = session.CreateObjectObserver())
        using (ObjectModel model = await observer.LoadObjectModelAsync(modelBytes))
        {
            Numerics.Vector3[] modelVertices = new Numerics.Vector3[model.VertexCount];
            model.GetVertexPositions(modelVertices);

            // Counter clock wise
            uint[] modelIndicesCcw = new uint[model.TriangleIndexCount];
            model.GetTriangleIndices(modelIndicesCcw);

            Numerics.Vector3[] modelNormals = new Numerics.Vector3[model.VertexCount];
            model.GetVertexNormals(modelNormals);

            AddMesh(gameObject,
                modelVertices,
                modelNormals,
                modelIndicesCcw);
        }
    }

    static void AddMesh(GameObject gameObject,
        Numerics.Vector3[] modelVertices,
        Numerics.Vector3[] modelNormals,
        uint[] modelIndicesCcw)
    {
        MeshFilter meshFilter = gameObject.AddComponent<MeshFilter>();
        Mesh mesh = new Mesh();

        // We need to flip handedness of vertices and modify triangle list to
        // clockwise winding in order to be usable in Unity.

        mesh.vertices = modelVertices.Select(v => v.ToUnity()).ToArray();
        if (modelVertices.Length > UInt16.MaxValue)
        {
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        }

        if (modelIndicesCcw.Length > 2)
        {
            // Clock wise
            int[] modelIndicesCw = new int[modelIndicesCcw.Length];
            Enumerable.Range(0, modelIndicesCcw.Length).Select(i =>
                i % 3 == 0 ?
                modelIndicesCw[i] = (int)modelIndicesCcw[i] : i % 3 == 1 ?
                modelIndicesCw[i] = (int)modelIndicesCcw[i + 1] : modelIndicesCw[i] = (int)modelIndicesCcw[i - 1])
                .ToArray();

            mesh.SetIndices(modelIndicesCw, MeshTopology.Triangles, 0);
        }
        else
        {
            mesh.SetIndices(Enumerable.Range(0, modelVertices.Length).ToArray(), MeshTopology.Points, 0);
        }

        mesh.normals = modelNormals.Select(v => v.ToUnity()).ToArray();

        mesh.RecalculateBounds();
        meshFilter.mesh = mesh;
    }

    public static void AddMesh(GameObject gameObject, IObjectAnchorsService service, Guid modelId)
    {
        AddMesh(gameObject,
            service.GetModelVertexPositions(modelId),
            service.GetModelVertexNormals(modelId),
            new uint[] { });
    }
}
