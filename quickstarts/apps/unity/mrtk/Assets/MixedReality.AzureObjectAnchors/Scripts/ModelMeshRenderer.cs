using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ModelMeshRenderer : MonoBehaviour
{
    /// <summary>
    /// The material to use when rendering the model as a point cloud
    /// </summary>
    public Material PointCloudMaterial;

    /// <summary>
    /// The material to use when rendering the model as a triangle mesh
    /// </summary>
    public Material MeshMaterial;

    /// <summary>
    /// The component that provides the mesh geometry
    /// </summary>
    public MeshFilter MeshFilterComponent;

    /// <summary>
    /// The component that renders the mesh
    /// </summary>
    public MeshRenderer MeshRendererComponent;

    private Material _renderMaterial;
    private int _wireFrameColorParameter = Shader.PropertyToID("_WireColor");

    private void OnDestroy()
    {
        Destroy(MeshRendererComponent.material);
    }

    internal void UpdateMesh(Mesh mesh, Color color)
    {
        if (MeshFilterComponent.sharedMesh == null || MeshFilterComponent.sharedMesh.GetTopology(0) != mesh.GetTopology(0))
        {
            Destroy(MeshRendererComponent.material);

            switch (mesh.GetTopology(0))
            {
                case MeshTopology.Triangles:
                    MeshRendererComponent.material = MeshMaterial;
                    break;
                default: // point cloud material is the default shader so it should generally work.
                    MeshRendererComponent.material = PointCloudMaterial;
                    break;
            }
        }

        MeshFilterComponent.sharedMesh = mesh;

        switch (MeshFilterComponent.sharedMesh.GetTopology(0))
        {
            case MeshTopology.Triangles:
                MeshRendererComponent.material.SetColor(_wireFrameColorParameter, color);
                break;
            default:
                MeshRendererComponent.material.color = color;
                break;
        }
    }
}
