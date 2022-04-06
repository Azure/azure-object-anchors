// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
#include "pch.h"
#include "Common/DirectXHelper.h"
#include "Content/GeometricPrimitives.h"
#include "AoaSampleAppMain.h"

#include <windows.graphics.directx.direct3d11.interop.h>
#include <winrt/Windows.Storage.Search.h>
#include <winrt/Windows.Data.Json.h>
#include <winrt/Windows.UI.Popups.h>

using namespace AoaSampleApp;
using namespace concurrency;
using namespace Microsoft::WRL;
using namespace std::placeholders;
using namespace winrt::Microsoft::Azure::ObjectAnchors;
using namespace winrt::Microsoft::Azure::ObjectAnchors::SpatialGraph;
using namespace winrt::Windows::Data::Json;
using namespace winrt::Windows::Foundation::Numerics;
using namespace winrt::Windows::Gaming::Input;
using namespace winrt::Windows::Graphics::Holographic;
using namespace winrt::Windows::Graphics::DirectX::Direct3D11;
using namespace winrt::Windows::Perception::Spatial;
using namespace winrt::Windows::UI::Input::Spatial;
using namespace winrt::Windows::Foundation::Metadata;
using namespace winrt::Windows::Storage;

namespace
{
    // Name of the file in application local cache that turns on diagnostics.
    constexpr WCHAR* c_DebugFilename = L"debug";
    constexpr WCHAR* c_ConfigurationFilename = L"ms-appx:///ObjectAnchorsConfig.json";

    winrt::guid TryParseGuid(winrt::hstring const& value)
    {
        if (value.size() != 36 || value[8] != '-' || value[13] != '-' || value[18] != '-' || value[23] != '-')
        {
            throw std::invalid_argument(winrt::to_string(value) + " is not a valid GUID");
        }
        return winrt::guid{ value };
    }

    AccountInformation TryParseAccountInformation(winrt::hstring const& configuration) try
    {
        auto json = winrt::Windows::Data::Json::JsonObject::Parse(configuration);

        // Throws an exception if the name doesn't exist in the json object.
        return {
            TryParseGuid(json.GetNamedString(L"AccountId")),
            json.GetNamedString(L"AccountKey"),
            json.GetNamedString(L"AccountDomain")
        };
    }
    catch (...) { return nullptr; }

    static inline DirectX::BoundingOrientedBox GetObjectModelBoundingBox(winrt::Microsoft::Azure::ObjectAnchors::ObjectModel const& model)
    {
        DirectX::BoundingOrientedBox boundingBox{};

        auto sourceBoundingBox = model.BoundingBox();
        memcpy(&boundingBox.Center, &sourceBoundingBox.Center, sizeof(sourceBoundingBox.Center));
        memcpy(&boundingBox.Extents, &sourceBoundingBox.Extents, sizeof(sourceBoundingBox.Extents));
        memcpy(&boundingBox.Orientation, &sourceBoundingBox.Orientation, sizeof(sourceBoundingBox.Orientation));

        // ObjectSptialBoundingOrientedBox uses edge-to-edge length as extent, while DirectX uses half width as extent.
        boundingBox.Extents.x *= 0.5f;
        boundingBox.Extents.y *= 0.5f;
        boundingBox.Extents.z *= 0.5f;

        return boundingBox;
    }
}

///////////////////////////////////////////////////////////////////////////////////////////////////
// ObjectRender

bool AoaSampleAppMain::ObjectRenderer::IsActive() const
{
    return BoundingBoxRenderer->IsActive() || PointCloudRenderer->IsActive();
}

void AoaSampleAppMain::ObjectRenderer::SetActive(bool flag)
{
    BoundingBoxRenderer->SetActive(flag);
    PointCloudRenderer->SetActive(flag);
}

void AoaSampleAppMain::ObjectRenderer::SetTransform(float4x4 const& frameOfReferenceFromObject)
{
    BoundingBoxRenderer->SetTransform(frameOfReferenceFromObject);
    PointCloudRenderer->SetTransform(frameOfReferenceFromObject);
}

void AoaSampleAppMain::ObjectRenderer::CreateDeviceDependentResources()
{
    BoundingBoxRenderer->CreateDeviceDependentResources();
    PointCloudRenderer->CreateDeviceDependentResources();
}

void AoaSampleAppMain::ObjectRenderer::ReleaseDeviceDependentResources()
{
    BoundingBoxRenderer->ReleaseDeviceDependentResources();
    PointCloudRenderer->ReleaseDeviceDependentResources();
}

void AoaSampleAppMain::ObjectRenderer::Render()
{
    BoundingBoxRenderer->Render();
    PointCloudRenderer->Render();
}

float3 AoaSampleAppMain::ObjectRenderer::GetPosition() const
{
    float3 position{};

    if (PointCloudRenderer->IsActive())
    {
        position = PointCloudRenderer->GetPosition();
    }
    else if (BoundingBoxRenderer->IsActive())
    {
        position = BoundingBoxRenderer->GetPosition();
    }

    return position;
}

///////////////////////////////////////////////////////////////////////////////////////////////////
// AoaSampleAppMain

