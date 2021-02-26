// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
#pragma once

//
// Comment out this preprocessor definition to disable all of the
// sample content.
//
// To remove the content after disabling it:
//     * Remove the unused code from your app's Main class.
//     * Delete the Content folder provided with this template.
//
#define DRAW_SAMPLE_CONTENT

#include "Common/DeviceResources.h"
#include "Common/StepTimer.h"
#include "Common/ObjectTracker.h"

#ifdef DRAW_SAMPLE_CONTENT
#include "Content/PrimitiveRenderer.h"
#include "Content/SpatialInputHandler.h"
#endif

// Updates, renders, and presents holographic content using Direct3D.
namespace AoaSampleApp
{
    class AoaSampleAppMain : public IDeviceNotify
    {
    public:
        AoaSampleAppMain(std::shared_ptr<DeviceResources> const& deviceResources);
        ~AoaSampleAppMain();

        // Sets the holographic space. This is our closest analogue to setting a new window
        // for the app.
        void SetHolographicSpace(winrt::Windows::Graphics::Holographic::HolographicSpace const& holographicSpace);

        // Starts the holographic frame and updates the content.
        winrt::Windows::Graphics::Holographic::HolographicFrame Update(winrt::Windows::Graphics::Holographic::HolographicFrame const& previousFrame);

        // Renders holograms, including world-locked content.
        bool Render(winrt::Windows::Graphics::Holographic::HolographicFrame const& holographicFrame);

        // Handle saving and loading of app state owned by AppMain.
        void SaveAppState();
        void LoadAppState();

        // IDeviceNotify
        void OnDeviceLost() override;
        void OnDeviceRestored() override;

    private:
        // Asynchronously creates resources for new holographic cameras.
        void OnCameraAdded(
            winrt::Windows::Graphics::Holographic::HolographicSpace const& sender,
            winrt::Windows::Graphics::Holographic::HolographicSpaceCameraAddedEventArgs const& args);

        // Synchronously releases resources for holographic cameras that are no longer
        // attached to the system.
        void OnCameraRemoved(
            winrt::Windows::Graphics::Holographic::HolographicSpace const& sender,
            winrt::Windows::Graphics::Holographic::HolographicSpaceCameraRemovedEventArgs const& args);

        // Used to notify the app when the positional tracking state changes.
        void OnLocatabilityChanged(
            winrt::Windows::Perception::Spatial::SpatialLocator const& sender,
            winrt::Windows::Foundation::IInspectable const& args);

        // Used to respond to changes to the default spatial locator.
        void OnHolographicDisplayIsAvailableChanged(winrt::Windows::Foundation::IInspectable, winrt::Windows::Foundation::IInspectable);

        // Clears event registration state. Used when changing to a new HolographicSpace
        // and when tearing down AppMain.
        void UnregisterHolographicEventHandlers();

        // Load OU object models from application's local storage.
        winrt::Windows::Foundation::IAsyncAction LoadObjectModelAsync();

        // Check diagnostics flag and turn on diagnostics if required.
        winrt::Windows::Foundation::IAsyncAction TurnonDiagnosticsIfRequiredAsync();

        // Update object location hint based on current head pose.
        winrt::Windows::Foundation::IAsyncAction UpdateObjectSearchArea(winrt::Windows::Perception::People::HeadPose const& headPose);

        // Stop diagnostics capture and upload to Object Anchors service if a subscription account is provided.
        winrt::Windows::Foundation::IAsyncAction StopAndUploadDiagnosticsAsync();

    private:

#ifdef DRAW_SAMPLE_CONTENT
        // A dictionary of renderers to render object's bounding box, with object model id as the key.
        struct ObjectRenderer
        {
            std::unique_ptr<PrimitiveRenderer> BoundingBoxRenderer;
            std::unique_ptr<PrimitiveRenderer> PointCloudRenderer;

            bool IsActive() const;
            void SetActive(bool flag);
            void SetTransform(DirectX::XMFLOAT4X4 const& frameOfReferenceFromObject);

            void CreateDeviceDependentResources();
            void ReleaseDeviceDependentResources();
            void Render();

            DirectX::XMFLOAT3 GetPosition() const;
        };

        std::unordered_map<winrt::guid, ObjectRenderer>             m_objectRenderers;
        std::unique_ptr<PrimitiveRenderer>                          m_boundsRenderer;

        // Listens for the Pressed spatial input event.
        std::shared_ptr<SpatialInputHandler>                        m_spatialInputHandler;
#endif

        // Cached pointer to device resources.
        std::shared_ptr<DeviceResources>                            m_deviceResources;

        // Render loop timer.
        StepTimer                                                   m_timer;

        // Represents the holographic space around the user.
        winrt::Windows::Graphics::Holographic::HolographicSpace     m_holographicSpace = nullptr;

        // SpatialLocator that is attached to the default HolographicDisplay.
        winrt::Windows::Perception::Spatial::SpatialLocator         m_spatialLocator = nullptr;

        // A stationary reference frame based on m_spatialLocator.
        winrt::Windows::Perception::Spatial::SpatialStationaryFrameOfReference m_stationaryReferenceFrame = nullptr;

        // Event registration tokens.
        winrt::event_token                                          m_cameraAddedToken;
        winrt::event_token                                          m_cameraRemovedToken;
        winrt::event_token                                          m_locatabilityChangedToken;
        winrt::event_token                                          m_holographicDisplayIsAvailableChangedEventToken;


        // Cache whether or not the HolographicCamera.Display property can be accessed.
        bool                                                        m_canGetHolographicDisplayForCamera = false;

        // Cache whether or not the HolographicDisplay.GetDefault() method can be called.
        bool                                                        m_canGetDefaultHolographicDisplay = false;

        // Cache whether or not the HolographicCameraRenderingParameters.CommitDirect3D11DepthBuffer() method can be called.
        bool                                                        m_canCommitDirect3D11DepthBuffer = false;

        // Cache whether or not the HolographicFrame.WaitForNextFrameReady() method can be called.
        bool                                                        m_canUseWaitForNextFrameReadyAPI = false;

        // Object tracker.
        std::unique_ptr<ObjectTracker>                              m_objectTrackerPtr;
        winrt::Microsoft::Azure::ObjectAnchors::ObjectSearchArea    m_lastSearchArea{ nullptr };

        winrt::Windows::Foundation::IAsyncAction                    m_initializeOperation{ nullptr };
        winrt::Windows::Foundation::IAsyncAction                    m_searchAreaOperation{ nullptr };
    };
}
