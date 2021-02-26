
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
#include "pch.h"
#include "GeometricPrimitives.h"

using namespace DirectX;
using namespace std;
using namespace winrt::Microsoft::Azure::ObjectAnchors;
using namespace winrt::Microsoft::Azure::ObjectAnchors::SpatialGraph;
using namespace winrt::Windows::Foundation::Numerics;

// Get vertices and triangle indices of a sphere.
 void AoaSampleApp::GetSphereVerticesAndIndices(SpatialSphere const& sphere, unsigned short tessellation, bool shouldDrawVerticalSegments, vector<XMFLOAT3>& vertices, vector<uint32_t>& indices)
{
    vertices.clear();
    indices.clear();

    if (tessellation < 3)
    {
        return;
    }

    uint32_t verticalSegments = tessellation;
    uint32_t horizontalSegments = tessellation * 2;

    // Create rings of vertices at progressively higher latitudes.
    for (uint32_t i = 0; i <= verticalSegments; i++)
    {
        float v = 1 - (float)i / verticalSegments;

        float latitude = (i * XM_PI / verticalSegments) - XM_PIDIV2;
        float dy, dxz;

        XMScalarSinCos(&dy, &dxz, latitude);

        // Create a single ring of vertices at this latitude.
        for (uint32_t j = 0; j <= horizontalSegments; j++)
        {
            float u = (float)j / horizontalSegments;

            float longitude = j * XM_2PI / horizontalSegments;
            float dx, dz;

            XMScalarSinCos(&dx, &dz, longitude);

            dx *= dxz;
            dz *= dxz;

            XMVECTOR normal = XMVectorSet(dx, dy, dz, 0);
            XMVECTOR textureCoordinate = XMVectorSet(u, v, 0, 0);

            XMFLOAT3 vert;
            XMStoreFloat3(&vert, normal * sphere.Radius);

            vert.x += sphere.Center.x;
            vert.y += sphere.Center.y;
            vert.z += sphere.Center.z;

            vertices.push_back(vert);
        }
    }

    // Fill the index buffer with triangles joining each pair of latitude rings.
    uint32_t stride = horizontalSegments + 1;

    for (uint32_t i = 0; i < verticalSegments; i++)
    {
        for (uint32_t j = 0; j <= horizontalSegments; j++)
        {
            uint32_t nextI = i + 1;
            uint32_t nextJ = (j + 1) % stride;

            indices.push_back(i * stride + j);
            indices.push_back(nextI * stride + j);
            indices.push_back(i * stride + nextJ);

            indices.push_back(i * stride + nextJ);
            indices.push_back(nextI * stride + j);
            indices.push_back(nextI * stride + nextJ);
        }
    }
}

// Get vertices and triangle indices of a bounding box.
 void AoaSampleApp::GetBoundingBoxVerticesAndIndices(SpatialOrientedBox const& box, vector<XMFLOAT3>& vertices, vector<uint32_t>& indices)
{
    // 8 corners position of bounding box.
    //
    //     Far     Near
    //    0----1  4----5
    //    |    |  |    |
    //    |    |  |    |
    //    3----2  7----6

    BoundingOrientedBox bounds;
    bounds.Center = reinterpret_cast<XMFLOAT3 const&>(box.Center);
    bounds.Extents = reinterpret_cast<XMFLOAT3 const&>(box.Extents * 0.5f); // DirectX uses half size as extent.
    bounds.Orientation = reinterpret_cast<XMFLOAT4 const&>(box.Orientation);

    array<XMFLOAT3, 8> corners;
    bounds.GetCorners(corners.data());

    constexpr array<uint32_t, 24> c_boundsOutlineIndices =
    {
        {
            0, 1, 1, 2, 2, 3, 3, 0,     // far plane
            4, 5, 5, 6, 6, 7, 7, 4,     // near plane
            0, 4, 1, 5, 2, 6, 3, 7,     // far to near
        },
    };

    vertices.assign(corners.cbegin(), corners.cend());
    indices.assign(c_boundsOutlineIndices.cbegin(), c_boundsOutlineIndices.cend());
}

// Get vertices and triangle indices of a field of view.
 void AoaSampleApp::GetFieldOfViewVerticesAndIndices(SpatialFieldOfView const& fieldOfView, vector<XMFLOAT3>& vertices, vector<uint32_t>& indices)
{
    // 8 corners position of bounding frustum.
    //
    //     Far     Near
    //    0----1  4----5
    //    |    |  |    |
    //    |    |  |    |
    //    3----2  7----6

    XMFLOAT4X4 M;
    memset(&M, 0, sizeof(M));

    const float nearPlane = 0.1f;
    const float farPlane = fieldOfView.FarDistance;

    const float w = 1.0f / tanf(fieldOfView.HorizontalFieldOfViewInDegrees * 0.5f * 3.1415926f / 180.0f);
    const float h = w * fieldOfView.AspectRatio;
    const float Q = farPlane / (farPlane - nearPlane);

    // Projection matrix in LH coordinate system.
    M(0, 0) = w;
    M(1, 1) = h;
    M(2, 2) = Q;
    M(2, 3) = 1;
    M(3, 2) = -Q * nearPlane;

    // Frustum in LH coordinate system.
    BoundingFrustum frustum;
    BoundingFrustum::CreateFromMatrix(frustum, XMLoadFloat4x4(&M));

    // Transform its location to RH coordinate system by rotating about +Y by 180 degrees.
    XMMATRIX transform =
        XMMatrixRotationY(3.1415926f) *
        XMMatrixRotationQuaternion(XMLoadFloat4(&reinterpret_cast<XMFLOAT4 const&>(fieldOfView.Orientation))) *
        XMMatrixTranslationFromVector(XMLoadFloat3(&reinterpret_cast<XMFLOAT3 const&>(fieldOfView.Position)));

    BoundingFrustum frustumTrans;
    frustum.Transform(frustumTrans, transform);

    array<XMFLOAT3, 8> corners;
    frustumTrans.GetCorners(corners.data());

    array<uint32_t, 24> c_boundsOutlineIndices =
    {
        {
            0, 1, 1, 2, 2, 3, 3, 0,     // far plane
            4, 5, 5, 6, 6, 7, 7, 4,     // near plane
            0, 4, 1, 5, 2, 6, 3, 7,     // far to near
        },
    };

    vertices.assign(corners.cbegin(), corners.cend());
    indices.assign(c_boundsOutlineIndices.cbegin(), c_boundsOutlineIndices.cend());
}