// Loads and initializes application assets when the application is loaded.
AoaSampleAppMain::AoaSampleAppMain(std::shared_ptr<DeviceResources> const& deviceResources)
    : m_deviceResources(deviceResources)
{
    // Register to be notified if the device is lost or recreated.
    m_deviceResources->RegisterDeviceNotify(this);

    m_canGetHolographicDisplayForCamera = ApiInformation::IsPropertyPresent(winrt::name_of<HolographicCamera>(), L"Display");
    m_canGetDefaultHolographicDisplay = ApiInformation::IsMethodPresent(winrt::name_of<HolographicDisplay>(), L"GetDefault");
    m_canCommitDirect3D11DepthBuffer = ApiInformation::IsMethodPresent(winrt::name_of<HolographicCameraRenderingParameters>(), L"CommitDirect3D11DepthBuffer");
    m_canUseWaitForNextFrameReadyAPI = ApiInformation::IsMethodPresent(winrt::name_of<HolographicSpace>(), L"WaitForNextFrameReady");

    if (m_canGetDefaultHolographicDisplay)
    {
        // Subscribe for notifications about changes to the state of the default HolographicDisplay
        // and its SpatialLocator.
        m_holographicDisplayIsAvailableChangedEventToken = HolographicSpace::IsAvailableChanged(bind(&AoaSampleAppMain::OnHolographicDisplayIsAvailableChanged, this, _1, _2));
    }

    // Acquire the current state of the default HolographicDisplay and its SpatialLocator.
    OnHolographicDisplayIsAvailableChanged(nullptr, nullptr);
}

winrt::Windows::Foundation::IAsyncAction AoaSampleAppMain::InitializeAsync()
{
    // Parse account id, key and domain
    auto configuration = co_await PathIO::ReadTextAsync(c_ConfigurationFilename);

    AccountInformation accountInformation = TryParseAccountInformation(configuration);
    if (!accountInformation)
    {
        winrt::Windows::UI::Popups::MessageDialog message(
            L"Please update ObjectAnchorsConfig.json in the Assets folder of the project.",
            L"Invalid account information");
        co_await message.ShowAsync();
        co_return;
    }

    m_objectTrackerPtr = std::make_unique<ObjectTracker>(accountInformation);

    co_await LoadObjectModelAsync(ApplicationData::Current().LocalFolder());
    co_await LoadObjectModelAsync(KnownFolders::Objects3D());

    // Turn on diagnostics if a "debug" file existing in the local cache.
    // This check is required to be after loading models, otherwise the diagnostics session will not
    // include object models.
    co_await TurnonDiagnosticsIfRequiredAsync();
}

AoaSampleAppMain::~AoaSampleAppMain()
{
    m_objectTrackerPtr.reset();

    m_objectRenderers.clear();
    m_boundsRenderer.reset();

    // Deregister device notification.
    m_deviceResources->RegisterDeviceNotify(nullptr);

    UnregisterHolographicEventHandlers();

    HolographicSpace::IsAvailableChanged(m_holographicDisplayIsAvailableChangedEventToken);
}

void AoaSampleAppMain::SetHolographicSpace(HolographicSpace const& holographicSpace)
{
    UnregisterHolographicEventHandlers();

    m_holographicSpace = holographicSpace;

    //
    // TODO: Add code here to initialize your holographic content.
    //

#ifdef DRAW_SAMPLE_CONTENT
    // Initialize the sample hologram.
    m_boundsRenderer = std::make_unique<PrimitiveRenderer>(m_deviceResources);
    m_spatialInputHandler = std::make_unique<SpatialInputHandler>();
#endif

    // Respond to camera added events by creating any resources that are specific
    // to that camera, such as the back buffer render target view.
    // When we add an event handler for CameraAdded, the API layer will avoid putting
    // the new camera in new HolographicFrames until we complete the deferral we created
    // for that handler, or return from the handler without creating a deferral. This
    // allows the app to take more than one frame to finish creating resources and
    // loading assets for the new holographic camera.
    // This function should be registered before the app creates any HolographicFrames.
    m_cameraAddedToken = m_holographicSpace.CameraAdded(std::bind(&AoaSampleAppMain::OnCameraAdded, this, _1, _2));

    // Respond to camera removed events by releasing resources that were created for that
    // camera.
    // When the app receives a CameraRemoved event, it releases all references to the back
    // buffer right away. This includes render target views, Direct2D target bitmaps, and so on.
    // The app must also ensure that the back buffer is not attached as a render target, as
    // shown in DeviceResources::ReleaseResourcesForBackBuffer.
    m_cameraRemovedToken = m_holographicSpace.CameraRemoved(std::bind(&AoaSampleAppMain::OnCameraRemoved, this, _1, _2));

    // Notes on spatial tracking APIs:
    // * Stationary reference frames are designed to provide a best-fit position relative to the
    //   overall space. Individual positions within that reference frame are allowed to drift slightly
    //   as the device learns more about the environment.
    // * When precise placement of individual holograms is required, a SpatialAnchor should be used to
    //   anchor the individual hologram to a position in the real world - for example, a point the user
    //   indicates to be of special interest. Anchor positions do not drift, but can be corrected; the
    //   anchor will use the corrected position starting in the next frame after the correction has
    //   occurred.
}

