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

            gameObject.AddComponent<MeshFilter>().mesh = LoadMesh(modelVertices, modelNormals, modelIndicesCcw);
        }
    }

    public static Mesh LoadMesh(
        Numerics.Vector3[] modelVertices,
        Numerics.Vector3[] modelNormals,
        uint[] modelIndicesCcw)
    {
        Mesh mesh = new Mesh();
        LoadMesh(mesh, new MeshData(modelVertices, modelNormals, modelIndicesCcw));
        return mesh;
    }

    public struct MeshData
    {
        public readonly Vector3[] vertices;
        public readonly Vector3[] normals;
        public readonly int[] indices;
        public readonly MeshTopology topology;

        public MeshData(
            Numerics.Vector3[] modelVertices,
            Numerics.Vector3[] modelNormals,
            uint[] modelIndicesCcw)
        {
            vertices = modelVertices.Select(v => v.ToUnity()).ToArray();

            if (modelIndicesCcw.Length > 2)
            {
                // Clock wise
                int[] modelIndicesCw = indices = new int[modelIndicesCcw.Length];
                Enumerable.Range(0, modelIndicesCcw.Length).Select(i =>
                    i % 3 == 0 ?
                    modelIndicesCw[i] = (int)modelIndicesCcw[i] : i % 3 == 1 ?
                    modelIndicesCw[i] = (int)modelIndicesCcw[i + 1] : modelIndicesCw[i] = (int)modelIndicesCcw[i - 1])
                    .ToArray();

                topology = MeshTopology.Triangles;
            }
            else
            {
                indices = Enumerable.Range(0, modelVertices.Length).ToArray();
                topology = MeshTopology.Points;
            }

            normals = modelNormals.Select(v => v.ToUnity()).ToArray();
        }
    }

    public static void LoadMesh(Mesh mesh, MeshData meshData)
    {
        mesh.Clear();

        // We need to flip handedness of vertices and modify triangle list to
        // clockwise winding in order to be usable in Unity.

        mesh.vertices = meshData.vertices;
        if (mesh.vertices.Length > ushort.MaxValue)
        {
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        }

        mesh.SetIndices(meshData.indices, meshData.topology, 0);
        mesh.normals = meshData.normals;
        mesh.RecalculateBounds();
    }

    public static void AddMesh(GameObject gameObject, IObjectAnchorsService service, Guid modelId)
    {
        gameObject.AddComponent<MeshFilter>().mesh = LoadMesh(
            service.GetModelVertexPositions(modelId),
            service.GetModelVertexNormals(modelId),
            new uint[] { });
    }
}
