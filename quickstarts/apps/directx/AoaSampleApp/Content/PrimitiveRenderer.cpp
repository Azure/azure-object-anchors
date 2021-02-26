// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
#include "pch.h"
#include "PrimitiveRenderer.h"
#include "Common/DirectXHelper.h"

using namespace AoaSampleApp;
using namespace DirectX;
using namespace winrt::Windows::Foundation::Numerics;
using namespace winrt::Windows::UI::Input::Spatial;

// Loads vertex and pixel shaders from files and instantiates the cube geometry.
PrimitiveRenderer::PrimitiveRenderer(std::shared_ptr<DeviceResources> const& deviceResources) 
    : m_deviceResources(deviceResources)
{
    CreateDeviceDependentResources();

    XMStoreFloat4x4(&m_frameOfReferenceFromPrimitive, XMMatrixIdentity());
}

void PrimitiveRenderer::SetVerticesAndIndices(
    XMFLOAT3 const* vertices,
    uint32_t vertexCount,
    uint32_t const* indices,
    uint32_t indexCount,
    D3D11_PRIMITIVE_TOPOLOGY topology)
{
    // If we need more memory to store the updated geometry, recreate the buffers
    // Otherwise we just reuse and update the previous buffers.
    if (vertexCount > m_volumeVertices.size() || indexCount > m_volumeIndices.size())
    {
        RecreateVertexAndIndexBuffers(vertexCount, indexCount);
    }

    m_vertexCount = vertexCount;
    m_indexCount = indexCount;

    // If there is no geometry, we're done.
    if (m_vertexCount == 0 || m_indexCount == 0)
    {
        m_primitiveTopology = D3D11_PRIMITIVE_TOPOLOGY_UNDEFINED;
        return;
    }

    // copy the callers vertices into our local storage. This isn't strictly necessary with just a list of positions
    // but if we add other parameters to the vertex, it will be again.
    for (uint32_t i = 0; i < m_vertexCount; i++)
    {
        m_volumeVertices[i] = (VertexPosition{ vertices[i] });
    }

    // Update our buffers with the updated geometry
    D3D11_MAPPED_SUBRESOURCE resource;
    m_deviceResources->GetD3DDeviceContext()->Map(m_vertexBuffer.Get(), 0, D3D11_MAP_WRITE_DISCARD, 0, &resource);
    memcpy(resource.pData, m_volumeVertices.data(), sizeof(VertexPosition) * m_vertexCount);
    m_deviceResources->GetD3DDeviceContext()->Unmap(m_vertexBuffer.Get(), 0);

    m_deviceResources->GetD3DDeviceContext()->Map(m_indexBuffer.Get(), 0, D3D11_MAP_WRITE_DISCARD, 0, &resource);
    memcpy(resource.pData, indices, sizeof(uint32_t) * indexCount);
    m_deviceResources->GetD3DDeviceContext()->Unmap(m_indexBuffer.Get(), 0);

    m_primitiveTopology = topology;
}

void AoaSampleApp::PrimitiveRenderer::SetColor(DirectX::XMFLOAT4 const& color)
{
    m_modelColor = color;
    m_modelConstantBufferData.color = m_modelColor;
}

void PrimitiveRenderer::SetTransform(XMFLOAT4X4 const& frameOfReferenceFromObject)
{
    m_frameOfReferenceFromPrimitive = frameOfReferenceFromObject;
}

DirectX::XMFLOAT3 PrimitiveRenderer::GetPosition() const
{ 
    // Compute the center of bounding box in reference coordinate system.
    XMFLOAT3 center{};
    XMVECTOR position = XMVector3Transform(XMLoadFloat3(&center), XMLoadFloat4x4(&m_frameOfReferenceFromPrimitive));

    XMFLOAT3 pos;
    XMStoreFloat3(&pos, position);

    return pos;
}

