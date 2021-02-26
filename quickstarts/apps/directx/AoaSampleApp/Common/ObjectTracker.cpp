// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
#include "pch.h"
#include "ObjectTracker.h"
#include <PathCch.h>
#include <ppl.h>
#include <robuffer.h>

using namespace std;
using namespace DirectX;
using namespace winrt;
using namespace winrt::Windows::Foundation::Numerics;
using namespace winrt::Windows::Perception::Spatial;
using namespace winrt::Windows::Storage;
using namespace winrt::Microsoft::Azure::ObjectAnchors;
using namespace winrt::Microsoft::Azure::ObjectAnchors::SpatialGraph;

namespace
{
    winrt::Windows::Foundation::IReference<float4x4> TryComputeTransformTo(SpatialGraphLocation const& from, SpatialGraphLocation const& to)
    {
        winrt::Windows::Foundation::IReference<float4x4> relativePose{ nullptr };

        auto coordSystemOfFrom = Preview::SpatialGraphInteropPreview::CreateCoordinateSystemForNode(from.NodeId, from.Position, from.Orientation);
        auto coordSystemOfTo = Preview::SpatialGraphInteropPreview::CreateCoordinateSystemForNode(to.NodeId, to.Position, to.Orientation);

        if (coordSystemOfFrom != nullptr && coordSystemOfTo)
        {
            relativePose = coordSystemOfFrom.TryGetTransformTo(coordSystemOfTo);
        }

        return relativePose;
    }
}

namespace AoaSampleApp
{
    ObjectTracker::ObjectTracker()
        : m_stopWorker(::CreateEvent(nullptr, true, false, nullptr))    // manual reset event
        , m_detectionWorker(&ObjectTracker::DetectionThreadFunc, this)
    {
        m_initOperation = InitializeAsync();
    }

    ObjectTracker::~ObjectTracker()
    {
        winrt::check_bool(::SetEvent(m_stopWorker.get()));
        m_detectionWorker.join();

        lock_guard lock(m_mutex);

        m_diagnostics = nullptr;

        for (auto& entry : m_instances)
        {
            auto instance = entry.first.try_as<ObjectInstance>();
            instance.Changed(entry.second.ChangedEventToken);
            instance.Close();
        }
        m_instances.clear();

        for (auto& model : m_models)
        {
            model.second.Close();
        }
        m_models.clear();

        m_observer.Close();
        m_observer = nullptr;
    }

    winrt::Windows::Foundation::IAsyncAction ObjectTracker::InitializeAsync()
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

