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

        MeshFilter meshFilter = gameObject.AddComponent<MeshFilter>();
        Mesh mesh = new Mesh();

        ObjectAnchorsSession session = new ObjectAnchorsSession(ObjectAnchorsConfig.GetConfig().AccountInformation);
        ObjectObserver observer = session.CreateObjectObserver();
        byte[] modelBytes = File.ReadAllBytes(ModelPath);
        ObjectModel model = await observer.LoadObjectModelAsync(modelBytes);

        gameObject.transform.localPosition = model.BoundingBox.Center.ToUnity();
        gameObject.transform.localRotation = model.BoundingBox.Orientation.ToUnity();

        // We need to flip handedness of vertices and modify triangle list to
        // clockwise winding in order to be usable in Unity.

        Numerics.Vector3[] modelVertices = new Numerics.Vector3[model.VertexCount];
        model.GetVertexPositions(modelVertices);
        mesh.vertices = modelVertices.Select(v => v.ToUnity()).ToArray();
        if (modelVertices.Length > UInt16.MaxValue)
        {
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        }

        if (model.TriangleIndexCount > 2)
        {
            // Counter clock wise
            uint[] modelIndices_ccw = new uint[model.TriangleIndexCount];
            model.GetTriangleIndices(modelIndices_ccw);

            // Clock wise
            int[] modelIndices_cw = new int[modelIndices_ccw.Length];
            Enumerable.Range(0, modelIndices_ccw.Length).Select(i =>
                i % 3 == 0 ?
                modelIndices_cw[i] = (int)modelIndices_ccw[i] : i % 3 == 1 ?
                modelIndices_cw[i] = (int)modelIndices_ccw[i + 1] : modelIndices_cw[i] = (int)modelIndices_ccw[i - 1])
                .ToArray();

            mesh.SetIndices(modelIndices_cw, MeshTopology.Triangles, 0);
        }
        else
        {
            mesh.SetIndices(Enumerable.Range(0, modelVertices.Length).ToArray(), MeshTopology.Points, 0);
        }

        Numerics.Vector3[] modelNormals = new Numerics.Vector3[model.VertexCount];
        model.GetVertexNormals(modelNormals);
        mesh.normals = modelNormals.Select(v => v.ToUnity()).ToArray();

        mesh.RecalculateBounds();
        meshFilter.mesh = mesh;
    }
}