// Renders one frame using the vertex and pixel shaders.
// On devices that do not support the D3D11_FEATURE_D3D11_OPTIONS3::
// VPAndRTArrayIndexFromAnyShaderFeedingRasterizer optional feature,
// a pass-through geometry shader is also used to set the render 
// target array index.
void PrimitiveRenderer::Render()
{
    // Loading is asynchronous. Resources must be created before drawing can occur.
    if (!m_loadingComplete || !m_isActive)
    {
        return;
    }

    const auto context = m_deviceResources->GetD3DDeviceContext();

    // Each vertex is one instance of the VertexPositionColor struct.
    const UINT stride = sizeof(VertexPosition);
    const UINT offset = 0;

    // Attach the vertex shader.
    context->VSSetShader(
        m_vertexShader.Get(),
        nullptr,
        0
    );

    // Apply the model constant buffer to the vertex shader.
    context->VSSetConstantBuffers(
        0,
        1,
        m_modelConstantBuffer.GetAddressOf()
    );

    if (!m_usingVprtShaders)
    {
        // On devices that do not support the D3D11_FEATURE_D3D11_OPTIONS3::
        // VPAndRTArrayIndexFromAnyShaderFeedingRasterizer optional feature,
        // a pass-through geometry shader is used to set the render target 
        // array index.
        context->GSSetShader(
            m_geometryShader.Get(),
            nullptr,
            0
        );
    }

    // Attach the pixel shader.
    context->PSSetShader(
        m_pixelShader.Get(),
        nullptr,
        0
    );

    // Draw bounding box.
    if(m_vertexBuffer)
    {
        // Set vertex and index buffer to render bounding box.
        context->IASetVertexBuffers(
            0,
            1,
            m_vertexBuffer.GetAddressOf(),
            &stride,
            &offset
        );
       
        context->IASetPrimitiveTopology(m_primitiveTopology);
        context->IASetInputLayout(m_inputLayout.Get());

        if (m_primitiveTopology == D3D11_PRIMITIVE_TOPOLOGY_POINTLIST)
        {
            // Render point cloud 5 passes with a small shift to "scale" the point size.
            constexpr float c_offset = 0.001f;
            constexpr std::array<XMFLOAT3, 5> c_directions =
            { {
                { -1.0f, -1.0f, -1.0f },
                {  1.0f,  1.0f, -1.0f },
                {  1.0f, -1.0f,  1.0f },
                { -1.0f,  1.0f,  1.0f },
                {  0.0f,  0.0f,  0.0f },
            } };

            for (auto const& dir : c_directions)
            {
                XMMATRIX referenceFromPointCloud =
                    XMMatrixTranslationFromVector(XMLoadFloat3(&dir) * c_offset) *
                    XMLoadFloat4x4(&m_frameOfReferenceFromPrimitive);

                // The view and projection matrices are provided by the system; they are associated
                // with holographic cameras, and updated on a per-camera basis.
                // Here, we provide the model transform for the sample hologram. The model transform
                // matrix is transposed to prepare it for the shader.
                XMStoreFloat4x4(&m_modelConstantBufferData.model, XMMatrixTranspose(referenceFromPointCloud));

                // Update the model transform buffer for the hologram.
                context->UpdateSubresource(
                    m_modelConstantBuffer.Get(),
                    0,
                    nullptr,
                    &m_modelConstantBufferData,
                    0,
                    0
                );

                // Draw the objects.
                context->DrawInstanced(
                    m_vertexCount,      // Point count
                    2,                  // Instance count.
                    0,                  // Start index location.
                    0                   // Start instance location.
                );
            }
        }
        else
        {
            // The view and projection matrices are provided by the system; they are associated
            // with holographic cameras, and updated on a per-camera basis.
            // Here, we provide the model transform for the sample hologram. The model transform
            // matrix is transposed to prepare it for the shader.
            XMStoreFloat4x4(&m_modelConstantBufferData.model, XMMatrixTranspose(XMLoadFloat4x4(&m_frameOfReferenceFromPrimitive)));

            // Update the model transform buffer for the hologram.
            context->UpdateSubresource(
                m_modelConstantBuffer.Get(),
                0,
                nullptr,
                &m_modelConstantBufferData,
                0,
                0
            );

            context->IASetIndexBuffer(
                m_indexBuffer.Get(),
                DXGI_FORMAT_R32_UINT, // Each index is one 32-bit unsigned integer.
                0
            );

            context->RSSetState(m_rasterizerState.Get());

            // Draw the objects.
            context->DrawIndexedInstanced(
                m_indexCount,           // Index count per instance.
                2,                      // Instance count.
                0,                      // Start index location.
                0,                      // Base vertex location.
                0                       // Start instance location.
            );
        }
    }
}

void PrimitiveRenderer::SetActive(bool isActive)
{
    m_isActive = isActive;
}