void AoaSampleAppMain::UnregisterHolographicEventHandlers()
{
    if (m_holographicSpace != nullptr)
    {
        // Clear previous event registrations.
        m_holographicSpace.CameraAdded(m_cameraAddedToken);
        m_cameraAddedToken = {};
        m_holographicSpace.CameraRemoved(m_cameraRemovedToken);
        m_cameraRemovedToken = {};
    }

    if (m_spatialLocator != nullptr)
    {
        m_spatialLocator.LocatabilityChanged(m_locatabilityChangedToken);
    }
}

winrt::Windows::Foundation::IAsyncAction AoaSampleAppMain::LoadObjectModelAsync(const StorageFolder& rootFolder)
{
    // Round-trip through the path to ensure consistent access to known folders like 3D Objects.
    const auto rootFolderByPath = co_await StorageFolder::GetFolderFromPathAsync(rootFolder.Path());

    for (auto const& item : co_await rootFolderByPath.GetItemsAsync())
    {
        if (item.IsOfType(StorageItemTypes::Folder))
        {
            co_await LoadObjectModelAsync(item.as<StorageFolder>());
            continue;
        }

        const auto file = item.as<StorageFile>();
        if (file.FileType() != L".ou")
        {
            continue;
        }

        const auto id = co_await m_objectTrackerPtr->AddObjectModelAsync(file);
        const auto model = m_objectTrackerPtr->GetObjectModel(id);

#ifdef DRAW_SAMPLE_CONTENT
        ObjectRenderer renderer;
        renderer.BoundingBoxRenderer = std::make_unique<PrimitiveRenderer>(m_deviceResources);
        renderer.PointCloudRenderer = std::make_unique<PrimitiveRenderer>(m_deviceResources);

        // Setup bounding box renderer
        {
            std::vector<DirectX::XMFLOAT3> vertices;
            std::vector<uint32_t> indices;
            GetBoundingBoxVerticesAndIndices(model.BoundingBox(), vertices, indices);

            renderer.BoundingBoxRenderer->SetVerticesAndIndices(
                vertices.data(),
                static_cast<uint32_t>(vertices.size()),
                indices.data(),
                static_cast<uint32_t>(indices.size()),
                D3D11_PRIMITIVE_TOPOLOGY_LINELIST
            );

            renderer.BoundingBoxRenderer->SetColor(c_Magenta);
        }

        // Setup model point cloud renderer
        {
            std::vector<float3> vertices(model.VertexCount());
            model.GetVertexPositions(vertices);

            D3D_PRIMITIVE_TOPOLOGY topology;
            std::vector<uint32_t> indices;

            if (model.TriangleIndexCount() == 0)
            {
                indices.resize(vertices.size());
                std::iota(indices.begin(), indices.end(), uint32_t(0));

                topology = D3D11_PRIMITIVE_TOPOLOGY_POINTLIST;
            }
            else
            {
                indices.resize(model.TriangleIndexCount());
                model.GetTriangleIndices(indices);

                topology = D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST;
            }

            renderer.PointCloudRenderer->SetVerticesAndIndices(
                reinterpret_cast<DirectX::XMFLOAT3*>(vertices.data()),
                static_cast<uint32_t>(vertices.size()),
                indices.data(),
                static_cast<uint32_t>(indices.size()),
                topology
            );

            renderer.PointCloudRenderer->SetColor(c_Magenta);
        }

        m_objectRenderers.emplace(id, std::move(renderer));
#endif //DRAW_SAMPLE_CONTENT
    }
}

winrt::Windows::Foundation::IAsyncAction AoaSampleAppMain::TurnonDiagnosticsIfRequiredAsync()
{
    // Check if a file named "debug" existing in the local cache folder or the 3D Objects folder
    // If exists, turn on diagnostics, otherwise turn it off.

    if (co_await ApplicationData::Current().LocalFolder().TryGetItemAsync(c_DebugFilename) ||
        co_await KnownFolders::Objects3D().TryGetItemAsync(c_DebugFilename))
    {
        co_await m_objectTrackerPtr->StartDiagnosticsAsync();
    }
    else
    {
        // No side effect of calling StopDiagnostics multiple times.
        co_await StopAndUploadDiagnosticsAsync();
    }
}

winrt::Windows::Foundation::IAsyncAction AoaSampleAppMain::StopAndUploadDiagnosticsAsync()
{
    const auto diagnosticsFilePath = co_await m_objectTrackerPtr->StopDiagnosticsAsync();

    if (diagnosticsFilePath.empty())
    {
        // Diagnostics is not captured, skip uploading.
        co_return;
    }

    co_await m_objectTrackerPtr->UploadDiagnosticsAsync(diagnosticsFilePath);
}

