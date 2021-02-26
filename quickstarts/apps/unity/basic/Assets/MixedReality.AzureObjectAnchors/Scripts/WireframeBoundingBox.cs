// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
using UnityEngine;

/// <summary>
/// A class to draw a box around a given Bounds
/// </summary>
public class WireframeBoundingBox : MonoBehaviour
{
    private Vector3 _center = new Vector3();
    private Vector3 _extents = new Vector3();
    private Quaternion _rotation = new Quaternion();
    private Vector3[] _boxEdges = new Vector3[8];
    private Material _wireframeMaterial;

    bool _showBounds = false;

    /// <summary>
    /// Shows the box around the bounds provided.
    /// </summary>
    /// <param name="center">the dimensions of the box to draw</param>
    /// <param name="size">the size of the box</param>
    /// <param name="rotation">the orientation of the bounding box</param>
    /// <param name="wireframeMaterial">the material to draw the bounding box with</param>
    public void UpdateBounds(Vector3 center, Vector3 size, Quaternion rotation, Material wireframeMaterial)
    {
        _center = center;
        _extents = size*0.5f;
        _rotation = rotation;
        _wireframeMaterial = wireframeMaterial;
        CalculateVertexPositions(transform);
        ShowBounds();
    }

    private void ShowBounds()
    {
        _showBounds = true;
    }

    /// <summary>
    /// Hide the bounds
    /// </summary>
    public void HideBounds()
    {
        _showBounds = false;
    }

    /// <summary>
    /// Calculate the vertices of the box.
    /// Transforms the vertices based on the orientation and scale of the object passed in.
    /// </summary>
    /// <param name="objectToDrawTransform"></param>
    void CalculateVertexPositions(Transform objectToDrawTransform)
    {
        // Apply scale and rotation first centered at origin
        _boxEdges[0] = _rotation * new Vector3(-_extents.x, _extents.y,  -_extents.z);  // Front top left corner
        _boxEdges[1] = _rotation * new Vector3( _extents.x, _extents.y,  -_extents.z);  // Front top right corner
        _boxEdges[2] = _rotation * new Vector3(-_extents.x, -_extents.y, -_extents.z);  // Front bottom left corner
        _boxEdges[3] = _rotation * new Vector3( _extents.x, -_extents.y, -_extents.z);  // Front bottom right corner
        _boxEdges[4] = _rotation * new Vector3(-_extents.x,  _extents.y,  _extents.z);  // Back top left corner
        _boxEdges[5] = _rotation * new Vector3( _extents.x,  _extents.y,  _extents.z);  // Back top right corner
        _boxEdges[6] = _rotation * new Vector3(-_extents.x,  -_extents.y,  _extents.z);  // Back bottom left corner
        _boxEdges[7] = _rotation * new Vector3( _extents.x,  -_extents.y,  _extents.z);  // Back bottom right corner

        // Then apply translation
        for (int i = 0; i < _boxEdges.Length; ++i)
        {
            _boxEdges[i] += _center;
        }

        _boxEdges[0] = objectToDrawTransform.TransformPoint(_boxEdges[0]);
        _boxEdges[1] = objectToDrawTransform.TransformPoint(_boxEdges[1]);
        _boxEdges[2] = objectToDrawTransform.TransformPoint(_boxEdges[2]);
        _boxEdges[3] = objectToDrawTransform.TransformPoint(_boxEdges[3]);
        _boxEdges[4] = objectToDrawTransform.TransformPoint(_boxEdges[4]);
        _boxEdges[5] = objectToDrawTransform.TransformPoint(_boxEdges[5]);
        _boxEdges[6] = objectToDrawTransform.TransformPoint(_boxEdges[6]);
        _boxEdges[7] = objectToDrawTransform.TransformPoint(_boxEdges[7]);

    }

    private void OnRenderObject()
    {
        if (_showBounds)
        {
            DrawLines();
        }
    }

    private void Update()
    {
        if (_showBounds)
        {
            CalculateVertexPositions(transform);
        }
    }

    /// <summary>
    /// Draws a line between the vertices of the bounding box
    /// </summary>
    void DrawLines()
    {
        GL.PushMatrix();
        _wireframeMaterial.SetPass(0);
        GL.Begin(GL.LINES);
        GL.Color(Color.white);

        GL.Vertex(_boxEdges[0]);
        GL.Vertex(_boxEdges[1]);

        GL.Vertex(_boxEdges[1]);
        GL.Vertex(_boxEdges[3]);

        GL.Vertex(_boxEdges[3]);
        GL.Vertex(_boxEdges[2]);

        GL.Vertex(_boxEdges[2]);
        GL.Vertex(_boxEdges[0]);

        GL.Vertex(_boxEdges[0]);
        GL.Vertex(_boxEdges[4]);

        GL.Vertex(_boxEdges[4]);
        GL.Vertex(_boxEdges[5]);

        GL.Vertex(_boxEdges[5]);
        GL.Vertex(_boxEdges[7]);

        GL.Vertex(_boxEdges[7]);
        GL.Vertex(_boxEdges[6]);

        GL.Vertex(_boxEdges[6]);
        GL.Vertex(_boxEdges[4]);

        GL.Vertex(_boxEdges[5]);
        GL.Vertex(_boxEdges[1]);

        GL.Vertex(_boxEdges[7]);
        GL.Vertex(_boxEdges[3]);

        GL.Vertex(_boxEdges[6]);
        GL.Vertex(_boxEdges[2]);

        GL.End();

        GL.PopMatrix();
    }
}
