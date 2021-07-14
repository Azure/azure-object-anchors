// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
#pragma once

#include <algorithm>
#include <array>
#include <chrono>
#include <ctime>
#include <d2d1_2.h>
#include <d3d11_4.h>
#include <DirectXCollision.h>
#include <DirectXColors.h>
#include <DirectXMath.h>
#include <dwrite_2.h>
#include <functional>	// For bind
#include <future>
#include <iomanip>
#include <mutex>
#include <numeric>
#include <sstream>
#include <unordered_map>
#include <vector>
#include <thread>
#include <time.h>
#include <wincodec.h>
#include <WindowsNumerics.h>

#include <Windows.Graphics.Directx.Direct3D11.Interop.h>
#include <wrl/client.h>

#include <winrt/Windows.ApplicationModel.Activation.h>
#include <winrt/Windows.ApplicationModel.Core.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.Foundation.Metadata.h>
#include <winrt/Windows.Gaming.Input.h>
#include <winrt/Windows.Graphics.Display.h>
#include <winrt/Windows.Graphics.Holographic.h>
#include <winrt/Windows.Perception.People.h>
#include <winrt/Windows.Perception.Spatial.h>
#include <winrt/Windows.Perception.Spatial.Preview.h>
#include <winrt/Windows.Security.Authorization.AppCapabilityAccess.h>
#include <winrt/Windows.Storage.h>
#include <winrt/Windows.Storage.Streams.h>
#include <winrt/Windows.UI.Core.h>
#include <winrt/Windows.UI.Input.Spatial.h>

#include "Common/FileUtilities.h"
#include "Common/SafeCast.h"

template <typename TAsync>
struct shared_awaitable
{
    TAsync m_async{ nullptr };
    std::mutex m_lock;

    shared_awaitable& operator=(TAsync&& async)
    {
        std::unique_lock lock(m_lock);
        m_async = std::move(async);
        return *this;
    }

    operator bool() { std::unique_lock lock(m_lock); return bool(m_async); }

    struct awaiter
    {
        TAsync m_async;

        bool await_ready() const
        {
            return m_async.Status() != winrt::Windows::Foundation::AsyncStatus::Started;
        }

        void await_suspend(std::experimental::coroutine_handle<> onReady)
        {
            std::thread([&async = m_async, context = winrt::apartment_context{}, onReady]() mutable
            {
                async.get();
                context.await_suspend(onReady);
            }).detach();
        }

        decltype(auto) await_resume()
        {
            return m_async.GetResults();
        }
    };

    auto operator co_await() { std::unique_lock lock(m_lock); return awaiter{ m_async }; }
};