winrt::Windows::Foundation::IAsyncAction AoaSampleAppMain::UpdateObjectSearchArea(winrt::Windows::Perception::People::HeadPose const& headPose)
{
    if (m_initializeOperation)
    {
        co_await m_initializeOperation;
    }

    if (m_searchAreaOperation)
    {
        co_await m_searchAreaOperation;
    }

    //
    // Compute and update location.
    //

    using namespace DirectX;

    enum class ObjectTrackingBoundingVolumeKind
    {
        Sphere,
        OrientedBox,
        FieldOfView,
    };

    static constexpr std::array<ObjectTrackingBoundingVolumeKind, 3> c_boundingVolumeKinds =
    {
        {
            ObjectTrackingBoundingVolumeKind::Sphere,
            ObjectTrackingBoundingVolumeKind::OrientedBox,
            ObjectTrackingBoundingVolumeKind::FieldOfView,
        }
    };

    // Choose next bounding volume kind.
    static int64_t s_pointerPressedCount = 0;
    const auto requiredBoundingVolumeKind = c_boundingVolumeKinds[s_pointerPressedCount % c_boundingVolumeKinds.size()];
    s_pointerPressedCount += 1;

    // Compute bounding volume in the reference coordinate frame based on head location.
    auto frameOfReference = Preview::SpatialGraphInteropPreview::TryCreateFrameOfReference(m_stationaryReferenceFrame.CoordinateSystem());
    SpatialGraphCoordinateSystem coordinateSystem;
    coordinateSystem.NodeId = frameOfReference.NodeId();
    coordinateSystem.CoordinateSystemToNodeTransform = frameOfReference.CoordinateSystemToNodeTransform();

    const float3 headPosition = headPose.Position();
    const float3 headForwardDirection = headPose.ForwardDirection();
    const float3 headUpDirection = headPose.UpDirection();

    constexpr float c_observationDistance = 2.0f;
    const float3 boundsPosition = headPosition + (c_observationDistance * headForwardDirection);

    // Bounding box will be vertical aligned while field of view can have arbitrary orientation.
    const float4x4 frameOfReferenceFromBounds =
        requiredBoundingVolumeKind == ObjectTrackingBoundingVolumeKind::FieldOfView ?
        make_float4x4_look_at(headPosition, boundsPosition, headUpDirection) :
        make_float4x4_look_at(float3(headPosition.x, boundsPosition.y, headPosition.z), boundsPosition, float3::unit_y());

    // Bounding box.
    // Note that ObjectSptialBoundingOrientedBox uses edge-to-edge length as extent, while DirectX uses half width as extent.
    SpatialOrientedBox boundingBox;
    boundingBox.Center = boundsPosition;
    boundingBox.Extents = float3(4.0f);
    boundingBox.Orientation = inverse(make_quaternion_from_rotation_matrix(frameOfReferenceFromBounds));

    // Field of view.
    SpatialFieldOfView fieldOfView;
    fieldOfView.Position = headPosition;
    fieldOfView.Orientation = inverse(make_quaternion_from_rotation_matrix(frameOfReferenceFromBounds));
    fieldOfView.HorizontalFieldOfViewInDegrees = 75.0f;
    fieldOfView.AspectRatio = 1.0f;
    fieldOfView.FarDistance = 4.0f;

    // Sphere at head
    SpatialSphere sphere;
    sphere.Center = boundsPosition;
    sphere.Radius = 2.0f;

    // Apply bounds to object tracker and object model.
    constexpr float c_relaxScale = 1.50f;
    constexpr float c_minHorizontalFov = 75.0f;
    constexpr float c_maxHorizontalFov = 180.0f;

    // Find maximum bounding box and field of view large enough to cover all objects.
    float requiredScale = 1.0f;
    float maxModelExtent = 0.0f;
    XMFLOAT3 requiredMaxExtents{ 0.0f, 0.0f, 0.0f };

    for (auto const& renderer : m_objectRenderers)
    {
        const auto modelId = renderer.first;
        const auto model = m_objectTrackerPtr->GetObjectModel(modelId);
        const auto modelBounds = GetObjectModelBoundingBox(model);

        requiredMaxExtents.x = (std::max)(requiredMaxExtents.x, modelBounds.Extents.x);
        requiredMaxExtents.y = (std::max)(requiredMaxExtents.y, modelBounds.Extents.y);
        requiredMaxExtents.z = (std::max)(requiredMaxExtents.z, modelBounds.Extents.z);

        const float maxExtent = (std::max)((std::max)(modelBounds.Extents.x, modelBounds.Extents.y), modelBounds.Extents.z);

        if (maxExtent > maxModelExtent)
        {
            const float diagonalExtent = XMVectorGetX(XMVector3Length(XMLoadFloat3(&modelBounds.Extents)));
            const float requiredMaxExtent = diagonalExtent * c_relaxScale;

            requiredScale = requiredMaxExtent / maxExtent;
            maxModelExtent = maxExtent;
        }
    }

    if (m_objectRenderers.empty())
    {
        requiredMaxExtents.x = requiredMaxExtents.y = requiredMaxExtents.z = 2.0f;
    }

    // Bounding volume's vertices and indices, for rendering.
    std::vector<XMFLOAT3> boundingVolumeVertices;
    std::vector<uint32_t> boundingVolumeVertexIndices;
    XMFLOAT4 boundingVolumeColor = c_White;

    ObjectSearchArea searchArea{ nullptr };

    if (requiredBoundingVolumeKind == ObjectTrackingBoundingVolumeKind::OrientedBox)
    {
        boundingBox.Extents.x = requiredMaxExtents.x * requiredScale * 2.0f;
        boundingBox.Extents.y = requiredMaxExtents.y * requiredScale * 2.0f;
        boundingBox.Extents.z = requiredMaxExtents.z * requiredScale * 2.0f;

        boundingVolumeColor = c_White;
        GetBoundingBoxVerticesAndIndices(boundingBox, boundingVolumeVertices, boundingVolumeVertexIndices);

        searchArea = ObjectSearchArea::FromOrientedBox(coordinateSystem, boundingBox);
    }
    else if (requiredBoundingVolumeKind == ObjectTrackingBoundingVolumeKind::FieldOfView)
    {
        const float horizontalFov = 2.0f * atanf(maxModelExtent / c_observationDistance);
        const float aspectRatio = 1.0f;

        fieldOfView.HorizontalFieldOfViewInDegrees = (std::min)((std::max)(horizontalFov * 180.0f / 3.1415926f, c_minHorizontalFov), c_maxHorizontalFov);
        fieldOfView.AspectRatio = aspectRatio;
        fieldOfView.FarDistance = c_observationDistance + maxModelExtent * 1.5f;

        boundingVolumeColor = c_White;
        GetFieldOfViewVerticesAndIndices(fieldOfView, boundingVolumeVertices, boundingVolumeVertexIndices);

        searchArea = ObjectSearchArea::FromFieldOfView(coordinateSystem, fieldOfView);
    }
    else if (requiredBoundingVolumeKind == ObjectTrackingBoundingVolumeKind::Sphere)
    {
        boundingVolumeColor = c_White;
        GetSphereVerticesAndIndices(sphere, 15, true, boundingVolumeVertices, boundingVolumeVertexIndices);

        searchArea = ObjectSearchArea::FromSphere(coordinateSystem, sphere);
    }

    m_boundsRenderer->SetVerticesAndIndices(
        boundingVolumeVertices.data(),
        static_cast<uint32_t>(boundingVolumeVertices.size()),
        boundingVolumeVertexIndices.data(),
        static_cast<uint32_t>(boundingVolumeVertexIndices.size()),
        D3D11_PRIMITIVE_TOPOLOGY_LINELIST);

    m_boundsRenderer->SetColor(boundingVolumeColor);
    m_boundsRenderer->SetActive(!boundingVolumeVertices.empty() && !boundingVolumeVertexIndices.empty());

    m_lastSearchArea = searchArea;
    co_await m_objectTrackerPtr->DetectAsync(frameOfReference, searchArea);
}

