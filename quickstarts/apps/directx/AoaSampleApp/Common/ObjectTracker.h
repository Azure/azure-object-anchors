// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
#pragma once

#include <DirectXMath.h>
#include <DirectXCollision.h>

#include <winrt/Microsoft.Azure.ObjectAnchors.h>
#include <winrt/Microsoft.Azure.ObjectAnchors.Diagnostics.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Perception.Spatial.h>

#include <mutex>
#include <string>
#include <unordered_map>
#include <vector>
#include <thread>

namespace AoaSampleApp
{
    struct TrackedObject
    {
        winrt::guid ModelId;
        DirectX::XMFLOAT4X4 RelativePoseToFrameOfReference;
    };

    class ObjectTracker
    {
    public:

        ObjectTracker(winrt::Microsoft::Azure::ObjectAnchors::AccountInformation const& accountInformation);
        ~ObjectTracker();

        winrt::Windows::Foundation::IAsyncOperation<winrt::guid> AddObjectModelAsync(std::wstring const& filename);
        winrt::Microsoft::Azure::ObjectAnchors::ObjectModel GetObjectModel(winrt::guid const& id) const;

        winrt::Windows::Foundation::IAsyncAction DetectAsync(winrt::Microsoft::Azure::ObjectAnchors::ObjectSearchArea searchArea);

        winrt::Windows::Foundation::IAsyncAction StartDiagnosticsAsync();
        winrt::Windows::Foundation::IAsyncOperation<winrt::hstring> StopDiagnosticsAsync();
        winrt::Windows::Foundation::IAsyncAction UploadDiagnosticsAsync(winrt::hstring const& diagnosticsFilePath);

        std::vector<TrackedObject> GetTrackedObjects(winrt::Windows::Perception::Spatial::SpatialCoordinateSystem coordinateSystem);

        winrt::Microsoft::Azure::ObjectAnchors::ObjectInstanceTrackingMode GetInstanceTrackingMode() const;
        void SetInstanceTrackingMode(winrt::Microsoft::Azure::ObjectAnchors::ObjectInstanceTrackingMode const& mode);

        void SetMaxScaleChange(float value);

    private:

        winrt::Windows::Foundation::IAsyncAction InitializeAsync(winrt::Microsoft::Azure::ObjectAnchors::AccountInformation const& accountInformation);

        void OnInstanceStateChanged(
            winrt::Windows::Foundation::IInspectable sender,
            winrt::Microsoft::Azure::ObjectAnchors::ObjectInstanceChangedEventArgs args);

        void DetectionThreadFunc();

    private:

        shared_awaitable<winrt::Windows::Foundation::IAsyncAction> m_initOperation{ nullptr };

        winrt::Microsoft::Azure::ObjectAnchors::ObjectAnchorsSession m_session{ nullptr };
        winrt::Microsoft::Azure::ObjectAnchors::ObjectObserver m_observer{ nullptr };
        winrt::Microsoft::Azure::ObjectAnchors::Diagnostics::ObjectDiagnosticsSession m_diagnostics{ nullptr };

        std::unordered_map<winrt::guid, winrt::Microsoft::Azure::ObjectAnchors::ObjectModel> m_models;

        struct ObjectInstanceMetadata
        {
            winrt::event_token ChangedEventToken;
            winrt::Microsoft::Azure::ObjectAnchors::ObjectInstanceState State;
        };

        std::unordered_map<winrt::Windows::Foundation::IInspectable, ObjectInstanceMetadata> m_instances;

        // Object detection related fields.
        mutable std::mutex m_mutex;

        winrt::handle m_stopWorker{ nullptr };
        std::thread m_detectionWorker;

        winrt::Microsoft::Azure::ObjectAnchors::ObjectSearchArea m_searchArea{ nullptr };
        winrt::Microsoft::Azure::ObjectAnchors::ObjectInstanceTrackingMode m_trackingMode{ winrt::Microsoft::Azure::ObjectAnchors::ObjectInstanceTrackingMode::LowLatencyCoarsePosition };
        float m_maxScaleChange{ 0.1f };
    };
}
