// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
#include "pch.h"
#include "ObjectTracker.h"
#include <PathCch.h>
#include <ppl.h>
#include <winrt/Windows.Storage.AccessCache.h>

using namespace std;
using namespace DirectX;
using namespace winrt;
using namespace winrt::Windows::Foundation::Numerics;
using namespace winrt::Windows::Perception::Spatial;
using namespace winrt::Windows::Perception::Spatial::Preview;
using namespace winrt::Windows::Storage;
using namespace winrt::Microsoft::Azure::ObjectAnchors;
using namespace winrt::Microsoft::Azure::ObjectAnchors::SpatialGraph;

namespace AoaSampleApp
{
    ObjectTracker::ObjectTracker(AccountInformation const& accountInformation)
        : m_stopWorker(::CreateEvent(nullptr, true, false, nullptr))    // manual reset event
        , m_detectionWorker(&ObjectTracker::DetectionThreadFunc, this)
    {
        m_initOperation = InitializeAsync(accountInformation);
    }

    ObjectTracker::~ObjectTracker()
    {
        winrt::check_bool(::SetEvent(m_stopWorker.get()));
        m_detectionWorker.join();

        lock_guard lock(m_mutex);

        m_diagnostics = nullptr;

        for (auto& [instance, metadata] : m_instances)
        {
            instance.Close();
        }
        m_instances.clear();

        for (auto& [modelId, model] : m_models)
        {
            model.Close();
        }
        m_models.clear();

        m_observer.Close();
        m_observer = nullptr;
    }

    winrt::Windows::Foundation::IAsyncAction ObjectTracker::InitializeAsync(AccountInformation const& accountInformation)
    {
        if (!ObjectObserver::IsSupported())
        {
            throw hresult_not_implemented();
        }

        auto status = co_await ObjectObserver::RequestAccessAsync();
        if (status != ObjectObserverAccessStatus::Allowed)
        {
            throw hresult_access_denied();
        }

        m_session = ObjectAnchorsSession(accountInformation);

        m_observer = m_session.CreateObjectObserver();
    }

    winrt::Windows::Foundation::IAsyncOperation<guid> ObjectTracker::AddObjectModelAsync(winrt::Windows::Storage::StorageFile file)
    {
        co_await m_initOperation;

        auto buffer = co_await winrt::Windows::Storage::FileIO::ReadBufferAsync(file);
        auto model = co_await m_observer.LoadObjectModelAsync(
            winrt::array_view(buffer.data(), buffer.Length()));

        auto id = model.Id();
        m_models.emplace(id, model);

        co_return id;
    }

    ObjectModel ObjectTracker::GetObjectModel(guid const& id) const
    {
        auto it = m_models.find(id);

        if (it == m_models.cend())
        {
            return nullptr;
        }
        else
        {
            return it->second;
        }
    }

    winrt::Windows::Foundation::IAsyncAction ObjectTracker::DetectAsync(SpatialGraphInteropFrameOfReferencePreview const& interopReferenceFrame, ObjectSearchArea const& searchArea)
    {
        co_await m_initOperation;

        if (m_models.empty())
        {
            co_return;
        }

        //
        // Create object queries with the provided search area.
        //

        lock_guard lock(m_mutex);
        m_interopReferenceFrame = interopReferenceFrame;
        m_searchArea = searchArea;

        //
        // Close instances being tracked to enforce using latest detection results.
        //

        for (auto& [instance, metadata] : m_instances)
        {
            instance.Close();
        }

        m_instances.clear();
    }

    winrt::Windows::Foundation::IAsyncAction ObjectTracker::StartDiagnosticsAsync()
    {
        if (m_diagnostics == nullptr)
        {
            co_await winrt::resume_background();
            m_diagnostics = winrt::Microsoft::Azure::ObjectAnchors::Diagnostics::ObjectDiagnosticsSession(m_observer, (std::numeric_limits<uint32_t>::max)());
        }
    }

    winrt::Windows::Foundation::IAsyncOperation<winrt::hstring> ObjectTracker::StopDiagnosticsAsync()
    {
        hstring diagnosticsFilePath;

        if (m_diagnostics != nullptr)
        {
            // Create a diagnostics folder named by current time.
            auto diagnosticsFilename = StringToWideString(FormatDateTime(std::time(nullptr))) + L".zip";

            static StorageFolder diagnosticsFolder{ nullptr };
            if (!diagnosticsFolder)
            {
                constexpr auto token = L"diagnosticsFolder";
                auto futureAccessList = AccessCache::StorageApplicationPermissions::FutureAccessList();
                if (futureAccessList.ContainsItem(token))
                {
                    try
                    {
                        diagnosticsFolder = co_await futureAccessList.GetFolderAsync(token);
                    }
                    catch (...)
                    {
                        // Folder may have been deleted by the user; recreate it below.
                    }
                }

                if (!diagnosticsFolder)
                {
                    diagnosticsFolder = co_await DownloadsFolder::CreateFolderAsync(L"Diagnostics");
                    futureAccessList.AddOrReplace(token, diagnosticsFolder);
                }
            }

            diagnosticsFilePath = PathJoin(diagnosticsFolder.Path(), diagnosticsFilename);

            co_await m_diagnostics.CloseAsync(diagnosticsFilePath);

            m_diagnostics = nullptr;
        }

        co_return diagnosticsFilePath;
    }