// Updates the application state once per frame.
HolographicFrame AoaSampleAppMain::Update(HolographicFrame const& previousFrame)
{
    if (!m_initializeOperation)
    {
        // Load object model and start tracking objects.
        m_initializeOperation = InitializeAsync();
    }

    // TODO: Put CPU work that does not depend on the HolographicCameraPose here.

    // Apps should wait for the optimal time to begin pose-dependent work.
    // The platform will automatically adjust the wakeup time to get
    // the lowest possible latency at high frame rates. For manual
    // control over latency, use the WaitForNextFrameReadyWithHeadStart
    // API.
    // WaitForNextFrameReady and WaitForNextFrameReadyWithHeadStart are the
    // preferred frame synchronization APIs for Windows Mixed Reality. When
    // running on older versions of the OS that do not include support for
    // these APIs, your app can use the WaitForFrameToFinish API for similar
    // (but not as optimal) behavior.
    if (m_canUseWaitForNextFrameReadyAPI)
    {
        try
        {
            m_holographicSpace.WaitForNextFrameReady();
        }
        catch (winrt::hresult_not_implemented const& /*ex*/)
        {
            // Catch a specific case where WaitForNextFrameReady() is present but not implemented
            // and default back to WaitForFrameToFinish() in that case.
            m_canUseWaitForNextFrameReadyAPI = false;
        }
    }
    else if (previousFrame)
    {
        previousFrame.WaitForFrameToFinish();
    }

    // Before doing the timer update, there is some work to do per-frame
    // to maintain holographic rendering. First, we will get information
    // about the current frame.

    // The HolographicFrame has information that the app needs in order
    // to update and render the current frame. The app begins each new
    // frame by calling CreateNextFrame.
    HolographicFrame holographicFrame = m_holographicSpace.CreateNextFrame();

    // Get a prediction of where holographic cameras will be when this frame
    // is presented.
    HolographicFramePrediction prediction = holographicFrame.CurrentPrediction();

    // Back buffers can change from frame to frame. Validate each buffer, and recreate
    // resource views and depth buffers as needed.
    m_deviceResources->EnsureCameraResources(holographicFrame, prediction);

    // Tracked objects at current time.
    std::vector<TrackedObject> trackedObjects;

#ifdef DRAW_SAMPLE_CONTENT
    if (m_stationaryReferenceFrame && m_objectTrackerPtr)
    {
        SpatialInteractionSourceState pointerState = m_spatialInputHandler->CheckForInput();

        if (pointerState != nullptr && pointerState.Source().Kind() == SpatialInteractionSourceKind::Hand)
        {
            SpatialPointerPose pose = pointerState.TryGetPointerPose(m_stationaryReferenceFrame.CoordinateSystem());

            if (pose != nullptr)
            {
                // Check update event internal to avoid accidental events.
                using namespace std::chrono;

                constexpr uint32_t c_minPointerTimeIntervalInSeconds = 2;

                static system_clock::time_point previousPointerTime{};
                system_clock::time_point currentTime = system_clock::now();

                if (duration_cast<seconds>(currentTime - previousPointerTime).count() >= c_minPointerTimeIntervalInSeconds)
                {
                    previousPointerTime = currentTime;

                    if (pointerState.Source().Handedness() == SpatialInteractionSourceHandedness::Right)
                    {
                        // Update search area by air-tap with right hand.
                        m_searchAreaOperation = UpdateObjectSearchArea(pose.Head());
                    }
                    else if (pointerState.Source().Handedness() == SpatialInteractionSourceHandedness::Left)
                    {
                        // Switch tracking mode by air-tap with left hand.
                        // Note that object mesh will be rendered at different colors in accordance with the modes.

                        DirectX::XMFLOAT4 meshColor;

                        auto mode = m_objectTrackerPtr->GetInstanceTrackingMode();

                        if (mode == ObjectInstanceTrackingMode::LowLatencyCoarsePosition)
                        {
                            meshColor = c_Yellow;
                            m_objectTrackerPtr->SetInstanceTrackingMode(ObjectInstanceTrackingMode::HighLatencyAccuratePosition);
                        }
                        else if (mode == ObjectInstanceTrackingMode::HighLatencyAccuratePosition)
                        {
                            meshColor = c_Magenta;
                            m_objectTrackerPtr->SetInstanceTrackingMode(ObjectInstanceTrackingMode::LowLatencyCoarsePosition);
                        }

                        // Update renderer to render new color.
                        for (auto& renderer : m_objectRenderers)
                        {
                            renderer.second.PointCloudRenderer->SetColor(meshColor);
                        }
                    }
                }
            }
        }

        // Get currently detected objects.
        trackedObjects = m_objectTrackerPtr->GetTrackedObjects(m_stationaryReferenceFrame.CoordinateSystem());
    }

#endif

    m_timer.Tick([this, &trackedObjects, &prediction]()
    {
        //
        // TODO: Update scene objects.
        //
        // Put time-based updates here. By default this code will run once per frame,
        // but if you change the StepTimer to use a fixed time step this code will
        // run as many times as needed to get to the current step.
        //

#ifdef DRAW_SAMPLE_CONTENT
        const SpatialLocation viewLocation = m_spatialLocator.TryLocateAtTimestamp(prediction.Timestamp(), m_stationaryReferenceFrame.CoordinateSystem());

        for (auto& renderer : m_objectRenderers)
        {
            auto it = std::find_if(trackedObjects.cbegin(), trackedObjects.cend(), [&renderer](auto const& obj)
            {
                return renderer.first == obj.ModelId;
            });

            if (it == trackedObjects.cend())
            {
                renderer.second.SetActive(false);
            }
            else
            {
                const SpatialPose modelPose = it->ComputeOriginForView({ viewLocation.Position(), viewLocation.Orientation() }, it->CoordinateSystemToPlacement);

                renderer.second.SetTransform(make_float4x4_from_quaternion(modelPose.Orientation) * make_float4x4_translation(modelPose.Position));
                renderer.second.SetActive(true);
            }
        }
#endif
    });

    // On HoloLens 2, the platform can achieve better image stabilization results if it has
    // a stabilization plane and a depth buffer.
    // Note that the SetFocusPoint API includes an override which takes velocity as a
    // parameter. This is recommended for stabilizing holograms in motion.
    for (HolographicCameraPose const& cameraPose : prediction.CameraPoses())
    {
#ifdef DRAW_SAMPLE_CONTENT
        // The HolographicCameraRenderingParameters class provides access to set
        // the image stabilization parameters.
        HolographicCameraRenderingParameters renderingParameters = holographicFrame.GetRenderingParameters(cameraPose);

        // SetFocusPoint informs the system about a specific point in your scene to
        // prioritize for image stabilization. The focus point is set independently
        // for each holographic camera. When setting the focus point, put it on or
        // near content that the user is looking at.
        // In this example, we put the focus point at the center of the sample hologram.
        // You can also set the relative velocity and facing of the stabilization
        // plane using overloads of this method.
        if (m_stationaryReferenceFrame != nullptr)
        {
            auto it = std::find_if(m_objectRenderers.cbegin(), m_objectRenderers.cend(), [](auto const& renderer)
            {
                return renderer.second.IsActive();
            });

            if (it != m_objectRenderers.cend())
            {
                renderingParameters.SetFocusPoint(
                    m_stationaryReferenceFrame.CoordinateSystem(),
                    it->second.GetPosition()
                );
            }
        }
#endif
    }

    // The holographic frame will be used to get up-to-date view and projection matrices and
    // to present the swap chain.
    return holographicFrame;
}

