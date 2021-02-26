// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
#pragma once

#include "../Common/DeviceResources.h"
#include "../Common/StepTimer.h"
#include "ShaderStructures.h"

namespace AoaSampleApp
{
    // This sample renderer instantiates a basic rendering pipeline.
    class PrimitiveRenderer
    {
    public:
        PrimitiveRenderer(std::shared_ptr<DeviceResources> const& deviceResources);

        std::future<void> CreateDeviceDependentResources();
        void ReleaseDeviceDependentResources();

        void SetVerticesAndIndices(
            DirectX::XMFLOAT3 const* vertices,
            uint32_t vertexCount,
            uint32_t const* indices,
            uint32_t indexCount,
            D3D11_PRIMITIVE_TOPOLOGY topology);

        void SetColor(DirectX::XMFLOAT4 const& color);

        void SetTransform(DirectX::XMFLOAT4X4 const& frameOfReferenceFromObject);
        void SetActive(bool isActive);
        void Render();

        // Property accessors.
        bool IsActive() const { return m_isActive; }
        DirectX::XMFLOAT3 GetPosition() const;

    private:
       void RecreateVertexAndIndexBuffers(
            uint32_t vertexCount,
            uint32_t indexCount);

        // Cached pointer to device resources.
        std::shared_ptr<DeviceResources>                m_deviceResources;

        // Direct3D resources for geometry.
        Microsoft::WRL::ComPtr<ID3D11InputLayout>       m_inputLayout;
        Microsoft::WRL::ComPtr<ID3D11Buffer>            m_vertexBuffer;
        Microsoft::WRL::ComPtr<ID3D11Buffer>            m_indexBuffer;
        Microsoft::WRL::ComPtr<ID3D11VertexShader>      m_vertexShader;
        Microsoft::WRL::ComPtr<ID3D11GeometryShader>    m_geometryShader;
        Microsoft::WRL::ComPtr<ID3D11PixelShader>       m_pixelShader;
        Microsoft::WRL::ComPtr<ID3D11Buffer>            m_modelConstantBuffer;
        Microsoft::WRL::ComPtr<ID3D11RasterizerState>   m_rasterizerState;

        // CPU Resources for geometry
        std::vector<VertexPosition>                     m_volumeVertices;
        std::vector<uint32_t>                           m_volumeIndices;
        
        // System resources for cube geometry.
        ModelConstantBuffer                             m_modelConstantBufferData;

        // Description about the primitive.
        uint32_t                                        m_vertexCount{ 0 };
        uint32_t                                        m_indexCount{ 0 };
        D3D11_PRIMITIVE_TOPOLOGY                        m_primitiveTopology{ D3D11_PRIMITIVE_TOPOLOGY_UNDEFINED };

        // Transform from model to view.
        DirectX::XMFLOAT4X4                             m_frameOfReferenceFromPrimitive;

        // Color to render
        DirectX::XMFLOAT4                               m_modelColor;

        // Variables used with the rendering loop.
        bool                                            m_loadingComplete{ false };

        // If the current D3D Device supports VPRT, we can avoid using a geometry
        // shader just to set the render target array index.
        bool                                            m_usingVprtShaders{ false };

        // Draw this object if it's active, hide otherwise.
        bool                                            m_isActive{ false };
    };
}