        m_observer = ObjectObserver();
    }

    winrt::Windows::Foundation::IAsyncOperation<guid> ObjectTracker::AddObjectModelAsync(wstring const& filename)
    {
        co_await m_initOperation;

        wchar_t fullFilePath[1024];
        GetFullPathNameW(filename.data(), _countof(fullFilePath), fullFilePath, nullptr);

        auto buffer{ co_await winrt::Windows::Storage::PathIO::ReadBufferAsync(fullFilePath) };
        const unsigned int bufferSize = static_cast<unsigned int>(buffer.Length());

        uint8_t* bufferData{};
        buffer.as<::Windows::Storage::Streams::IBufferByteAccess>()->Buffer(&bufferData);
        auto model = co_await m_observer.LoadObjectModelAsync(
            winrt::array_view(bufferData, bufferData + bufferSize));

        auto id = model.Id();
        m_models.emplace(id, model);

        return id;
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

    winrt::Windows::Foundation::IAsyncAction ObjectTracker::DetectAsync(ObjectSearchArea searchArea)
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

        m_searchArea = searchArea;

        //
        // Close instances being tracked to enforce using latest detection results.
        //

        for (auto& entry : m_instances)
        {
            auto instance = entry.first.try_as<ObjectInstance>();

            instance.Changed(entry.second.ChangedEventToken);
            instance.Close();
        }

        m_instances.clear();
    }

    winrt::Windows::Foundation::IAsyncAction ObjectTracker::StartDiagnosticsAsync()
    {
        if (m_diagnostics == nullptr)
        {
            m_diagnostics = winrt::Microsoft::Azure::ObjectAnchors::Diagnostics::ObjectDiagnosticsSession(m_observer, (std::numeric_limits<uint32_t>::max)());
        }

        co_return;
    }

    winrt::Windows::Foundation::IAsyncOperation<winrt::hstring> ObjectTracker::StopDiagnosticsAsync()
    {
        wstring diagnosticsFilePath;

        if (m_diagnostics != nullptr)
        {
            // Create a diagnostics folder named by current time.
            auto diagnosticsFilename = StringToWideString(FormatDateTime(std::time(nullptr))) + L".zip";

            diagnosticsFilePath = PathJoin(ApplicationData::Current().TemporaryFolder().Path().c_str(), diagnosticsFilename);

            co_await m_diagnostics.CloseAsync(diagnosticsFilePath.c_str());

            m_diagnostics = nullptr;
        }

        co_return diagnosticsFilePath.c_str();
    }

    winrt::Windows::Foundation::IAsyncAction ObjectTracker::UploadDiagnosticsAsync(winrt::hstring const& diagnosticsFilePath, winrt::hstring const& accountId, winrt::hstring const& accountKey, winrt::hstring const& accountDomain)
    {
        winrt::check_bool(!diagnosticsFilePath.empty());
        winrt::check_bool(FileExists(WideStringToString(diagnosticsFilePath.c_str())));

        co_await winrt::Microsoft::Azure::ObjectAnchors::Diagnostics::ObjectDiagnosticsSession::UploadDiagnosticsAsync(diagnosticsFilePath, accountId, accountKey, accountDomain);
    }

    vector<TrackedObject> ObjectTracker::GetTrackedObjects(SpatialCoordinateSystem coordinateSystem)
    {
        lock_guard lock(m_mutex);

        vector<TrackedObject> objects;
        objects.reserve(m_instances.size());

        for(auto const& entry : m_instances)
        {
            auto const& state = entry.second.State;

            auto frameOfStaticNode = Preview::SpatialGraphInteropPreview::CreateCoordinateSystemForNode(state.Center.NodeId);
            if (frameOfStaticNode != nullptr)
            {
                auto referenceFromStaticNode = frameOfStaticNode.TryGetTransformTo(coordinateSystem);
                if (referenceFromStaticNode != nullptr)
                {
                    // reference_from_object = reference_from_node * node_from_object
                    XMMATRIX referenceFromObject =
                        XMMatrixScalingFromVector(XMLoadFloat3(&reinterpret_cast<XMFLOAT3 const&>(state.ScaleChange))) *
                        XMMatrixRotationQuaternion(XMLoadFloat4(&reinterpret_cast<XMFLOAT4 const&>(state.Center.Orientation))) *
                        XMMatrixTranslationFromVector(XMLoadFloat3(&reinterpret_cast<XMFLOAT3 const&>(state.Center.Position))) *
                        XMLoadFloat4x4(&referenceFromStaticNode.Value());

                    auto instance = entry.first.try_as<ObjectInstance>();

                    TrackedObject obj;
                    obj.ModelId = instance.ModelId();
                    XMStoreFloat4x4(&obj.RelativePoseToFrameOfReference, referenceFromObject);

                    objects.emplace_back(obj);
                }
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

        for (auto& entry : m_instances)
        {
            auto inst = entry.first.try_as<ObjectInstance>();
            inst.Mode(m_trackingMode);
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

        auto it = m_instances.find(sender);
        winrt::check_bool(it != m_instances.cend());

        // Query tracking state, close an instance if it's lost in tracking.
        auto instance = sender.try_as<ObjectInstance>();
        auto state = instance.TryGetCurrentState();

        if (state == nullptr)
        {
            instance.Changed(it->second.ChangedEventToken);
            instance.Close();

            m_instances.erase(it);
        }
        else
        {
            it->second.State = state.Value();
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
            vector<ObjectQuery> queries;
            {
                lock_guard lock(m_mutex);

                if (m_searchArea != nullptr)
                {
                    for (auto const& modelKV : m_models)
                    {
                        bool found = false;
                        for (auto const& instKV : m_instances)
                        {
                            auto inst = instKV.first.try_as<ObjectInstance>();
                            if (modelKV.first == inst.ModelId())
                            {
                                found = true;
                                break;
                            }
                        }

                        if (!found)
                        {
                            auto query = ObjectQuery(modelKV.second);
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

                lock_guard lock(m_mutex);

                for (auto& inst : detectedObjects)
                {
                    auto state = inst.TryGetCurrentState();

                    if (state != nullptr)
                    {
                        inst.Mode(m_trackingMode);

                        auto token = inst.Changed(
                            ObjectInstanceChangedHandler(
                                bind(&ObjectTracker::OnInstanceStateChanged, this, placeholders::_1, placeholders::_2)));

                        m_instances[inst] = { token, state.Value() };
                    }
                    else
                    {
                        inst.Close();
                    }
                }
            }
        }
    }
}