// Renders the current frame to each holographic camera, according to the
// current application and spatial positioning state. Returns true if the
// frame was rendered to at least one camera.
bool AoaSampleAppMain::Render(HolographicFrame const& holographicFrame)
{
    // Don't try to render anything before the first Update.
    if (m_timer.GetFrameCount() == 0)
    {
        return false;
    }

    //
    // TODO: Add code for pre-pass rendering here.
    //
    // Take care of any tasks that are not specific to an individual holographic
    // camera. This includes anything that doesn't need the final view or projection
    // matrix, such as lighting maps.
    //

    // Lock the set of holographic camera resources, then draw to each camera
    // in this frame.
    return m_deviceResources->UseHolographicCameraResources<bool>(
        [this, holographicFrame](std::map<UINT32, std::unique_ptr<CameraResources>>& cameraResourceMap)
    {
        // Up-to-date frame predictions enhance the effectiveness of image stablization and
        // allow more accurate positioning of holograms.
        holographicFrame.UpdateCurrentPrediction();
        HolographicFramePrediction prediction = holographicFrame.CurrentPrediction();

        bool atLeastOneCameraRendered = false;
        for (HolographicCameraPose const& cameraPose : prediction.CameraPoses())
        {
            // This represents the device-based resources for a HolographicCamera.
            CameraResources* pCameraResources = cameraResourceMap[cameraPose.HolographicCamera().Id()].get();

            // Get the device context.
            const auto context = m_deviceResources->GetD3DDeviceContext();
            const auto depthStencilView = pCameraResources->GetDepthStencilView();

            // Set render targets to the current holographic camera.
            ID3D11RenderTargetView *const targets[1] = { pCameraResources->GetBackBufferRenderTargetView() };
            context->OMSetRenderTargets(1, targets, depthStencilView);

            // Clear the back buffer and depth stencil view.
            if (m_canGetHolographicDisplayForCamera &&
                cameraPose.HolographicCamera().Display().IsOpaque())
            {
                context->ClearRenderTargetView(targets[0], DirectX::Colors::CornflowerBlue);
            }
            else
            {
                context->ClearRenderTargetView(targets[0], DirectX::Colors::Transparent);
            }
            context->ClearDepthStencilView(depthStencilView, D3D11_CLEAR_DEPTH | D3D11_CLEAR_STENCIL, 1.0f, 0);

            //
            // TODO: Replace the sample content with your own content.
            //
            // Notes regarding holographic content:
            //    * For drawing, remember that you have the potential to fill twice as many pixels
            //      in a stereoscopic render target as compared to a non-stereoscopic render target
            //      of the same resolution. Avoid unnecessary or repeated writes to the same pixel,
            //      and only draw holograms that the user can see.
            //    * To help occlude hologram geometry, you can create a depth map using geometry
            //      data obtained via the surface mapping APIs. You can use this depth map to avoid
            //      rendering holograms that are intended to be hidden behind tables, walls,
            //      monitors, and so on.
            //    * On HolographicDisplays that are transparent, black pixels will appear transparent
            //      to the user. On such devices, you should clear the screen to Transparent as shown
            //      above. You should still use alpha blending to draw semitransparent holograms.
            //


            // The view and projection matrices for each holographic camera will change
            // every frame. This function refreshes the data in the constant buffer for
            // the holographic camera indicated by cameraPose.
            if (m_stationaryReferenceFrame)
            {
                pCameraResources->UpdateViewProjectionBuffer(m_deviceResources, cameraPose, m_stationaryReferenceFrame.CoordinateSystem());
            }

            // Attach the view/projection constant buffer for this camera to the graphics pipeline.
            bool cameraActive = pCameraResources->AttachViewProjectionBuffer(m_deviceResources);

#ifdef DRAW_SAMPLE_CONTENT
            // Only render world-locked content when positional tracking is active.
            if (cameraActive)
            {
                m_boundsRenderer->Render();

                // Draw object bounding box.
                for (auto& renderer : m_objectRenderers)
                {
                    renderer.second.Render();
                }

                if (m_canCommitDirect3D11DepthBuffer)
                {
                    // On versions of the platform that support the CommitDirect3D11DepthBuffer API, we can
                    // provide the depth buffer to the system, and it will use depth information to stabilize
                    // the image at a per-pixel level.
                    HolographicCameraRenderingParameters renderingParameters = holographicFrame.GetRenderingParameters(cameraPose);

                    IDirect3DSurface interopSurface = CreateDepthTextureInteropObject(pCameraResources->GetDepthStencilTexture2D());

                    // Calling CommitDirect3D11DepthBuffer causes the system to queue Direct3D commands to
                    // read the depth buffer. It will then use that information to stabilize the image as
                    // the HolographicFrame is presented.
                    renderingParameters.CommitDirect3D11DepthBuffer(interopSurface);
                }
            }
#endif
            atLeastOneCameraRendered = true;
        }

        return atLeastOneCameraRendered;
    });
}