std::future<void> PrimitiveRenderer::CreateDeviceDependentResources()
{
    m_usingVprtShaders = m_deviceResources->GetDeviceSupportsVprt();

    // On devices that do support the D3D11_FEATURE_D3D11_OPTIONS3::
    // VPAndRTArrayIndexFromAnyShaderFeedingRasterizer optional feature
    // we can avoid using a pass-through geometry shader to set the render
    // target array index, thus avoiding any overhead that would be 
    // incurred by setting the geometry shader stage.
    std::wstring vertexShaderFileName = m_usingVprtShaders ? L"ms-appx:///VprtVertexShader.cso" : L"ms-appx:///VertexShader.cso";

    // Shaders will be loaded asynchronously.

    // After the vertex shader file is loaded, create the shader and input layout.
    std::vector<byte> vertexShaderFileData = co_await ReadDataAsync(vertexShaderFileName);
    winrt::check_hresult(
        m_deviceResources->GetD3DDevice()->CreateVertexShader(
            vertexShaderFileData.data(),
            vertexShaderFileData.size(),
            nullptr,
            &m_vertexShader
        ));

    constexpr std::array<D3D11_INPUT_ELEMENT_DESC, 1> vertexDesc =
        { {
            { "POSITION", 0, DXGI_FORMAT_R32G32B32_FLOAT, 0,  0, D3D11_INPUT_PER_VERTEX_DATA, 0 },
        } };

    winrt::check_hresult(
        m_deviceResources->GetD3DDevice()->CreateInputLayout(
            vertexDesc.data(),
            static_cast<UINT>(vertexDesc.size()),
            vertexShaderFileData.data(),
            static_cast<UINT>(vertexShaderFileData.size()),
            &m_inputLayout
        ));

    // After the pixel shader file is loaded, create the shader and constant buffer.
    std::vector<byte> pixelShaderFileData = co_await ReadDataAsync(L"ms-appx:///PixelShader.cso");
    winrt::check_hresult(
        m_deviceResources->GetD3DDevice()->CreatePixelShader(
            pixelShaderFileData.data(),
            pixelShaderFileData.size(),
            nullptr,
            &m_pixelShader
        ));

    const CD3D11_BUFFER_DESC constantBufferDesc(sizeof(ModelConstantBuffer), D3D11_BIND_CONSTANT_BUFFER);
    winrt::check_hresult(
        m_deviceResources->GetD3DDevice()->CreateBuffer(
            &constantBufferDesc,
            nullptr,
            &m_modelConstantBuffer
        ));


    if (!m_usingVprtShaders)
    {
        // Load the pass-through geometry shader.
        std::vector<byte> geometryShaderFileData = co_await ReadDataAsync(L"ms-appx:///GeometryShader.cso");

        // After the pass-through geometry shader file is loaded, create the shader.
        winrt::check_hresult(
            m_deviceResources->GetD3DDevice()->CreateGeometryShader(
                geometryShaderFileData.data(),
                geometryShaderFileData.size(),
                nullptr,
                &m_geometryShader
            ));
    }

    // Create a rasterizer to draw wire frame.
    CD3D11_RASTERIZER_DESC rasterizerDesc{};
    rasterizerDesc.FillMode = D3D11_FILL_WIREFRAME;
    rasterizerDesc.CullMode = D3D11_CULL_NONE;

    winrt::check_hresult(
        m_deviceResources->GetD3DDevice()->CreateRasterizerState(&rasterizerDesc, &m_rasterizerState));

    // Once the cube is loaded, the object is ready to be rendered.
    m_loadingComplete = true;
};

void AoaSampleApp::PrimitiveRenderer::RecreateVertexAndIndexBuffers(uint32_t vertexCount, uint32_t indexCount)
{
    m_vertexBuffer.Reset();
    m_indexBuffer.Reset();
    
    if (vertexCount == 0 || indexCount == 0)
    {
        return;
    }
    
    // Allocate our CPU side data that represents the intial buffer data.
    m_volumeVertices.resize(vertexCount);
    m_volumeIndices.resize(indexCount);

    // Create the buffers for storing the geometry. Let D3D know that we may wish to write updated information into the buffers.
    D3D11_SUBRESOURCE_DATA vertexBufferData = { 0 };
    vertexBufferData.pSysMem = m_volumeVertices.data();
    vertexBufferData.SysMemPitch = 0;
    vertexBufferData.SysMemSlicePitch = 0;
    CD3D11_BUFFER_DESC vertexBufferDesc(sizeof(VertexPosition) * vertexCount, D3D11_BIND_VERTEX_BUFFER);
    vertexBufferDesc.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
    vertexBufferDesc.Usage = D3D11_USAGE_DYNAMIC;
    winrt::check_hresult(
        m_deviceResources->GetD3DDevice()->CreateBuffer(
            &vertexBufferDesc,
            &vertexBufferData,
            &m_vertexBuffer
        ));

    D3D11_SUBRESOURCE_DATA indexBufferData = { 0 };
    indexBufferData.pSysMem = m_volumeIndices.data();
    indexBufferData.SysMemPitch = 0;
    indexBufferData.SysMemSlicePitch = 0;
    CD3D11_BUFFER_DESC indexBufferDesc(sizeof(uint32_t) * indexCount, D3D11_BIND_INDEX_BUFFER);
    indexBufferDesc.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
    indexBufferDesc.Usage = D3D11_USAGE_DYNAMIC;
    
    winrt::check_hresult(
        m_deviceResources->GetD3DDevice()->CreateBuffer(
            &indexBufferDesc,
            &indexBufferData,
            &m_indexBuffer
        ));
}

void PrimitiveRenderer::ReleaseDeviceDependentResources()
{
    m_loadingComplete = false;
    m_usingVprtShaders = false;
    m_vertexCount = 0;
    m_indexCount = 0;
    m_vertexShader.Reset();
    m_inputLayout.Reset();
    m_pixelShader.Reset();
    m_geometryShader.Reset();
    m_modelConstantBuffer.Reset();
    m_vertexBuffer.Reset();
    m_indexBuffer.Reset();
    m_rasterizerState.Reset();
}
