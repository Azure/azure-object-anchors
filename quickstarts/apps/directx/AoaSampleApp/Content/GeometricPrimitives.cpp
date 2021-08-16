
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
    // BoundingFrustum is constructed with +Z forward, so add a 180-degree rotation about Y to point it towards -Z instead
    // (forward in a right-handed coordinate system with +X right and +Y up).
    static const quaternion rotate180AboutYAxis = make_quaternion_from_axis_angle(float3::unit_y(), XM_PI);
    const quaternion orientation = fieldOfView.Orientation * rotate180AboutYAxis;

    // Note that the naming of BoundingFrustum's fields are expressed in a left-handed coordinate system; however, left/right
    // simply refer to -X/+X. Therefore when used in a right-handed coordinate system, "left"/"right" are swapped.
    BoundingFrustum frustum;
    frustum.Origin = { fieldOfView.Position.x, fieldOfView.Position.y, fieldOfView.Position.z };
    frustum.Orientation = { orientation.x, orientation.y, orientation.z, orientation.w };
    frustum.Near = 0.1f;
    frustum.Far = fieldOfView.FarDistance;
    frustum.RightSlope = tanf(0.5f * XM_PI * fieldOfView.HorizontalFieldOfViewInDegrees / 180.f);
    frustum.LeftSlope = -frustum.RightSlope;
    frustum.TopSlope = frustum.RightSlope / fieldOfView.AspectRatio;
    frustum.BottomSlope = -frustum.TopSlope;

    // 8 corners position of bounding frustum in a right-handed system (see above).
    //
    //     Near    Far
    //    1----0  5----4
    //    |    |  |    |
    //    |    |  |    |
    //    2----3  6----7

    array<XMFLOAT3, 8> corners;
    frustum.GetCorners(corners.data());

    array<uint32_t, 24> c_boundsOutlineIndices =
    {
        {
            0, 1, 1, 2, 2, 3, 3, 0,     // near plane
            4, 5, 5, 6, 6, 7, 7, 4,     // far plane
            0, 4, 1, 5, 2, 6, 3, 7,     // far to near
        },
    };

    vertices.assign(corners.cbegin(), corners.cend());
    indices.assign(c_boundsOutlineIndices.cbegin(), c_boundsOutlineIndices.cend());
}