void AoaSampleAppMain::SaveAppState()
{
    StopAndUploadDiagnosticsAsync().get();
}

void AoaSampleAppMain::LoadAppState()
{
    TurnonDiagnosticsIfRequiredAsync();
}

// Notifies classes that use Direct3D device resources that the device resources
// need to be released before this method returns.
void AoaSampleAppMain::OnDeviceLost()
{
#ifdef DRAW_SAMPLE_CONTENT

    for (auto& renderer : m_objectRenderers)
    {
        renderer.second.ReleaseDeviceDependentResources();
    }

    m_boundsRenderer->ReleaseDeviceDependentResources();

#endif
}

// Notifies classes that use Direct3D device resources that the device resources
// may now be recreated.
void AoaSampleAppMain::OnDeviceRestored()
{
#ifdef DRAW_SAMPLE_CONTENT

    for (auto& renderer : m_objectRenderers)
    {
        renderer.second.CreateDeviceDependentResources();
    }

    m_boundsRenderer->CreateDeviceDependentResources();

#endif
}

void AoaSampleAppMain::OnLocatabilityChanged(SpatialLocator const& sender, winrt::Windows::Foundation::IInspectable const& args)
{
    switch (sender.Locatability())
    {
    case SpatialLocatability::Unavailable:
        // Holograms cannot be rendered.
    {
        winrt::hstring message(L"Warning! Positional tracking is " + std::to_wstring(int(sender.Locatability())) + L".\n");
        OutputDebugStringW(message.data());
    }
    break;

    // In the following three cases, it is still possible to place holograms using a
    // SpatialLocatorAttachedFrameOfReference.
    case SpatialLocatability::PositionalTrackingActivating:
        // The system is preparing to use positional tracking.

    case SpatialLocatability::OrientationOnly:
        // Positional tracking has not been activated.

    case SpatialLocatability::PositionalTrackingInhibited:
        // Positional tracking is temporarily inhibited. User action may be required
        // in order to restore positional tracking.
        break;

    case SpatialLocatability::PositionalTrackingActive:
        // Positional tracking is active. World-locked content can be rendered.
        break;
    }
}

