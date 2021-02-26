// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
#pragma once

#include <DirectXCollision.h>
#include <DirectXColors.h>
#include <DirectXMath.h>
#include <vector>

#include <winrt/Microsoft.Azure.ObjectAnchors.h>

namespace AoaSampleApp
{
    static inline DirectX::XMFLOAT4 ConvertColor(const DirectX::XMVECTORF32& color)
    {
        DirectX::XMFLOAT4 float4;
        DirectX::XMStoreFloat4(&float4, color);

        return float4;
    }

    // Color table
    static const DirectX::XMFLOAT4 c_White{ ConvertColor(DirectX::Colors::White) };
    static const DirectX::XMFLOAT4 c_Red{ ConvertColor(DirectX::Colors::Red) };
    static const DirectX::XMFLOAT4 c_Green{ ConvertColor(DirectX::Colors::Green) };
    static const DirectX::XMFLOAT4 c_Blue{ ConvertColor(DirectX::Colors::Blue) };
    static const DirectX::XMFLOAT4 c_Yellow{ ConvertColor(DirectX::Colors::Yellow) };
    static const DirectX::XMFLOAT4 c_Pink{ ConvertColor(DirectX::Colors::Pink) };
    static const DirectX::XMFLOAT4 c_Cyan{ ConvertColor(DirectX::Colors::Cyan) };
    static const DirectX::XMFLOAT4 c_Magenta{ ConvertColor(DirectX::Colors::Magenta) };
    static const DirectX::XMFLOAT4 c_Coral{ ConvertColor(DirectX::Colors::Coral) };
    static const DirectX::XMFLOAT4 c_LightSalmon{ ConvertColor(DirectX::Colors::LightSalmon) };
    static const DirectX::XMFLOAT4 c_Purple{ ConvertColor(DirectX::Colors::Purple) };
    static const DirectX::XMFLOAT4 c_SemiTransparentGray{ 0.5f, 0.5f, 0.5f, 0.5f };
    static const DirectX::XMFLOAT4 c_SemiTransparentCyan{ 0.0f, 0.5f, 0.5f, 0.5f };

    // Get vertices and triangle indices of a sphere.
    void GetSphereVerticesAndIndices(winrt::Microsoft::Azure::ObjectAnchors::SpatialGraph::SpatialSphere const& sphere, unsigned short tessellation, bool shouldDrawVerticalSegments, std::vector<DirectX::XMFLOAT3>& vertices, std::vector<uint32_t>& indices);

    // Get vertices and triangle indices of a bounding box.
    void GetBoundingBoxVerticesAndIndices(winrt::Microsoft::Azure::ObjectAnchors::SpatialGraph::SpatialOrientedBox const& box, std::vector<DirectX::XMFLOAT3>& vertices, std::vector<uint32_t>& indices);

    // Get vertices and triangle indices of a field of view.
    void GetFieldOfViewVerticesAndIndices(winrt::Microsoft::Azure::ObjectAnchors::SpatialGraph::SpatialFieldOfView const& fieldOfView, std::vector<DirectX::XMFLOAT3>& vertices, std::vector<uint32_t>& indices);
}