    winrt::Windows::Foundation::IAsyncAction ObjectTracker::UploadDiagnosticsAsync(winrt::hstring const& diagnosticsFilePath)
    {
        winrt::check_bool(!diagnosticsFilePath.empty());

        co_await winrt::Microsoft::Azure::ObjectAnchors::Diagnostics::ObjectDiagnosticsSession::UploadDiagnosticsAsync(diagnosticsFilePath, m_session);
    }

    vector<TrackedObject> ObjectTracker::GetTrackedObjects(SpatialCoordinateSystem coordinateSystem)
    {
        lock_guard lock(m_mutex);

        vector<TrackedObject> objects;
        objects.reserve(m_instances.size());

        for(auto& [instance, metadata] : m_instances)
        {
            auto coordinateSystemToPlacement = coordinateSystem.TryGetTransformTo(metadata.PlacementCoordinateSystem);
            if (coordinateSystemToPlacement)
            {
                TrackedObject obj(metadata.Placement);
                obj.ModelId = instance.ModelId();
                obj.CoordinateSystemToPlacement = coordinateSystemToPlacement.Value();
                objects.emplace_back(obj);
            }
        }

        return objects;
    }

    ObjectInstanceTrackingMode ObjectTracker::GetInstanceTrackingMode() const
    {
        return m_trackingMode;
    }

    void ObjectTracker::SetInstanceTrackingMode(ObjectInstanceTrackingMode const& mode)
    {
        lock_guard<mutex> lock(m_mutex);

        m_trackingMode = mode;

        for (auto& [instance, metadata] : m_instances)
        {
            instance.Mode(m_trackingMode);
        }
    }

    void ObjectTracker::SetMaxScaleChange(float value)
    {
        winrt::check_bool(value >= 0.0f && value < 1.0f);

        m_maxScaleChange = value;
    }


    void ObjectTracker::OnInstanceStateChanged(winrt::Windows::Foundation::IInspectable sender, ObjectInstanceChangedEventArgs args)
    {
        lock_guard lock(m_mutex);

        auto instance = sender.as<ObjectInstance>();
        auto it = m_instances.find(instance);
        winrt::check_bool(it != m_instances.cend());

        // Query tracking state, close an instance if it's lost in tracking.
        auto placement = instance.TryGetCurrentPlacement({ m_interopReferenceFrame.NodeId(), m_interopReferenceFrame.CoordinateSystemToNodeTransform() });

        if (placement == nullptr)
        {
            instance.Close();

            m_instances.erase(it);
        }
        else
        {
            it->second.Placement = placement;
            it->second.PlacementCoordinateSystem = m_interopReferenceFrame.CoordinateSystem();
        }
    }

    void ObjectTracker::DetectionThreadFunc()
    {
        constexpr uint32_t c_timeoutMilliSecond = 10;
        constexpr uint32_t c_sleepTimeMilliSecond = 100;

        // Event handle should be created.
        winrt::check_bool(bool{ m_stopWorker });

        for (;;)
        {
            DWORD dwWaitResult = WaitForSingleObject(m_stopWorker.get(), c_timeoutMilliSecond);

            if (dwWaitResult == WAIT_OBJECT_0)
            {
                // Exit detection thread when 'stop' event fired.
                break;
            }

            // Create query for models not detected yet.
            SpatialGraphInteropFrameOfReferencePreview interopReferenceFrame{ nullptr };
            vector<ObjectQuery> queries;
            {
                lock_guard lock(m_mutex);

                interopReferenceFrame = m_interopReferenceFrame;
                if (m_searchArea != nullptr)
                {
                    for (auto const& [modelId, model] : m_models)
                    {
                        bool found = false;
                        for (auto const& [instance, metadata] : m_instances)
                        {
                            if (modelId == instance.ModelId())
                            {
                                found = true;
                                break;
                            }
                        }

                        if (!found)
                        {
                            auto query = ObjectQuery(model);
                            query.MaxScaleChange(m_maxScaleChange);
                            query.SearchAreas().Append(m_searchArea);

                            queries.emplace_back(std::move(query));
                        }
                    }
                }
            }

            //
            // Run detection if required, otherwise sleep for a while.
            //

            if (queries.empty())
            {
                Sleep(c_sleepTimeMilliSecond);
            }
            else
            {
                auto detectedObjects = m_observer.DetectAsync(queries).get();

                // Release resources held by a query.
                queries.clear();

                //
                // Add instances to the list.
                //

                decltype(m_instances) newInstances;
                for (const auto& inst : detectedObjects)
                {
                    auto placement = inst.TryGetCurrentPlacement({ interopReferenceFrame.NodeId(), interopReferenceFrame.CoordinateSystemToNodeTransform() });

                    if (placement != nullptr)
                    {
                        inst.Mode(m_trackingMode);

                        auto token = inst.Changed(
                            ObjectInstanceChangedHandler(
                                bind(&ObjectTracker::OnInstanceStateChanged, this, placeholders::_1, placeholders::_2)));

                        newInstances.emplace(inst, ObjectInstanceMetadata{
                            inst.Changed(winrt::auto_revoke, bind(&ObjectTracker::OnInstanceStateChanged, this, placeholders::_1, placeholders::_2)),
                            placement,
                            interopReferenceFrame.CoordinateSystem()
                        });
                    }
                    else
                    {
                        inst.Close();
                    }
                }

                lock_guard lock(m_mutex);
                newInstances.merge(std::move(m_instances)); // splice in old instances; new instances are preserved
                m_instances = std::move(newInstances);
            }
        }
    }
}