void AoaSampleAppMain::OnCameraAdded(
    HolographicSpace const& sender,
    HolographicSpaceCameraAddedEventArgs const& args
)
{
    winrt::Windows::Foundation::Deferral deferral = args.GetDeferral();
    HolographicCamera holographicCamera = args.Camera();
    create_task([this, deferral, holographicCamera]()
    {
        //
        // TODO: Allocate resources for the new camera and load any content specific to
        //       that camera. Note that the render target size (in pixels) is a property
        //       of the HolographicCamera object, and can be used to create off-screen
        //       render targets that match the resolution of the HolographicCamera.
        //

        // Create device-based resources for the holographic camera and add it to the list of
        // cameras used for updates and rendering. Notes:
        //   * Since this function may be called at any time, the AddHolographicCamera function
        //     waits until it can get a lock on the set of holographic camera resources before
        //     adding the new camera. At 60 frames per second this wait should not take long.
        //   * A subsequent Update will take the back buffer from the RenderingParameters of this
        //     camera's CameraPose and use it to create the ID3D11RenderTargetView for this camera.
        //     Content can then be rendered for the HolographicCamera.
        m_deviceResources->AddHolographicCamera(holographicCamera);

        // Enable optional second camera when photo/video camera is active to render directly into the camera's view.
        if (auto photoVideoViewConfiguration = holographicCamera.Display().TryGetViewConfiguration(HolographicViewConfigurationKind::PhotoVideoCamera))
        {
            photoVideoViewConfiguration.IsEnabled(true);
        }

        // Holographic frame predictions will not include any information about this camera until
        // the deferral is completed.
        deferral.Complete();
    });
}

void AoaSampleAppMain::OnCameraRemoved(
    HolographicSpace const& sender,
    HolographicSpaceCameraRemovedEventArgs const& args
)
{
    create_task([this]()
    {
        //
        // TODO: Asynchronously unload or deactivate content resources (not back buffer
        //       resources) that are specific only to the camera that was removed.
        //
    });

    // Before letting this callback return, ensure that all references to the back buffer
    // are released.
    // Since this function may be called at any time, the RemoveHolographicCamera function
    // waits until it can get a lock on the set of holographic camera resources before
    // deallocating resources for this camera. At 60 frames per second this wait should
    // not take long.
    m_deviceResources->RemoveHolographicCamera(args.Camera());
}

void AoaSampleAppMain::OnHolographicDisplayIsAvailableChanged(winrt::Windows::Foundation::IInspectable, winrt::Windows::Foundation::IInspectable)
{
    // Get the spatial locator for the default HolographicDisplay, if one is available.
    SpatialLocator spatialLocator = nullptr;
    if (m_canGetDefaultHolographicDisplay)
    {
        HolographicDisplay defaultHolographicDisplay = HolographicDisplay::GetDefault();
        if (defaultHolographicDisplay)
        {
            spatialLocator = defaultHolographicDisplay.SpatialLocator();
        }
    }
    else
    {
        spatialLocator = SpatialLocator::GetDefault();
    }

    if (m_spatialLocator != spatialLocator)
    {
        // If the spatial locator is disconnected or replaced, we should discard all state that was
        // based on it.
        if (m_spatialLocator != nullptr)
        {
            m_spatialLocator.LocatabilityChanged(m_locatabilityChangedToken);
            m_spatialLocator = nullptr;
        }

        m_stationaryReferenceFrame = nullptr;

        if (spatialLocator != nullptr)
        {
            // Use the SpatialLocator from the default HolographicDisplay to track the motion of the device.
            m_spatialLocator = spatialLocator;

            // Respond to changes in the positional tracking state.
            m_locatabilityChangedToken = m_spatialLocator.LocatabilityChanged(std::bind(&AoaSampleAppMain::OnLocatabilityChanged, this, _1, _2));

            // The simplest way to render world-locked holograms is to create a stationary reference frame
            // based on a SpatialLocator. This is roughly analogous to creating a "world" coordinate system
            // with the origin placed at the device's position as the app is launched.
            m_stationaryReferenceFrame = m_spatialLocator.CreateStationaryFrameOfReferenceAtCurrentLocation();
        }
    }
}
